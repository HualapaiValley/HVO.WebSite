using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Asp.Versioning;
using HVO.DataModels.Data;
using HVO.DataModels.Models.V9;
using HVO.Edge.Contracts;
using HVO.Edge.Contracts.PowerSystem;
using HVO.WebSite.v9.Infrastructure;
using HVO.WebSite.v9.Models;
using HVO.WebSite.v9.Services;
using HVO.WebSite.v9.Telemetry;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace HVO.WebSite.v9.Controllers;

/// <summary>
/// Power ingest API for normalized edge gateway power snapshots.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/power")]
[Tags("Power")]
public class PowerIngestController : ControllerBase
{
    /// <summary>
    /// Maximum number of records per ingest batch. Values above this threshold
    /// risk exceeding SQL Server's 2100-parameter limit in IN-list deduplication
    /// queries and are rejected with HTTP 400.
    /// </summary>
    public const int MaxBatchSize = 500;

    private const int MaxInventoryDevices = 50;
    private const int MaxConfigurationSettings = 200;
    private const int MaxCommandCapabilities = 100;
    private const int MaxEnergyCounters = 20;
    private const int MaxPvStrings = 8;
    private const int MaxInverterStatuses = 20;
    private const int MaxInverterTemperatures = 20;
    private const int MaxMpptTrackers = 32;
    private const int MaxMpptTemperatures = 32;
    private const int MaxMpptDiagnostics = 100;
    private const int MaxGatewayAlerts = 50;
    private const int MaxSnapshotStringLength = 256;
    private const int MaxSnapshotValueLength = 1024;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HvoV9DbContext _db;
    private readonly ILogger<PowerIngestController> _logger;
    private readonly IPowerReadingIngestService _readingIngestService;
    private readonly IPowerSystemSnapshotProvider _snapshotProvider;
    private readonly IPowerInventoryConfigurationProvider _inventoryConfigurationProvider;

    public PowerIngestController(
        HvoV9DbContext db,
        ILogger<PowerIngestController> logger,
        IPowerReadingIngestService readingIngestService,
        IPowerSystemSnapshotProvider snapshotProvider,
        IPowerInventoryConfigurationProvider inventoryConfigurationProvider)
    {
        _db = db;
        _logger = logger;
        _readingIngestService = readingIngestService;
        _snapshotProvider = snapshotProvider;
        _inventoryConfigurationProvider = inventoryConfigurationProvider;
    }

