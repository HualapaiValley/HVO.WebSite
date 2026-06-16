using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Asp.Versioning;
using HVO.DataModels.Data;
using HVO.DataModels.Models.V9;
using HVO.Edge.Contracts;
using HVO.Edge.Contracts.PowerSystem;
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
    private const int MaxGatewayAlerts = 50;
    private const int MaxSnapshotStringLength = 256;
    private const int MaxSnapshotValueLength = 1024;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HvoV9DbContext _db;
    private readonly PowerIngestTelemetry _telemetry;
    private readonly ILogger<PowerIngestController> _logger;
    private readonly IPowerSystemSnapshotProvider _snapshotProvider;
    private readonly IPowerInventoryConfigurationProvider _inventoryConfigurationProvider;

    public PowerIngestController(
        HvoV9DbContext db,
        PowerIngestTelemetry telemetry,
        ILogger<PowerIngestController> logger,
        IPowerSystemSnapshotProvider snapshotProvider,
        IPowerInventoryConfigurationProvider inventoryConfigurationProvider)
    {
        _db = db;
        _telemetry = telemetry;
        _logger = logger;
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
        [FromBody] IReadOnlyList<PowerReadingIngestRequest> requests,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        if (requests.Count == 0)
            return ValidationProblem(detail: "Batch must contain at least one record.");

        if (requests.Count > MaxBatchSize)
            return ValidationProblem(detail: $"Batch size {requests.Count} exceeds the maximum of {MaxBatchSize} records. Split the batch or reduce the outbox batch size on the gateway.");

        var candidates = new List<(PowerReadingIngestRequest Request, string SourceId, DateTime RecordedAt)>();
        var failures = new List<PowerReadingBatchFailure>();

        foreach (var request in requests)
        {
            var recordedAt = NormalizeRecordedAt(request.RecordedAtUtc);
            var sourceId = NormalizeSourceId(request.SourceId);
            var validationResults = new List<ValidationResult>();

            if (sourceId.Length == 0)
                validationResults.Add(new ValidationResult("The SourceId field is required.", [nameof(request.SourceId)]));

            ValidateMaxLength(validationResults, nameof(request.SourceId), sourceId, 64);
            ValidateMaxLength(validationResults, nameof(request.SourceSystem), request.SourceSystem, 64);
            ValidateMaxLength(validationResults, nameof(request.DeviceId), request.DeviceId, 64);
            ValidateMaxLength(validationResults, nameof(request.InverterMode), request.InverterMode, 100);
            ValidateMaxLength(validationResults, nameof(request.OutputSourcePriority), request.OutputSourcePriority, 100);
            ValidateMaxLength(validationResults, nameof(request.ChargerSourcePriority), request.ChargerSourcePriority, 100);
            ValidateRequiredTimestamp(validationResults, nameof(request.RecordedAtUtc), request.RecordedAtUtc);
            ValidateRange(validationResults, nameof(request.PvPowerW), request.PvPowerW, 0, 1_000_000);
            ValidateRange(validationResults, nameof(request.LoadPowerW), request.LoadPowerW, 0, 1_000_000);
            ValidateRange(validationResults, nameof(request.GridPowerW), request.GridPowerW, -1_000_000, 1_000_000);
            ValidateRange(validationResults, nameof(request.BatteryPowerW), request.BatteryPowerW, -1_000_000, 1_000_000);
            ValidateRange(validationResults, nameof(request.SystemPowerW), request.SystemPowerW, -1_000_000, 1_000_000);
            ValidateRange(validationResults, nameof(request.BatteryStateOfChargePercent), request.BatteryStateOfChargePercent, 0, 100);
            ValidateRange(validationResults, nameof(request.BatteryVoltageV), request.BatteryVoltageV, 0, 1_000);
            ValidateRange(validationResults, nameof(request.BatteryCurrentA), request.BatteryCurrentA, -10_000, 10_000);
            ValidateRange(validationResults, nameof(request.BatteryCapacityKwh), request.BatteryCapacityKwh, 0, 100_000);
            ValidateRange(validationResults, nameof(request.GridVoltageV), request.GridVoltageV, 0, 1_000);
            ValidateRange(validationResults, nameof(request.GridFrequencyHz), request.GridFrequencyHz, 0, 1_000);
            ValidateRange(validationResults, nameof(request.OutputVoltageV), request.OutputVoltageV, 0, 1_000);
            ValidateRange(validationResults, nameof(request.OutputFrequencyHz), request.OutputFrequencyHz, 0, 1_000);
            ValidateRange(validationResults, nameof(request.LoadPercentage), request.LoadPercentage, 0, 1_000);

            if (validationResults.Count > 0)
            {
                failures.Add(new PowerReadingBatchFailure
                {
                    SourceId = sourceId,
                    RecordedAtUtc = recordedAt,
                    Error = string.Join("; ", validationResults.Select(r => r.ErrorMessage)),
                });
                continue;
            }

            candidates.Add((request, sourceId, recordedAt));
        }

        if (candidates.Count == 0)
        {
            sw.Stop();
            _telemetry.RecordBatch(requests.Count, inserted: 0, skipped: 0, failed: failures.Count, sw.Elapsed.TotalMilliseconds);
            _logger.LogWarning("Power batch ingest rejected {Failed} invalid record(s)", failures.Count);
            return CreatedAtAction(nameof(GetRecentReadings), new { },
                new PowerReadingBatchResponse { Inserted = 0, Skipped = 0, Failed = failures });
        }

        var sourceIds = candidates.Select(x => x.SourceId).Distinct(StringComparer.Ordinal).ToList();
        var timestamps = candidates.Select(x => x.RecordedAt).Distinct().ToList();

        var existing = await _db.PowerReadings
            .Where(r => sourceIds.Contains(r.SourceId) && timestamps.Contains(r.RecordedAt))
            .Select(r => new { r.SourceId, r.RecordedAt })
            .ToListAsync(ct);
        var existingKeys = existing.Select(x => (x.SourceId, x.RecordedAt)).ToHashSet();

        var toInsert = new List<PowerReading>();
        var seenInBatch = new HashSet<(string SourceId, DateTime RecordedAt)>();
        int skipped = 0;

        foreach (var (request, sourceId, recordedAt) in candidates)
        {
            if (existingKeys.Contains((sourceId, recordedAt)))
            {
                skipped++;
                continue;
            }

            if (!seenInBatch.Add((sourceId, recordedAt)))
            {
                skipped++;
                continue;
            }

            toInsert.Add(MapToEntity(request, sourceId, recordedAt));
        }

        if (toInsert.Count > 0)
        {
            _db.PowerReadings.AddRange(toInsert);
            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
            {
                skipped += toInsert.Count;
                _logger.LogDebug(ex, "Unique constraint violation in power batch ingest; treating as idempotent duplicate batch");
                toInsert.Clear();
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(
                    ex,
                    "Power batch ingest failed during SaveChanges for {SourceCount} sources and {BatchSize} records",
                    sourceIds.Count,
                    requests.Count);
                return Problem(
                    detail: "An error occurred while persisting the power batch. Retry is safe — duplicate records will be skipped.",
                    statusCode: StatusCodes.Status500InternalServerError,
                    title: "Power Batch Ingest Failed");
            }
        }

        sw.Stop();
        _telemetry.RecordBatch(requests.Count, toInsert.Count, skipped, failures.Count, sw.Elapsed.TotalMilliseconds);

        _logger.LogInformation(
            "Power batch ingest: {Inserted} inserted, {Skipped} skipped, {Failed} failed from {SourceCount} sources",
            toInsert.Count,
            skipped,
            failures.Count,
            sourceIds.Count);

        if (failures.Count > 0)
            _logger.LogWarning("Power batch ingest dead-lettered {Failed} validation failure(s)", failures.Count);

        return CreatedAtAction(nameof(GetRecentReadings), new { },
            new PowerReadingBatchResponse { Inserted = toInsert.Count, Skipped = skipped, Failed = failures });
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
            Load = payload.Load,
            Battery = payload.Battery,
            TemperatureC = payload.TemperatureC,
            Statuses = payload.Statuses,
        });
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

    private static PowerReading MapToEntity(
        PowerReadingIngestRequest request,
        string sourceId,
        DateTime recordedAt) => new()
        {
            SourceId = sourceId,
            SourceSystem = NormalizeSourceSystem(request.SourceSystem),
            DeviceId = NormalizeOptional(request.DeviceId),
            RecordedAt = recordedAt,
            PvPowerW = request.PvPowerW,
            LoadPowerW = request.LoadPowerW,
            GridPowerW = request.GridPowerW,
            BatteryPowerW = request.BatteryPowerW,
            SystemPowerW = request.SystemPowerW,
            BatteryStateOfChargePercent = request.BatteryStateOfChargePercent,
            BatteryVoltageV = request.BatteryVoltageV,
            BatteryCurrentA = request.BatteryCurrentA,
            BatteryCapacityKwh = request.BatteryCapacityKwh,
            GridVoltageV = request.GridVoltageV,
            GridFrequencyHz = request.GridFrequencyHz,
            OutputVoltageV = request.OutputVoltageV,
            OutputFrequencyHz = request.OutputFrequencyHz,
            LoadPercentage = request.LoadPercentage,
            InverterMode = NormalizeOptional(request.InverterMode),
            OutputSourcePriority = NormalizeOptional(request.OutputSourcePriority),
            ChargerSourcePriority = NormalizeOptional(request.ChargerSourcePriority),
            CreatedAt = DateTime.UtcNow,
        };

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
        if (value.HasValue && (value.Value < minimum || value.Value > maximum))
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
        ValidateCount(results, nameof(request.PvStrings), request.PvStrings.Count, MaxPvStrings);
        ValidateCount(results, nameof(request.Statuses), request.Statuses.Count, MaxInverterStatuses);
        for (var i = 0; i < request.PvStrings.Count; i++)
        {
            var pv = request.PvStrings[i];
            ValidateRequiredString(results, $"PvStrings[{i}].StringId", pv.StringId, MaxSnapshotStringLength);
            ValidateRange(results, $"PvStrings[{i}].PowerW", pv.PowerW, 0, 1_000_000);
            ValidateRange(results, $"PvStrings[{i}].VoltageV", pv.VoltageV, 0, 10_000);
            ValidateRange(results, $"PvStrings[{i}].CurrentA", pv.CurrentA, 0, 10_000);
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
        for (var i = 0; i < request.Statuses.Count; i++)
        {
            var status = request.Statuses[i];
            ValidateRequiredString(results, $"Statuses[{i}].Key", status.Key, MaxSnapshotStringLength);
            ValidateRequiredString(results, $"Statuses[{i}].Value", status.Value, MaxSnapshotValueLength);
            ValidateMaxLength(results, $"Statuses[{i}].SourceTopic", status.SourceTopic, MaxSnapshotStringLength);
        }
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
