using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using Asp.Versioning;
using HVO.DataModels.Data;
using HVO.DataModels.Models.V9;
using HVO.Edge.Contracts.PowerSystem;
using HVO.WebSite.v9.Models;
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
    private readonly HvoV9DbContext _db;
    private readonly PowerIngestTelemetry _telemetry;
    private readonly ILogger<PowerIngestController> _logger;

    public PowerIngestController(
        HvoV9DbContext db,
        PowerIngestTelemetry telemetry,
        ILogger<PowerIngestController> logger)
    {
        _db = db;
        _telemetry = telemetry;
        _logger = logger;
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
        var cutoffUtc = DateTime.UtcNow.AddMinutes(-lookbackMinutes);
        var readings = await _db.PowerReadings
            .Where(r => r.RecordedAt >= cutoffUtc)
            .OrderByDescending(r => r.RecordedAt)
            .Take(500)
            .ToListAsync(ct);

        return Ok(PowerSystemSnapshotComposer.Compose(readings, DateTime.UtcNow));
    }

    private static PowerReading MapToEntity(
        PowerReadingIngestRequest request,
        string sourceId,
        DateTime recordedAt) => new()
        {
            SourceId = sourceId,
            SourceSystem = NormalizeOptional(request.SourceSystem),
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

    private static bool IsUniqueConstraintViolation(DbUpdateException ex) =>
        ex.InnerException is SqlException sqlEx &&
        (sqlEx.Number == 2601 || sqlEx.Number == 2627);
}
