using System.Text.Json;
using Asp.Versioning;
using HVO.DataModels.Data;
using HVO.DataModels.Models.V9;
using HVO.Edge.Contracts.PowerSystem;
using HVO.WebSite.v9.Infrastructure;
using HVO.WebSite.v9.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace HVO.WebSite.v9.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/power/smartshunt-observations")]
[Tags("Power")]
public sealed class SmartShuntObservationIngestController(
    HvoV9DbContext db,
    ILogger<SmartShuntObservationIngestController> logger) : ControllerBase
{
    private const string SourceSystem = "victron-smartshunt";

    [HttpPost("batch")]
    [Authorize(Policy = "PowerIngest")]
    [ProducesResponseType(typeof(PowerReadingBatchResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<PowerReadingBatchResponse>> IngestBatch(
        [FromBody] JsonElement batch,
        CancellationToken cancellationToken)
    {
        if (batch.ValueKind != JsonValueKind.Array)
            return ValidationProblem(detail: "Request body must be a JSON array.");
        var elements = batch.EnumerateArray().Select(static element => element.Clone()).ToArray();
        if (elements.Length == 0)
            return ValidationProblem(detail: "Batch must contain at least one record.");
        if (elements.Length > PowerIngestController.MaxBatchSize)
            return ValidationProblem(detail: $"Batch size {elements.Length} exceeds the maximum of {PowerIngestController.MaxBatchSize} records.");

        var failures = new List<PowerReadingBatchFailure>();
        var valid = new List<SmartShuntObservationPayload>();
        var seen = new HashSet<(string SourceId, DateTime RecordedAtUtc)>();
        var skipped = 0;
        foreach (var element in elements)
        {
            SmartShuntObservationPayload? payload;
            try { payload = element.Deserialize<SmartShuntObservationPayload>(JsonSerializerOptions.Web); }
            catch (JsonException exception)
            {
                failures.Add(Failure(element, $"Invalid SmartShunt observation: {exception.Message}"));
                continue;
            }
            var error = Validate(payload);
            if (error is not null)
            {
                failures.Add(new() { SourceId = payload?.Summary?.SourceId?.Trim() ?? string.Empty, RecordedAtUtc = payload?.Summary?.RecordedAtUtc ?? default, Error = error });
                continue;
            }
            var key = (payload!.Summary.SourceId!.Trim(), payload.Summary.RecordedAtUtc);
            if (!seen.Add(key)) { skipped++; continue; }
            valid.Add(payload);
        }

        var ownedSources = User.FindAll(IngestSourceAuthority.SourceClaimType).Select(static claim => claim.Value).ToHashSet(StringComparer.Ordinal);
        if (valid.Any(payload => !ownedSources.Contains(payload.Summary.SourceId!.Trim())))
            return Forbid();

        var sourceIds = valid.Select(payload => payload.Summary.SourceId!.Trim()).Distinct().ToArray();
        var timestamps = valid.Select(payload => payload.Summary.RecordedAtUtc).Distinct().ToArray();
        var summaryKeys = await db.PowerReadings.AsNoTracking()
            .Where(row => sourceIds.Contains(row.SourceId) && timestamps.Contains(row.RecordedAt))
            .Select(static row => new { row.SourceId, row.RecordedAt }).ToListAsync(cancellationToken);
        var detailKeys = await db.SmartShuntDetailSnapshots.AsNoTracking()
            .Where(row => sourceIds.Contains(row.SourceId) && timestamps.Contains(row.RecordedAt))
            .Select(static row => new { row.SourceId, row.RecordedAt }).ToListAsync(cancellationToken);
        var summaries = summaryKeys.Select(static row => (row.SourceId, row.RecordedAt)).ToHashSet();
        var details = detailKeys.Select(static row => (row.SourceId, row.RecordedAt)).ToHashSet();
        var toInsert = new List<SmartShuntObservationPayload>();
        foreach (var payload in valid)
        {
            var key = (payload.Summary.SourceId!.Trim(), payload.Summary.RecordedAtUtc);
            if (summaries.Contains(key) && details.Contains(key)) skipped++;
            else toInsert.Add(payload);
        }

        if (toInsert.Count > 0)
        {
            await using var transaction = db.Database.IsRelational()
                ? await db.Database.BeginTransactionAsync(cancellationToken) : null;
            try
            {
                foreach (var payload in toInsert)
                {
                    var key = (payload.Summary.SourceId!.Trim(), payload.Summary.RecordedAtUtc);
                    if (!summaries.Contains(key)) db.PowerReadings.Add(MapSummary(payload.Summary));
                    if (!details.Contains(key)) db.SmartShuntDetailSnapshots.Add(MapDetail(payload.Detail));
                }
                await db.SaveChangesAsync(cancellationToken);
                if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            }
            catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
            {
                if (transaction is not null) await transaction.RollbackAsync(cancellationToken);
                db.ChangeTracker.Clear();
                var reconciledSummaries = await db.PowerReadings.AsNoTracking()
                    .Where(row => sourceIds.Contains(row.SourceId) && timestamps.Contains(row.RecordedAt))
                    .Select(static row => new { row.SourceId, row.RecordedAt }).ToListAsync(cancellationToken);
                var reconciledDetails = await db.SmartShuntDetailSnapshots.AsNoTracking()
                    .Where(row => sourceIds.Contains(row.SourceId) && timestamps.Contains(row.RecordedAt))
                    .Select(static row => new { row.SourceId, row.RecordedAt }).ToListAsync(cancellationToken);
                var summarySet = reconciledSummaries.Select(static row => (row.SourceId, row.RecordedAt)).ToHashSet();
                var detailSet = reconciledDetails.Select(static row => (row.SourceId, row.RecordedAt)).ToHashSet();
                if (toInsert.All(payload => summarySet.Contains((payload.Summary.SourceId!.Trim(), payload.Summary.RecordedAtUtc))
                    && detailSet.Contains((payload.Summary.SourceId!.Trim(), payload.Summary.RecordedAtUtc))))
                {
                    skipped += toInsert.Count;
                    toInsert.Clear();
                }
                else
                {
                    logger.LogError(exception, "SmartShunt observation batch had an unresolved uniqueness race");
                    return Problem(title: "SmartShunt Batch Ingest Failed", detail: "The batch could not be fully accounted. Retry is safe.", statusCode: 500);
                }
            }
            catch (DbUpdateException exception)
            {
                if (transaction is not null) await transaction.RollbackAsync(cancellationToken);
                logger.LogError(exception, "SmartShunt observation transaction failed");
                return Problem(title: "SmartShunt Batch Ingest Failed", detail: "No partial observation was committed. Retry is safe.", statusCode: 500);
            }
        }
        return StatusCode(201, new PowerReadingBatchResponse { Inserted = toInsert.Count, Skipped = skipped, Failed = failures });
    }

    private static string? Validate(SmartShuntObservationPayload? payload)
    {
        if (payload?.Summary is null || payload.Detail is null) return "Summary and detail are required.";
        var summary = payload.Summary;
        var detail = payload.Detail;
        if (string.IsNullOrWhiteSpace(summary.SourceId) || summary.SourceId != summary.SourceId.Trim() || summary.SourceId.Length > 64) return "Summary SourceId must be trimmed and 1-64 characters.";
        if (string.IsNullOrWhiteSpace(summary.DeviceId) || summary.DeviceId != summary.DeviceId.Trim() || summary.DeviceId.Length > 64) return "Summary DeviceId must be trimmed and 1-64 characters.";
        if (summary.SourceSystem != SourceSystem || detail.SourceSystem != SourceSystem) return $"SourceSystem must be {SourceSystem}.";
        if (summary.RecordedAtUtc == default || summary.RecordedAtUtc.Kind != DateTimeKind.Utc) return "RecordedAtUtc must be UTC.";
        if (detail.SourceId != summary.SourceId || detail.DeviceId != summary.DeviceId || detail.RecordedAtUtc != summary.RecordedAtUtc) return "Summary and detail identities must match.";
        if (summary.BatteryVoltageV is null || summary.BatteryCurrentA is null || summary.BatteryPowerW is null || summary.BatteryStateOfChargePercent is null) return "Voltage, current, power, and state of charge are required.";
        if (!Finite(summary.BatteryVoltageV, summary.BatteryCurrentA, summary.BatteryPowerW, summary.BatteryStateOfChargePercent,
            detail.ConsumedAh, detail.RemainingMinutes, detail.StarterVoltageV, detail.TemperatureC)) return "All provided numeric fields must be finite.";
        if (summary.BatteryStateOfChargePercent is < 0 or > 100 || detail.RemainingMinutes is < 0) return "State of charge and remaining time are outside their valid ranges.";
        return null;
    }

    private static bool Finite(params double?[] values) => values.All(value => !value.HasValue || double.IsFinite(value.Value));
    private static PowerReading MapSummary(PowerReadingPayload payload) => new()
    {
        SourceId = payload.SourceId!, SourceSystem = SourceSystem, DeviceId = payload.DeviceId, RecordedAt = payload.RecordedAtUtc,
        BatteryPowerW = payload.BatteryPowerW, SystemPowerW = payload.SystemPowerW, BatteryStateOfChargePercent = payload.BatteryStateOfChargePercent,
        BatteryVoltageV = payload.BatteryVoltageV, BatteryCurrentA = payload.BatteryCurrentA, CreatedAt = DateTime.UtcNow
    };
    private static SmartShuntDetailSnapshot MapDetail(SmartShuntDetailPayload payload) => new()
    {
        SourceId = payload.SourceId!, SourceSystem = SourceSystem, DeviceId = payload.DeviceId, RecordedAt = payload.RecordedAtUtc,
        ConsumedAh = payload.ConsumedAh, RemainingMinutes = payload.RemainingMinutes, StarterVoltageV = payload.StarterVoltageV,
        TemperatureC = payload.TemperatureC, CreatedAt = DateTime.UtcNow
    };
    private static PowerReadingBatchFailure Failure(JsonElement element, string error)
    {
        var sourceId = string.Empty; var recordedAt = default(DateTime);
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty("summary", out var summary))
        {
            if (summary.ValueKind == JsonValueKind.Object && summary.TryGetProperty("sourceId", out var source) && source.ValueKind == JsonValueKind.String)
                sourceId = source.GetString() ?? string.Empty;
            if (summary.ValueKind == JsonValueKind.Object && summary.TryGetProperty("recordedAtUtc", out var recorded) && recorded.ValueKind == JsonValueKind.String)
                DateTime.TryParse(recorded.GetString(), out recordedAt);
        }
        return new() { SourceId = sourceId, RecordedAtUtc = recordedAt, Error = error };
    }
    private static bool IsUniqueConstraintViolation(DbUpdateException exception) =>
        exception.InnerException is SqlException sql && sql.Number is 2601 or 2627
        || exception.InnerException?.Message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase) == true;
}
