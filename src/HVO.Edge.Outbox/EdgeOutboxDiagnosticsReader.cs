using HVO.Edge.Contracts;
using Microsoft.EntityFrameworkCore;

namespace HVO.Edge.Outbox;

public static class EdgeOutboxDiagnosticsReader
{
    public static async Task<GatewayOutboxDiagnostics> ReadAsync(
        EdgeOutboxDbContext db,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);

        var schema = await EdgeOutboxSchemaValidator.ValidateAsync(db, ct).ConfigureAwait(false);
        var failedByKind = await db.OutboxRecords
            .Where(record => record.Status == EdgeOutboxStatus.Failed)
            .GroupBy(record => record.FailureKind)
            .Select(group => new { Kind = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.Kind.ToString(), item => item.Count, ct)
            .ConfigureAwait(false);

        var lastFailure = await db.OutboxRecords
            .Where(record => record.Status == EdgeOutboxStatus.Failed && record.LastError != null)
            .OrderByDescending(record => record.LastAttemptedAtUtc ?? record.CreatedAtUtc)
            .Select(record => new { record.LastError, record.FailureKind })
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        var pendingCount = await db.OutboxRecords.CountAsync(record => record.Status == EdgeOutboxStatus.Pending, ct).ConfigureAwait(false);
        var sentCount = await db.OutboxRecords.CountAsync(record => record.Status == EdgeOutboxStatus.Sent, ct).ConfigureAwait(false);
        var failedCount = failedByKind.Values.Sum();
        var lastSentAtUtc = await db.OutboxRecords
            .Where(record => record.Status == EdgeOutboxStatus.Sent && record.SentAtUtc != null)
            .MaxAsync(record => (DateTime?)record.SentAtUtc, ct)
            .ConfigureAwait(false);

        return new GatewayOutboxDiagnostics(
            PendingCount: pendingCount,
            SentCount: sentCount,
            FailedCount: failedCount,
            FailedCountByKind: failedByKind,
            LastSentAtUtc: lastSentAtUtc,
            LastError: lastFailure?.LastError,
            LastFailureKind: lastFailure?.FailureKind.ToString(),
            Schema: new GatewayOutboxSchemaState(
                schema.IsCompatible,
                schema.Errors,
                schema.Warnings,
                DateTime.UtcNow),
            MaintenanceState: BuildMaintenanceState(schema, pendingCount, failedCount));
    }

    private static string BuildMaintenanceState(EdgeOutboxSchemaValidationResult schema, int pendingCount, int failedCount)
    {
        if (!schema.IsCompatible)
            return "schema-incompatible";
        if (failedCount > 0)
            return "failed-records-present";
        if (pendingCount > 0)
            return "pending-forward";
        return "current";
    }
}