    /// <summary>
    /// Ingests a batch of normalized power-system snapshots.
    /// </summary>
    /// <remarks>
    /// Idempotent by SourceId + RecordedAtUtc. Duplicate records are skipped.
    /// Requires an API key with the <c>ingest:power</c> scope claim.
    /// </remarks>
    [HttpPost("readings")]
    [Authorize(Policy = "PowerIngest")]
    [ProducesResponseType(typeof(PowerReadingBatchResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [Produces("application/json")]
    public async Task<ActionResult<PowerReadingBatchResponse>> IngestReadings(
        [FromBody] JsonElement batch,
        CancellationToken ct)
    {
        // Detect and unwrap CloudEvents 1.0 envelope format, or use raw payloads for backward compat
        List<JsonElement> rawPayloads;
        if (batch.ValueKind == JsonValueKind.Array)
        {
            var first = batch.EnumerateArray().FirstOrDefault();
            if (first.ValueKind == JsonValueKind.Object && CloudEventsBatchUnwrapper.IsCloudEventsBatch(first))
            {
                var (data, errors) = CloudEventsBatchUnwrapper.UnwrapBatch(batch, _logger);
                if (errors.Count > 0 && data.Count == 0)
                    return ValidationProblem(detail: $"Failed to unwrap CloudEvents batch: {string.Join("; ", errors.Take(3))}");
                rawPayloads = data;
            }
            else
            {
                rawPayloads = batch.EnumerateArray().Select(e => e.Clone()).ToList();
            }
        }
        else
        {
            return ValidationProblem(detail: "Request body must be a JSON array.");
        }

        if (rawPayloads.Count == 0)
            return ValidationProblem(detail: "Batch must contain at least one valid record.");

        if (rawPayloads.Count > MaxBatchSize)
            return ValidationProblem(detail: $"Batch size {rawPayloads.Count} exceeds the maximum of {MaxBatchSize} records. Split the batch or reduce the outbox batch size on the gateway.");

        // Deserialize each (possibly unwrapped) payload
        var requests = new List<PowerReadingPayload>();
        var deserFailures = new List<PowerReadingBatchFailure>();
        foreach (var (payload, index) in rawPayloads.Select((p, i) => (p, i)))
        {
            try
            {
                var request = payload.Deserialize<PowerReadingPayload>(JsonOptions);
                if (request is not null)
                    requests.Add(request);
                else
                {
                    var failure = ExtractPowerFailureMetadata(payload, $"Record at index {index} deserialized to null.");
                    deserFailures.Add(failure);
                    _logger.LogWarning("Power record at index {Index} deserialized to null — reported as failure", index);
                }
            }
            catch (JsonException ex)
            {
                var failure = ExtractPowerFailureMetadata(payload, $"Record at index {index}: invalid JSON — {ex.Message}");
                deserFailures.Add(failure);
                _logger.LogWarning(ex, "Power record at index {Index} has invalid JSON — reported as failure", index);
            }
        }

        if (requests.Count == 0 && deserFailures.Count > 0)
        {
            return CreatedAtAction(nameof(IngestReadings), new { },
                new PowerReadingBatchResponse { Inserted = 0, Skipped = 0, Failed = deserFailures });
        }

        if (requests.Count == 0)
        {
            return CreatedAtAction(nameof(IngestReadings), new { },
                new PowerReadingBatchResponse { Inserted = 0, Skipped = 0, Failed = [] });
        }

        if (!await IngestSourceAuthority.CanWriteAllAsync(
            _db, User, requests.Select(static request => (request.SourceId, request.SourceSystem)), ct))
            return Forbid();

        var result = await _readingIngestService.IngestReadingsAsync(requests, ct);
        if (result.PersistenceFailed)
        {
            return Problem(
                detail: "An error occurred while persisting the power batch. Retry is safe — duplicate records will be skipped.",
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Power Batch Ingest Failed");
        }

        return CreatedAtAction(nameof(GetRecentReadings), new { },
            new PowerReadingBatchResponse
            {
                Inserted = result.Response.Inserted,
                Skipped = result.Response.Skipped,
                Failed = deserFailures.Count > 0
                    ? [.. result.Response.Failed, .. deserFailures]
                    : result.Response.Failed
            });
    }

    private static PowerReadingBatchFailure ExtractPowerFailureMetadata(
        JsonElement payload, string error)
    {
        string? sourceId = null;
        DateTime? recordedAtUtc = null;
        if (payload.ValueKind == JsonValueKind.Object)
        {
            if (payload.TryGetProperty("sourceId", out var sid) && sid.ValueKind == JsonValueKind.String)
                sourceId = sid.GetString();
            if (payload.TryGetProperty("recordedAtUtc", out var ra) && ra.ValueKind == JsonValueKind.String)
                recordedAtUtc = ra.GetDateTime();
            else if (payload.TryGetProperty("recordedAt", out var rad) && rad.ValueKind == JsonValueKind.String)
                recordedAtUtc = rad.GetDateTime();
        }

        return new PowerReadingBatchFailure
        {
            SourceId = sourceId ?? string.Empty,
            RecordedAtUtc = recordedAtUtc ?? DateTime.UtcNow,
            Error = error,
        };
    }

    [HttpPost("device-inventory")]
    [Authorize(Policy = "PowerIngest")]
    [ProducesResponseType(typeof(PowerSnapshotIngestResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [Produces("application/json")]
    public async Task<ActionResult<PowerSnapshotIngestResponse>> IngestDeviceInventory(
        [FromBody] PowerDeviceInventoryPayload request,
        CancellationToken ct)
    {
        var sourceId = NormalizeSourceId(request.SourceId);
        var recordedAt = NormalizeRecordedAt(request.RecordedAtUtc);
        var validationResults = ValidateCommonSnapshot(sourceId, request.SourceSystem, request.DeviceId, recordedAt);
        ValidateDeviceInventory(validationResults, request);
        if (validationResults.Count > 0)
            return BadRequest(new ValidationProblemDetails(ToValidationDictionary(validationResults)));

        var payloadJson = JsonSerializer.Serialize(request, JsonOptions);
        var payloadHash = ComputeHash(RemoveRecordedAt(payloadJson));
        if (await _db.PowerDeviceInventorySnapshots.AnyAsync(
            r => r.SourceId == sourceId && (r.RecordedAt == recordedAt || r.PayloadHash == payloadHash), ct))
        {
            return CreatedAtAction(nameof(GetLatestDeviceInventory), new { sourceId }, new PowerSnapshotIngestResponse { Skipped = true });
        }

        _db.PowerDeviceInventorySnapshots.Add(new PowerDeviceInventorySnapshot
        {
            SourceId = sourceId,
            SourceSystem = NormalizeSourceSystem(request.SourceSystem),
            DeviceId = NormalizeOptional(request.DeviceId),
            RecordedAt = recordedAt,
            RestMetricCount = request.RestMetricCount,
            MqttEntityCount = request.MqttEntityCount,
            MqttStateTopicCount = request.MqttStateTopicCount,
            PayloadJson = payloadJson,
            PayloadHash = payloadHash,
            CreatedAt = DateTime.UtcNow,
        });

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            return CreatedAtAction(nameof(GetLatestDeviceInventory), new { sourceId }, new PowerSnapshotIngestResponse { Skipped = true });
        }

        return CreatedAtAction(nameof(GetLatestDeviceInventory), new { sourceId }, new PowerSnapshotIngestResponse { Inserted = true });
    }

    [HttpPost("configuration")]
    [Authorize(Policy = "PowerIngest")]
    [ProducesResponseType(typeof(PowerSnapshotIngestResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [Produces("application/json")]
    public async Task<ActionResult<PowerSnapshotIngestResponse>> IngestConfiguration(
        [FromBody] PowerConfigurationPayload request,
        CancellationToken ct)
    {
        var sourceId = NormalizeSourceId(request.SourceId);
        var recordedAt = NormalizeRecordedAt(request.RecordedAtUtc);
        var validationResults = ValidateCommonSnapshot(sourceId, request.SourceSystem, request.DeviceId, recordedAt);
        ValidateConfiguration(validationResults, request);
        if (validationResults.Count > 0)
            return BadRequest(new ValidationProblemDetails(ToValidationDictionary(validationResults)));

        var payloadJson = JsonSerializer.Serialize(request, JsonOptions);
        var payloadHash = ComputeHash(RemoveRecordedAt(payloadJson));
        if (await _db.PowerConfigurationSnapshots.AnyAsync(
            r => r.SourceId == sourceId && (r.RecordedAt == recordedAt || r.PayloadHash == payloadHash), ct))
        {
            return CreatedAtAction(nameof(GetLatestConfiguration), new { sourceId }, new PowerSnapshotIngestResponse { Skipped = true });
        }

        _db.PowerConfigurationSnapshots.Add(new PowerConfigurationSnapshot
        {
            SourceId = sourceId,
            SourceSystem = NormalizeSourceSystem(request.SourceSystem),
            DeviceId = NormalizeOptional(request.DeviceId),
            RecordedAt = recordedAt,
            SettingCount = request.Settings.Count,
            CommandCapabilityCount = request.CommandCapabilities.Count,
            PayloadJson = payloadJson,
            PayloadHash = payloadHash,
            CreatedAt = DateTime.UtcNow,
        });

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            return CreatedAtAction(nameof(GetLatestConfiguration), new { sourceId }, new PowerSnapshotIngestResponse { Skipped = true });
        }

        return CreatedAtAction(nameof(GetLatestConfiguration), new { sourceId }, new PowerSnapshotIngestResponse { Inserted = true });
    }

    [HttpPost("energy")]
    [Authorize(Policy = "PowerIngest")]
    [ProducesResponseType(typeof(PowerSnapshotIngestResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [Produces("application/json")]
    public async Task<ActionResult<PowerSnapshotIngestResponse>> IngestEnergy(
        [FromBody] PowerEnergyPayload request,
        CancellationToken ct)
    {
        var sourceId = NormalizeSourceId(request.SourceId);
        var recordedAt = NormalizeRecordedAt(request.RecordedAtUtc);
        var validationResults = ValidateCommonSnapshot(sourceId, request.SourceSystem, request.DeviceId, recordedAt);
        ValidateEnergy(validationResults, request);
        if (validationResults.Count > 0)
            return BadRequest(new ValidationProblemDetails(ToValidationDictionary(validationResults)));

        var payloadJson = JsonSerializer.Serialize(request, JsonOptions);
        var payloadHash = ComputeHash(RemoveRecordedAt(payloadJson));
        if (await _db.PowerEnergySnapshots.AnyAsync(
            r => r.SourceId == sourceId && (r.RecordedAt == recordedAt || r.PayloadHash == payloadHash), ct))
        {
            return CreatedAtAction(nameof(GetLatestEnergy), new { sourceId }, new PowerSnapshotIngestResponse { Skipped = true });
        }

        _db.PowerEnergySnapshots.Add(new PowerEnergySnapshot
        {
            SourceId = sourceId,
            SourceSystem = NormalizeSourceSystem(request.SourceSystem),
            DeviceId = NormalizeOptional(request.DeviceId),
            RecordedAt = recordedAt,
            CounterCount = request.Counters.Count,
            CounterResetDetected = request.CounterResetDetected,
            PayloadJson = payloadJson,
            PayloadHash = payloadHash,
            CreatedAt = DateTime.UtcNow,
        });

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            return CreatedAtAction(nameof(GetLatestEnergy), new { sourceId }, new PowerSnapshotIngestResponse { Skipped = true });
        }

        return CreatedAtAction(nameof(GetLatestEnergy), new { sourceId }, new PowerSnapshotIngestResponse { Inserted = true });
    }

    [HttpPost("inverter-detail")]
    [Authorize(Policy = "PowerIngest")]
    [ProducesResponseType(typeof(PowerSnapshotIngestResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [Produces("application/json")]
    public async Task<ActionResult<PowerSnapshotIngestResponse>> IngestInverterDetail(
        [FromBody] PowerInverterDetailPayload request,
        CancellationToken ct)
    {
        var sourceId = NormalizeSourceId(request.SourceId);
        var recordedAt = NormalizeRecordedAt(request.RecordedAtUtc);
        var validationResults = ValidateCommonSnapshot(sourceId, request.SourceSystem, request.DeviceId, recordedAt);
        ValidateInverterDetail(validationResults, request);
        if (validationResults.Count > 0)
            return BadRequest(new ValidationProblemDetails(ToValidationDictionary(validationResults)));
        if (!await IngestSourceAuthority.CanWriteAllAsync(
            _db, User, [(request.SourceId, request.SourceSystem)], ct))
            return Forbid();

        var payloadJson = JsonSerializer.Serialize(request, JsonOptions);
        var payloadHash = ComputeHash(RemoveRecordedAt(payloadJson));
        if (await _db.PowerInverterDetailSnapshots.AnyAsync(
            r => r.SourceId == sourceId && (r.RecordedAt == recordedAt || r.PayloadHash == payloadHash), ct))
        {
            return CreatedAtAction(nameof(GetLatestInverterDetail), new { sourceId }, new PowerSnapshotIngestResponse { Skipped = true });
        }

        _db.PowerInverterDetailSnapshots.Add(new PowerInverterDetailSnapshot
        {
            SourceId = sourceId,
            SourceSystem = NormalizeSourceSystem(request.SourceSystem),
            DeviceId = NormalizeOptional(request.DeviceId),
            RecordedAt = recordedAt,
            PvStringCount = request.PvStrings.Count,
            StatusCount = request.Statuses.Count,
            PayloadJson = payloadJson,
            PayloadHash = payloadHash,
            CreatedAt = DateTime.UtcNow,
        });

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            return CreatedAtAction(nameof(GetLatestInverterDetail), new { sourceId }, new PowerSnapshotIngestResponse { Skipped = true });
        }

        return CreatedAtAction(nameof(GetLatestInverterDetail), new { sourceId }, new PowerSnapshotIngestResponse { Inserted = true });
    }

    [HttpPost("mppt-detail")]
    [Authorize(Policy = "PowerIngest")]
    [ProducesResponseType(typeof(PowerSnapshotIngestResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [Produces("application/json")]
    public async Task<ActionResult<PowerSnapshotIngestResponse>> IngestMpptDetail(
        [FromBody] PowerMpptDetailPayload request,
        CancellationToken ct)
    {
        var sourceId = NormalizeSourceId(request.SourceId);
        var recordedAt = NormalizeRecordedAt(request.RecordedAtUtc);
        var validationResults = ValidateCommonSnapshot(sourceId, request.SourceSystem, request.DeviceId, recordedAt);
        ValidateMpptDetail(validationResults, request);
        if (validationResults.Count > 0)
            return BadRequest(new ValidationProblemDetails(ToValidationDictionary(validationResults)));
        if (!await IngestSourceAuthority.CanWriteAllAsync(
            _db, User, [(request.SourceId, request.SourceSystem)], ct))
            return Forbid();

        if (await _db.PowerMpptDetailSnapshots.AnyAsync(
            r => r.SourceId == sourceId && r.RecordedAt == recordedAt, ct))
        {
            return CreatedAtAction(nameof(GetLatestMpptDetail), new { sourceId }, new PowerSnapshotIngestResponse { Skipped = true });
        }

        _db.PowerMpptDetailSnapshots.Add(new PowerMpptDetailSnapshot
        {
            SourceId = sourceId,
            SourceSystem = NormalizeSourceSystem(request.SourceSystem),
            DeviceId = NormalizeOptional(request.DeviceId),
            RecordedAt = recordedAt,
            TrackerCount = request.Trackers.Count,
            TemperatureCount = request.Temperatures.Count,
            DiagnosticCount = request.Diagnostics.Count,
            PayloadJson = JsonSerializer.Serialize(request, JsonOptions),
            CreatedAt = DateTime.UtcNow,
        });

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            return CreatedAtAction(nameof(GetLatestMpptDetail), new { sourceId }, new PowerSnapshotIngestResponse { Skipped = true });
        }

        return CreatedAtAction(nameof(GetLatestMpptDetail), new { sourceId }, new PowerSnapshotIngestResponse { Inserted = true });
    }

    [HttpPost("gateway-status")]
    [Authorize(Policy = "PowerIngest")]
    [ProducesResponseType(typeof(PowerSnapshotIngestResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [Produces("application/json")]
    public async Task<ActionResult<PowerSnapshotIngestResponse>> IngestGatewayStatus(
        [FromBody] GatewayStatusPayload request,
        CancellationToken ct)
    {
        var sourceId = NormalizeSourceId(request.SourceId);
        var recordedAt = NormalizeRecordedAt(request.RecordedAtUtc);
        var validationResults = ValidateCommonSnapshot(sourceId, request.SourceSystem, request.DeviceId, recordedAt);
        ValidateGatewayStatus(validationResults, request);
        if (validationResults.Count > 0)
            return BadRequest(new ValidationProblemDetails(ToValidationDictionary(validationResults)));

        var payloadJson = JsonSerializer.Serialize(request, JsonOptions);
        var payloadHash = ComputeHash(RemoveRecordedAt(payloadJson));
        if (await _db.GatewayStatusSnapshots.AnyAsync(
            r => r.SourceId == sourceId && (r.RecordedAt == recordedAt || r.PayloadHash == payloadHash), ct))
        {
            return CreatedAtAction(nameof(GetLatestGatewayStatus), new { sourceId }, new PowerSnapshotIngestResponse { Skipped = true });
        }

        _db.GatewayStatusSnapshots.Add(new GatewayStatusSnapshot
        {
            SourceId = sourceId,
            SourceSystem = NormalizeSourceSystem(request.SourceSystem),
            DeviceId = NormalizeOptional(request.DeviceId),
            GatewayId = NormalizeOptional(request.Identity.GatewayId)!,
            HealthState = request.Health.State.ToString(),
            SourceFreshnessState = request.Health.SourceFreshness.ToString(),
            RestState = request.Rest.State.ToString(),
            MqttState = request.Mqtt?.State.ToString(),
            RecordedAt = recordedAt,
            AlertCount = request.Health.Alerts.Count,
            OutboxPendingCount = request.Outbox.PendingCount,
            OutboxFailedCount = request.Outbox.FailedCount,
            PayloadJson = payloadJson,
            PayloadHash = payloadHash,
            CreatedAt = DateTime.UtcNow,
        });

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            return CreatedAtAction(nameof(GetLatestGatewayStatus), new { sourceId }, new PowerSnapshotIngestResponse { Skipped = true });
        }

        return CreatedAtAction(nameof(GetLatestGatewayStatus), new { sourceId }, new PowerSnapshotIngestResponse { Inserted = true });
    }

    /// <summary>Returns recent normalized power readings.</summary>
    [HttpGet("readings/recent")]
    [Authorize(Policy = "PowerRead")]
    [ProducesResponseType(typeof(IReadOnlyList<PowerReadingResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [Produces("application/json")]
    public async Task<ActionResult<IReadOnlyList<PowerReadingResponse>>> GetRecentReadings(
        [FromQuery] string? sourceId,
        [FromQuery][Range(1, 500)] int limit = 100,
        CancellationToken ct = default)
    {
        var query = _db.PowerReadings.AsQueryable();
        if (!string.IsNullOrWhiteSpace(sourceId))
        {
            var normalized = NormalizeSourceId(sourceId);
            query = query.Where(r => r.SourceId == normalized);
        }

        var rows = await query
            .OrderByDescending(r => r.RecordedAt)
            .Take(limit)
            .Select(r => new PowerReadingResponse
            {
                Id = r.Id,
                SourceId = r.SourceId,
                SourceSystem = r.SourceSystem,
                DeviceId = r.DeviceId,
                RecordedAt = r.RecordedAt,
                PvPowerW = r.PvPowerW,
                LoadPowerW = r.LoadPowerW,
                GridPowerW = r.GridPowerW,
                BatteryPowerW = r.BatteryPowerW,
                SystemPowerW = r.SystemPowerW,
                BatteryStateOfChargePercent = r.BatteryStateOfChargePercent,
                BatteryVoltageV = r.BatteryVoltageV,
                BatteryCurrentA = r.BatteryCurrentA,
                BatteryCapacityKwh = r.BatteryCapacityKwh,
                GridVoltageV = r.GridVoltageV,
                GridFrequencyHz = r.GridFrequencyHz,
                OutputVoltageV = r.OutputVoltageV,
                OutputFrequencyHz = r.OutputFrequencyHz,
                LoadPercentage = r.LoadPercentage,
                InverterMode = r.InverterMode,
                OutputSourcePriority = r.OutputSourcePriority,
                ChargerSourcePriority = r.ChargerSourcePriority,
            })
            .ToListAsync(ct);
        return Ok(rows);
    }

    [HttpGet("device-inventory/latest")]
    [Authorize(Policy = "PowerRead")]
    [ProducesResponseType(typeof(PowerDeviceInventorySnapshotResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [Produces("application/json")]
    public async Task<ActionResult<PowerDeviceInventorySnapshotResponse>> GetLatestDeviceInventory(
        [FromQuery] string sourceId = "solarassistant-total",
        [FromQuery][Range(1, 10080)] int staleAfterMinutes = 1440,
        CancellationToken ct = default)
    {
        var snapshots = await _inventoryConfigurationProvider.GetLatestAsync(sourceId, staleAfterMinutes, ct);
        return Ok(snapshots.Inventory);
    }

    [HttpGet("configuration/latest")]
    [Authorize(Policy = "PowerRead")]
    [ProducesResponseType(typeof(PowerConfigurationSnapshotResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [Produces("application/json")]
    public async Task<ActionResult<PowerConfigurationSnapshotResponse>> GetLatestConfiguration(
        [FromQuery] string sourceId = "solarassistant-total",
        [FromQuery][Range(1, 10080)] int staleAfterMinutes = 1440,
        CancellationToken ct = default)
    {
        var snapshots = await _inventoryConfigurationProvider.GetLatestAsync(sourceId, staleAfterMinutes, ct);
        return Ok(snapshots.Configuration);
    }

    [HttpGet("energy/latest")]
    [Authorize(Policy = "PowerRead")]
    [ProducesResponseType(typeof(PowerEnergySnapshotResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [Produces("application/json")]
    public async Task<ActionResult<PowerEnergySnapshotResponse>> GetLatestEnergy(
        [FromQuery] string sourceId = "solarassistant-total",
        [FromQuery][Range(1, 10080)] int staleAfterMinutes = 1440,
        CancellationToken ct = default)
    {
        var normalized = NormalizeSourceId(sourceId);
        var row = await _db.PowerEnergySnapshots
            .AsNoTracking()
            .Where(r => r.SourceId == normalized)
            .OrderByDescending(r => r.RecordedAt)
            .FirstOrDefaultAsync(ct);
        if (row is null)
            return Ok(new PowerEnergySnapshotResponse { SourceId = normalized, IsPresent = false, IsStale = true });

        var payload = JsonSerializer.Deserialize<PowerEnergyPayload>(row.PayloadJson, JsonOptions) ?? new PowerEnergyPayload();
        return Ok(new PowerEnergySnapshotResponse
        {
            SourceId = row.SourceId,
            SourceSystem = row.SourceSystem,
            DeviceId = row.DeviceId,
            RecordedAtUtc = row.RecordedAt,
            IsPresent = true,
            IsStale = DateTime.UtcNow - row.RecordedAt > TimeSpan.FromMinutes(staleAfterMinutes),
            CounterResetDetected = payload.CounterResetDetected,
            Counters = payload.Counters,
        });
    }

    [HttpGet("inverter-detail/latest")]
    [Authorize(Policy = "PowerRead")]
    [ProducesResponseType(typeof(PowerInverterDetailSnapshotResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [Produces("application/json")]
    public async Task<ActionResult<PowerInverterDetailSnapshotResponse>> GetLatestInverterDetail(
        [FromQuery] string sourceId = "solarassistant-total",
        [FromQuery][Range(1, 10080)] int staleAfterMinutes = 1440,
        CancellationToken ct = default)
    {
        var normalized = NormalizeSourceId(sourceId);
        var row = await _db.PowerInverterDetailSnapshots
            .AsNoTracking()
            .Where(r => r.SourceId == normalized)
            .OrderByDescending(r => r.RecordedAt)
            .FirstOrDefaultAsync(ct);
        if (row is null)
            return Ok(new PowerInverterDetailSnapshotResponse { SourceId = normalized, IsPresent = false, IsStale = true });

        var payload = JsonSerializer.Deserialize<PowerInverterDetailPayload>(row.PayloadJson, JsonOptions) ?? new PowerInverterDetailPayload();
        return Ok(new PowerInverterDetailSnapshotResponse
        {
            SourceId = row.SourceId,
            SourceSystem = row.SourceSystem,
            DeviceId = row.DeviceId,
            RecordedAtUtc = row.RecordedAt,
            IsPresent = true,
            IsStale = DateTime.UtcNow - row.RecordedAt > TimeSpan.FromMinutes(staleAfterMinutes),
            PvStrings = payload.PvStrings,
            Ac = payload.Ac,
            Load = payload.Load,
            Battery = payload.Battery,
            Operating = payload.Operating,
            TemperatureC = payload.TemperatureC,
            Temperatures = payload.Temperatures,
            Statuses = payload.Statuses,
        });
    }

    [HttpGet("mppt-detail/latest")]
    [Authorize(Policy = "PowerRead")]
    [ProducesResponseType(typeof(PowerMpptDetailSnapshotResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [Produces("application/json")]
    public async Task<ActionResult<PowerMpptDetailSnapshotResponse>> GetLatestMpptDetail(
        [FromQuery][Required] string sourceId,
        [FromQuery][Range(1, 10080)] int staleAfterMinutes = 1440,
        CancellationToken ct = default)
    {
        var normalized = NormalizeSourceId(sourceId);
        if (normalized.Length == 0)
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]> { [nameof(sourceId)] = ["The sourceId field is required."] }));

        var row = await _db.PowerMpptDetailSnapshots
            .AsNoTracking()
            .Where(r => r.SourceId == normalized)
            .OrderByDescending(r => r.RecordedAt)
            .FirstOrDefaultAsync(ct);
        if (row is null)
            return Ok(new PowerMpptDetailSnapshotResponse { SourceId = normalized, IsPresent = false, IsStale = true });

        return Ok(ToMpptDetailResponse(row, staleAfterMinutes));
    }

    [HttpGet("mppt-detail/recent")]
    [Authorize(Policy = "PowerRead")]
    [ProducesResponseType(typeof(IReadOnlyList<PowerMpptDetailSnapshotResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [Produces("application/json")]
    public async Task<ActionResult<IReadOnlyList<PowerMpptDetailSnapshotResponse>>> GetRecentMpptDetail(
        [FromQuery] string? sourceId,
        [FromQuery][Range(1, 5000)] int limit = 2880,
        CancellationToken ct = default)
    {
        var query = _db.PowerMpptDetailSnapshots.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(sourceId))
        {
            var normalized = NormalizeSourceId(sourceId);
            query = query.Where(r => r.SourceId == normalized);
        }

        var rows = await query
            .OrderByDescending(r => r.RecordedAt)
            .Take(limit)
            .ToListAsync(ct);
        return Ok(rows.Select(row => ToMpptDetailResponse(row)).ToList());
    }

    [HttpGet("gateway-status/latest")]
    [Authorize(Policy = "PowerRead")]
    [ProducesResponseType(typeof(GatewayStatusSnapshotResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [Produces("application/json")]
    public async Task<ActionResult<GatewayStatusSnapshotResponse>> GetLatestGatewayStatus(
        [FromQuery] string sourceId = "solarassistant-total",
        [FromQuery][Range(1, 10080)] int staleAfterMinutes = 60,
        CancellationToken ct = default)
    {
        var normalized = NormalizeSourceId(sourceId);
        var row = await _db.GatewayStatusSnapshots
            .AsNoTracking()
            .Where(r => r.SourceId == normalized)
            .OrderByDescending(r => r.RecordedAt)
            .FirstOrDefaultAsync(ct);
        if (row is null)
            return Ok(new GatewayStatusSnapshotResponse { SourceId = normalized, IsPresent = false, IsStale = true });

        var recordedAtUtc = DateTime.SpecifyKind(row.RecordedAt, DateTimeKind.Utc);
        var payload = JsonSerializer.Deserialize<GatewayStatusPayload>(row.PayloadJson, JsonOptions) ?? new GatewayStatusPayload();
        return Ok(new GatewayStatusSnapshotResponse
        {
            SourceId = row.SourceId,
            SourceSystem = row.SourceSystem,
            DeviceId = row.DeviceId,
            RecordedAtUtc = recordedAtUtc,
            IsPresent = true,
            IsStale = DateTime.UtcNow - recordedAtUtc > TimeSpan.FromMinutes(staleAfterMinutes),
            Identity = payload.Identity,
            Health = payload.Health,
            Rest = payload.Rest,
            Mqtt = payload.Mqtt,
            Outbox = payload.Outbox,
            RestMetricCount = payload.RestMetricCount,
            MqttEntityCount = payload.MqttEntityCount,
            MqttStateTopicCount = payload.MqttStateTopicCount,
            MqttCommandTopicCount = payload.MqttCommandTopicCount,
        });
    }

    /// <summary>Returns the latest composed power-system snapshot from recent source readings.</summary>
    [HttpGet("system/latest")]
    [Authorize(Policy = "PowerRead")]
    [ProducesResponseType(typeof(PowerSystemSnapshot), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [Produces("application/json")]
    public async Task<ActionResult<PowerSystemSnapshot>> GetLatestSystemSnapshot(
        [FromQuery][Range(1, 1440)] int lookbackMinutes = 60,
        CancellationToken ct = default)
    {
        return Ok(await _snapshotProvider.GetLatestAsync(lookbackMinutes, ct)
            ?? PowerSystemSnapshotComposer.Compose([], DateTime.UtcNow));
    }

    private static string NormalizeSourceId(string? sourceId) => sourceId?.Trim() ?? string.Empty;

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizeSourceSystem(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();

    private static DateTime NormalizeRecordedAt(DateTime recordedAt) =>
        recordedAt.ToUniversalTime();

    private static void ValidateRequiredTimestamp(
        List<ValidationResult> results,
        string memberName,
        DateTime value)
    {
        if (value == default)
            results.Add(new ValidationResult($"The {memberName} field is required.", [memberName]));
    }

    private static void ValidateMaxLength(
        List<ValidationResult> results,
        string memberName,
        string? value,
        int maxLength)
    {
        if (value is { Length: > 0 } && value.Trim().Length > maxLength)
            results.Add(new ValidationResult($"The field {memberName} must be a string or array type with a maximum length of '{maxLength}'.", [memberName]));
    }

    private static void ValidateRange(
        List<ValidationResult> results,
        string memberName,
        double? value,
        double minimum,
        double maximum)
    {
        if (value.HasValue && (!double.IsFinite(value.Value) || value.Value < minimum || value.Value > maximum))
            results.Add(new ValidationResult($"The field {memberName} must be between {minimum} and {maximum}.", [memberName]));
    }

    private static void ValidateRange(
        List<ValidationResult> results,
        string memberName,
        double value,
        double minimum,
        double maximum) => ValidateRange(results, memberName, (double?)value, minimum, maximum);

    private static bool IsUniqueConstraintViolation(DbUpdateException ex) =>
        ex.InnerException is SqlException sqlEx &&
        (sqlEx.Number == 2601 || sqlEx.Number == 2627);

    private static List<ValidationResult> ValidateCommonSnapshot(
        string sourceId,
        string? sourceSystem,
        string? deviceId,
        DateTime recordedAt)
    {
        var validationResults = new List<ValidationResult>();
        if (sourceId.Length == 0)
            validationResults.Add(new ValidationResult("The SourceId field is required.", ["SourceId"]));
        ValidateMaxLength(validationResults, "SourceId", sourceId, 64);
        ValidateMaxLength(validationResults, "SourceSystem", sourceSystem, 64);
        ValidateMaxLength(validationResults, "DeviceId", deviceId, 64);
        ValidateRequiredTimestamp(validationResults, "RecordedAtUtc", recordedAt);
        return validationResults;
    }

    private static void ValidateDeviceInventory(List<ValidationResult> results, PowerDeviceInventoryPayload request)
    {
        ValidateCount(results, nameof(request.Devices), request.Devices.Count, MaxInventoryDevices);
        for (var i = 0; i < request.Devices.Count; i++)
        {
            var device = request.Devices[i];
            ValidateRequiredString(results, $"Devices[{i}].DeviceId", device.DeviceId, MaxSnapshotStringLength);
            ValidateRequiredString(results, $"Devices[{i}].Name", device.Name, MaxSnapshotStringLength);
            ValidateMaxLength(results, $"Devices[{i}].Manufacturer", device.Manufacturer, MaxSnapshotStringLength);
            ValidateMaxLength(results, $"Devices[{i}].Model", device.Model, MaxSnapshotStringLength);
            ValidateMaxLength(results, $"Devices[{i}].FirmwareVersion", device.FirmwareVersion, MaxSnapshotStringLength);
        }
    }

    private static void ValidateConfiguration(List<ValidationResult> results, PowerConfigurationPayload request)
    {
        ValidateCount(results, nameof(request.Settings), request.Settings.Count, MaxConfigurationSettings);
        ValidateCount(results, nameof(request.CommandCapabilities), request.CommandCapabilities.Count, MaxCommandCapabilities);
        for (var i = 0; i < request.Settings.Count; i++)
        {
            var setting = request.Settings[i];
            ValidateRequiredString(results, $"Settings[{i}].Key", setting.Key, MaxSnapshotStringLength);
            ValidateRequiredString(results, $"Settings[{i}].Name", setting.Name, MaxSnapshotStringLength);
            ValidateMaxLength(results, $"Settings[{i}].Value", setting.Value, MaxSnapshotValueLength);
            ValidateMaxLength(results, $"Settings[{i}].Unit", setting.Unit, MaxSnapshotStringLength);
            ValidateMaxLength(results, $"Settings[{i}].DeviceId", setting.DeviceId, MaxSnapshotStringLength);
            ValidateMaxLength(results, $"Settings[{i}].SourceTopic", setting.SourceTopic, MaxSnapshotStringLength);
        }

        for (var i = 0; i < request.CommandCapabilities.Count; i++)
        {
            var capability = request.CommandCapabilities[i];
            ValidateRequiredString(results, $"CommandCapabilities[{i}].Key", capability.Key, MaxSnapshotStringLength);
            ValidateRequiredString(results, $"CommandCapabilities[{i}].Name", capability.Name, MaxSnapshotStringLength);
            ValidateRequiredString(results, $"CommandCapabilities[{i}].CommandTopic", capability.CommandTopic, MaxSnapshotStringLength);
            ValidateMaxLength(results, $"CommandCapabilities[{i}].StateTopic", capability.StateTopic, MaxSnapshotStringLength);
            ValidateMaxLength(results, $"CommandCapabilities[{i}].DeviceId", capability.DeviceId, MaxSnapshotStringLength);
        }
    }

    private static void ValidateEnergy(List<ValidationResult> results, PowerEnergyPayload request)
    {
        ValidateCount(results, nameof(request.Counters), request.Counters.Count, MaxEnergyCounters);
        for (var i = 0; i < request.Counters.Count; i++)
        {
            var counter = request.Counters[i];
            ValidateRequiredString(results, $"Counters[{i}].Key", counter.Key, MaxSnapshotStringLength);
            ValidateRequiredString(results, $"Counters[{i}].Name", counter.Name, MaxSnapshotStringLength);
            ValidateRange(results, $"Counters[{i}].ValueKwh", counter.ValueKwh, 0, 1_000_000_000);
            ValidateMaxLength(results, $"Counters[{i}].DeviceId", counter.DeviceId, MaxSnapshotStringLength);
            ValidateMaxLength(results, $"Counters[{i}].SourceTopic", counter.SourceTopic, MaxSnapshotStringLength);
        }
    }

    private static void ValidateInverterDetail(List<ValidationResult> results, PowerInverterDetailPayload request)
    {
        if (request.PvStrings is null || request.Temperatures is null || request.Statuses is null)
        {
            if (request.PvStrings is null)
                results.Add(new ValidationResult("The PvStrings field cannot be null.", [nameof(request.PvStrings)]));
            if (request.Temperatures is null)
                results.Add(new ValidationResult("The Temperatures field cannot be null.", [nameof(request.Temperatures)]));
            if (request.Statuses is null)
                results.Add(new ValidationResult("The Statuses field cannot be null.", [nameof(request.Statuses)]));
            return;
        }

        ValidateCount(results, nameof(request.PvStrings), request.PvStrings.Count, MaxPvStrings);
        ValidateCount(results, nameof(request.Temperatures), request.Temperatures.Count, MaxInverterTemperatures);
        ValidateCount(results, nameof(request.Statuses), request.Statuses.Count, MaxInverterStatuses);
        ValidateUniqueTrimmedValues(results, "PvStrings.StringId", request.PvStrings.Select(item => item?.StringId));
        ValidateUniqueTrimmedValues(results, "Temperatures.TemperatureId", request.Temperatures.Select(item => item?.TemperatureId));
        ValidateUniqueTrimmedValues(results, "Statuses.Key", request.Statuses.Select(item => item?.Key));
        for (var i = 0; i < request.PvStrings.Count; i++)
        {
            var pv = request.PvStrings[i];
            if (pv is null)
            {
                results.Add(new ValidationResult($"The PvStrings[{i}] field cannot be null.", [$"PvStrings[{i}]"]));
                continue;
            }
            ValidateRequiredString(results, $"PvStrings[{i}].StringId", pv.StringId, MaxSnapshotStringLength);
            ValidateRange(results, $"PvStrings[{i}].PowerW", pv.PowerW, 0, 1_000_000);
            ValidateRange(results, $"PvStrings[{i}].VoltageV", pv.VoltageV, 0, 10_000);
            ValidateRange(results, $"PvStrings[{i}].CurrentA", pv.CurrentA, 0, 10_000);
        }


        if (request.Ac is not null)
        {
            ValidateRange(results, "Ac.InputVoltageV", request.Ac.InputVoltageV, 0, 1_000);
            ValidateRange(results, "Ac.InputFrequencyHz", request.Ac.InputFrequencyHz, 0, 100);
            ValidateRange(results, "Ac.OutputVoltageV", request.Ac.OutputVoltageV, 0, 1_000);
            ValidateRange(results, "Ac.OutputFrequencyHz", request.Ac.OutputFrequencyHz, 0, 100);
        }

        if (request.Load is not null)
        {
            ValidateRange(results, "Load.LoadPowerW", request.Load.LoadPowerW, 0, 1_000_000);
            ValidateRange(results, "Load.LoadApparentPowerVa", request.Load.LoadApparentPowerVa, 0, 1_000_000);
            ValidateRange(results, "Load.SystemAndLoadPowerW", request.Load.SystemAndLoadPowerW, -1_000_000, 1_000_000);
        }

        if (request.Battery is not null)
        {
            ValidateRange(results, "Battery.VoltageV", request.Battery.VoltageV, 0, 1_000);
            ValidateRange(results, "Battery.CurrentA", request.Battery.CurrentA, -10_000, 10_000);
            ValidateRange(results, "Battery.PowerW", request.Battery.PowerW, -1_000_000, 1_000_000);
        }

        ValidateRange(results, nameof(request.TemperatureC), request.TemperatureC, -100, 200);
        if (request.Operating is not null)
        {
            ValidateMaxLength(results, "Operating.Mode", request.Operating.Mode, MaxSnapshotStringLength);
            ValidateMaxLength(results, "Operating.FaultCode", request.Operating.FaultCode, MaxSnapshotStringLength);
            ValidateRange(results, "Operating.LoadPercentage", request.Operating.LoadPercentage, 0, 1_000);
            ValidateMaxLength(results, "Operating.StatusFlags", request.Operating.StatusFlags, MaxSnapshotValueLength);
        }

        for (var i = 0; i < request.Temperatures.Count; i++)
        {
            var temperature = request.Temperatures[i];
            if (temperature is null)
            {
                results.Add(new ValidationResult($"The Temperatures[{i}] field cannot be null.", [$"Temperatures[{i}]"]));
                continue;
            }
            ValidateRequiredString(results, $"Temperatures[{i}].TemperatureId", temperature.TemperatureId, MaxSnapshotStringLength);
            ValidateRequiredString(results, $"Temperatures[{i}].Name", temperature.Name, MaxSnapshotStringLength);
            ValidateRange(results, $"Temperatures[{i}].TemperatureC", temperature.TemperatureC, -100, 200);
        }

        for (var i = 0; i < request.Statuses.Count; i++)
        {
            var status = request.Statuses[i];
            if (status is null)
            {
                results.Add(new ValidationResult($"The Statuses[{i}] field cannot be null.", [$"Statuses[{i}]"]));
                continue;
            }
            ValidateRequiredString(results, $"Statuses[{i}].Key", status.Key, MaxSnapshotStringLength);
            ValidateRequiredString(results, $"Statuses[{i}].Value", status.Value, MaxSnapshotValueLength);
            ValidateMaxLength(results, $"Statuses[{i}].SourceTopic", status.SourceTopic, MaxSnapshotStringLength);
        }
    }

    private static void ValidateMpptDetail(List<ValidationResult> results, PowerMpptDetailPayload request)
    {
        if (request.Trackers is null || request.Temperatures is null || request.Diagnostics is null)
        {
            if (request.Trackers is null)
                results.Add(new ValidationResult("The Trackers field is required.", [nameof(request.Trackers)]));
            if (request.Temperatures is null)
                results.Add(new ValidationResult("The Temperatures field cannot be null.", [nameof(request.Temperatures)]));
            if (request.Diagnostics is null)
                results.Add(new ValidationResult("The Diagnostics field cannot be null.", [nameof(request.Diagnostics)]));
            return;
        }

        if (request.Trackers.Count == 0)
            results.Add(new ValidationResult("The Trackers field must contain at least one tracker.", [nameof(request.Trackers)]));
        ValidateCount(results, nameof(request.Trackers), request.Trackers.Count, MaxMpptTrackers);
        ValidateCount(results, nameof(request.Temperatures), request.Temperatures.Count, MaxMpptTemperatures);
        ValidateCount(results, nameof(request.Diagnostics), request.Diagnostics.Count, MaxMpptDiagnostics);
        ValidateUniqueTrimmedValues(results, "Trackers.TrackerId", request.Trackers.Select(item => item?.TrackerId));
        ValidateUniqueTrimmedValues(results, "Temperatures.TemperatureId", request.Temperatures.Select(item => item?.TemperatureId));
        ValidateUniqueTrimmedValues(results, "Diagnostics.Key", request.Diagnostics.Select(item => item?.Key));

        for (var i = 0; i < request.Trackers.Count; i++)
        {
            var tracker = request.Trackers[i];
            if (tracker is null)
            {
                results.Add(new ValidationResult($"The Trackers[{i}] field cannot be null.", [$"Trackers[{i}]"]));
                continue;
            }
            ValidateRequiredString(results, $"Trackers[{i}].TrackerId", tracker.TrackerId, MaxSnapshotStringLength);
            ValidateRequiredString(results, $"Trackers[{i}].Name", tracker.Name, MaxSnapshotStringLength);
            ValidateRange(results, $"Trackers[{i}].VoltageV", tracker.VoltageV, 0, 10_000);
            ValidateRange(results, $"Trackers[{i}].CurrentA", tracker.CurrentA, 0, 10_000);
            ValidateRange(results, $"Trackers[{i}].PowerW", tracker.PowerW, 0, 1_000_000);
            ValidateProvenance(results, $"Trackers[{i}].Provenance", tracker.Provenance);
            ValidateMaxLength(results, $"Trackers[{i}].Confidence", tracker.Confidence, MaxSnapshotStringLength);
        }

        if (request.BatteryOutput is not null)
        {
            ValidateRange(results, "BatteryOutput.VoltageV", request.BatteryOutput.VoltageV, 0, 1_000);
            ValidateRange(results, "BatteryOutput.CurrentA", request.BatteryOutput.CurrentA, -10_000, 0);
            ValidateRange(results, "BatteryOutput.PowerW", request.BatteryOutput.PowerW, -1_000_000, 0);
            ValidateProvenance(results, "BatteryOutput.Provenance", request.BatteryOutput.Provenance);
            ValidateMaxLength(results, "BatteryOutput.Confidence", request.BatteryOutput.Confidence, MaxSnapshotStringLength);
        }

        for (var i = 0; i < request.Temperatures.Count; i++)
        {
            var temperature = request.Temperatures[i];
            if (temperature is null)
            {
                results.Add(new ValidationResult($"The Temperatures[{i}] field cannot be null.", [$"Temperatures[{i}]"]));
                continue;
            }
            ValidateRequiredString(results, $"Temperatures[{i}].TemperatureId", temperature.TemperatureId, MaxSnapshotStringLength);
            ValidateRequiredString(results, $"Temperatures[{i}].Name", temperature.Name, MaxSnapshotStringLength);
            ValidateRange(results, $"Temperatures[{i}].TemperatureC", temperature.TemperatureC, -100, 200);
            ValidateMaxLength(results, $"Temperatures[{i}].Confidence", temperature.Confidence, MaxSnapshotStringLength);
        }

        for (var i = 0; i < request.Diagnostics.Count; i++)
        {
            var diagnostic = request.Diagnostics[i];
            if (diagnostic is null)
            {
                results.Add(new ValidationResult($"The Diagnostics[{i}] field cannot be null.", [$"Diagnostics[{i}]"]));
                continue;
            }
            ValidateRequiredString(results, $"Diagnostics[{i}].Key", diagnostic.Key, MaxSnapshotStringLength);
            ValidateRequiredString(results, $"Diagnostics[{i}].Name", diagnostic.Name, MaxSnapshotStringLength);
            ValidateRequiredString(results, $"Diagnostics[{i}].Value", diagnostic.Value, MaxSnapshotValueLength);
        }
    }

    private static void ValidateProvenance(
        List<ValidationResult> results,
        string memberName,
        PowerObservationProvenance provenance)
    {
        if (provenance == PowerObservationProvenance.Unknown || !Enum.IsDefined(provenance))
            results.Add(new ValidationResult($"The field {memberName} has an invalid provenance value.", [memberName]));
    }

    private static void ValidateUniqueTrimmedValues(
        List<ValidationResult> results,
        string memberName,
        IEnumerable<string?> values)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (values.Where(value => !string.IsNullOrWhiteSpace(value)).Any(value => !seen.Add(value!.Trim())))
            results.Add(new ValidationResult($"The field {memberName} must contain unique trimmed values.", [memberName]));
    }

    private static void ValidateGatewayStatus(List<ValidationResult> results, GatewayStatusPayload request)
    {
        ValidateRequiredString(results, "Identity.GatewayId", request.Identity.GatewayId, 64);
        ValidateRequiredString(results, "Identity.DisplayName", request.Identity.DisplayName, MaxSnapshotStringLength);
        ValidateRequiredString(results, "Identity.SourceId", request.Identity.SourceId, MaxSnapshotStringLength);
        ValidateMaxLength(results, "Identity.DeviceId", request.Identity.DeviceId, MaxSnapshotStringLength);
        ValidateMaxLength(results, "Identity.RuntimeHost", request.Identity.RuntimeHost, MaxSnapshotStringLength);
        ValidateRequiredTimestamp(results, "Health.EvaluatedAtUtc", request.Health.EvaluatedAtUtc);
        ValidateCount(results, "Health.Alerts", request.Health.Alerts.Count, MaxGatewayAlerts);
        ValidateMaxLength(results, "Health.OutboxState", request.Health.OutboxState, MaxSnapshotStringLength);
        ValidateMaxLength(results, "Health.ApiSyncState", request.Health.ApiSyncState, MaxSnapshotStringLength);
        ValidateRuntimeSignal(results, "Rest", request.Rest);
        if (request.Mqtt is not null)
            ValidateRuntimeSignal(results, "Mqtt", request.Mqtt);
        ValidateRange(results, "Outbox.PendingCount", request.Outbox.PendingCount, 0, 1_000_000);
        ValidateRange(results, "Outbox.FailedCount", request.Outbox.FailedCount, 0, 1_000_000);
        ValidateRange(results, "Outbox.LastBatchCount", request.Outbox.LastBatchCount, 0, 1_000_000);
        ValidateMaxLength(results, "Outbox.LastError", request.Outbox.LastError, MaxSnapshotValueLength);
        ValidateRange(results, nameof(request.RestMetricCount), request.RestMetricCount, 0, 1_000_000);
        ValidateRange(results, nameof(request.MqttEntityCount), request.MqttEntityCount, 0, 1_000_000);
        ValidateRange(results, nameof(request.MqttStateTopicCount), request.MqttStateTopicCount, 0, 1_000_000);
        ValidateRange(results, nameof(request.MqttCommandTopicCount), request.MqttCommandTopicCount, 0, 1_000_000);

        for (var i = 0; i < request.Health.Alerts.Count; i++)
        {
            var alert = request.Health.Alerts[i];
            ValidateRequiredString(results, $"Health.Alerts[{i}].Code", alert.Code, MaxSnapshotStringLength);
            ValidateRequiredString(results, $"Health.Alerts[{i}].Message", alert.Message, MaxSnapshotValueLength);
        }
    }

    private static void ValidateRuntimeSignal(List<ValidationResult> results, string prefix, GatewayRuntimeSignal signal)
    {
        ValidateMaxLength(results, $"{prefix}.LastError", signal.LastError, MaxSnapshotValueLength);
        ValidateMaxLength(results, $"{prefix}.Detail", signal.Detail, MaxSnapshotValueLength);
    }

    private static void ValidateCount(List<ValidationResult> results, string memberName, int count, int maximum)
    {
        if (count > maximum)
            results.Add(new ValidationResult($"The field {memberName} must contain at most {maximum} item(s).", [memberName]));
    }

    private static void ValidateRequiredString(List<ValidationResult> results, string memberName, string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            results.Add(new ValidationResult($"The {memberName} field is required.", [memberName]));
            return;
        }

        ValidateMaxLength(results, memberName, value, maxLength);
    }

    private static Dictionary<string, string[]> ToValidationDictionary(IEnumerable<ValidationResult> validationResults) =>
        validationResults
            .SelectMany(r => r.MemberNames.DefaultIfEmpty(string.Empty), (r, memberName) => new { MemberName = memberName, r.ErrorMessage })
            .GroupBy(x => x.MemberName)
            .ToDictionary(g => g.Key, g => g.Select(x => x.ErrorMessage ?? "Validation failed.").ToArray());

    private static PowerMpptDetailSnapshotResponse ToMpptDetailResponse(
        PowerMpptDetailSnapshot row,
        int? staleAfterMinutes = null)
    {
        var recordedAtUtc = DateTime.SpecifyKind(row.RecordedAt, DateTimeKind.Utc);
        var payload = JsonSerializer.Deserialize<PowerMpptDetailPayload>(row.PayloadJson, JsonOptions) ?? new PowerMpptDetailPayload();
        return new PowerMpptDetailSnapshotResponse
        {
            Id = row.Id,
            SourceId = row.SourceId,
            SourceSystem = row.SourceSystem,
            DeviceId = row.DeviceId,
            RecordedAtUtc = recordedAtUtc,
            IsPresent = true,
            IsStale = staleAfterMinutes.HasValue && DateTime.UtcNow - recordedAtUtc > TimeSpan.FromMinutes(staleAfterMinutes.Value),
            Trackers = payload.Trackers,
            BatteryOutput = payload.BatteryOutput,
            Temperatures = payload.Temperatures,
            Diagnostics = payload.Diagnostics,
        };
    }

    private static string ComputeHash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string RemoveRecordedAt(string payloadJson)
    {
        using var document = JsonDocument.Parse(payloadJson);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (string.Equals(property.Name, "recordedAtUtc", StringComparison.OrdinalIgnoreCase))
                    continue;

                property.WriteTo(writer);
            }
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
