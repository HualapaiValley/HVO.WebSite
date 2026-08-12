using System.Text.Json;
using HVO.Edge.Contracts;
using HVO.Edge.Contracts.PowerSystem;
using HVO.Edge.Outbox;
using Microsoft.EntityFrameworkCore;

namespace HVO.Hardware.VictronSmartShunt.Outbox;

public static class SmartShuntLegacyOutboxMigrator
{
    public static async Task<int> MigrateAsync(DefaultEdgeOutboxDbContext db, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        if (!string.Equals(db.Database.ProviderName, "Microsoft.EntityFrameworkCore.Sqlite", StringComparison.Ordinal)) return 0;
        var columns = await GetColumnsAsync(db, cancellationToken);
        if (columns.Count == 0) return 0;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (!columns.Contains("FailureKind"))
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE OutboxRecords ADD COLUMN FailureKind INTEGER NOT NULL DEFAULT 0", cancellationToken);
        await db.Database.ExecuteSqlRawAsync("DROP INDEX IF EXISTS IX_OutboxRecords_SourceId_RecordedAtUtc", cancellationToken);
        await db.Database.ExecuteSqlRawAsync("DROP INDEX IF EXISTS IX_OutboxRecords_SourceId_PayloadType_RecordedAtUtc", cancellationToken);

        var candidates = await db.OutboxRecords.Where(record =>
            record.PayloadType == EdgePayloadTypes.Legacy.SmartShuntReading
            || record.PayloadType == EdgePayloadTypes.SmartShuntReading
            || record.PayloadType == EdgePayloadTypes.SmartShuntObservation)
            .OrderBy(record => record.Id).ToListAsync(cancellationToken);
        var migrated = 0;
        foreach (var group in candidates.GroupBy(record => (record.SourceId, record.RecordedAtUtc)))
        {
            var parsed = group.Select(record => (Record: record, Payload: Parse(record))).ToArray();
            var migrationWork = parsed.Count(item => item.Record.PayloadType != EdgePayloadTypes.SmartShuntObservation)
                + Math.Max(0, parsed.Length - 1);
            var winner = parsed
                .Where(static item => item.Payload is not null)
                .OrderByDescending(static item => item.Record.PayloadType == EdgePayloadTypes.SmartShuntObservation)
                .ThenByDescending(static item => StatusRank(item.Record.Status))
                .ThenBy(static item => item.Record.Id)
                .FirstOrDefault();
            var records = parsed.Select(static item => item.Record).ToArray();
            var retained = winner.Record ?? records.OrderByDescending(record => StatusRank(record.Status)).ThenBy(record => record.Id).First();
            var bestStatus = records.OrderByDescending(record => StatusRank(record.Status)).First();
            retained.Status = bestStatus.Status;
            retained.AttemptCount = records.Max(static record => record.AttemptCount);
            retained.SentAtUtc = records.Where(static record => record.SentAtUtc.HasValue).Max(static record => record.SentAtUtc);
            retained.LastAttemptedAtUtc = records.Where(static record => record.LastAttemptedAtUtc.HasValue).Max(static record => record.LastAttemptedAtUtc);
            retained.NextRetryAtUtc = records.Min(static record => record.NextRetryAtUtc);
            retained.PayloadType = EdgePayloadTypes.SmartShuntObservation;
            retained.PayloadVersion = "1";
            if (winner.Payload is not null)
            {
                retained.PayloadJson = JsonSerializer.Serialize(winner.Payload, JsonSerializerOptions.Web);
                retained.DeviceId = winner.Payload.Summary.DeviceId;
                retained.FailureKind = retained.Status == EdgeOutboxStatus.Failed ? bestStatus.FailureKind : EdgeOutboxFailureKind.None;
                retained.LastError = retained.Status == EdgeOutboxStatus.Failed ? bestStatus.LastError : null;
            }
            else
            {
                retained.Status = EdgeOutboxStatus.Failed;
                retained.FailureKind = EdgeOutboxFailureKind.Permanent;
                retained.LastError = "Legacy SmartShunt payload could not be migrated.";
            }
            db.OutboxRecords.RemoveRange(records.Where(record => record.Id != retained.Id));
            migrated += migrationWork;
        }
        await db.SaveChangesAsync(cancellationToken);
        await db.Database.ExecuteSqlRawAsync("CREATE UNIQUE INDEX IX_OutboxRecords_SourceId_PayloadType_RecordedAtUtc ON OutboxRecords (SourceId, PayloadType, RecordedAtUtc)", cancellationToken);
        await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS IX_OutboxRecords_Status ON OutboxRecords (Status)", cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return migrated;
    }

    private static SmartShuntObservationPayload? Parse(EdgeOutboxRecord record)
    {
        try
        {
            if (record.PayloadType == EdgePayloadTypes.SmartShuntObservation)
                return JsonSerializer.Deserialize<SmartShuntObservationPayload>(record.PayloadJson, JsonSerializerOptions.Web);
            var summary = JsonSerializer.Deserialize<PowerReadingPayload>(record.PayloadJson, JsonSerializerOptions.Web);
            if (summary is null || string.IsNullOrWhiteSpace(summary.SourceId) || summary.RecordedAtUtc == default) return null;
            return new(summary, new SmartShuntDetailPayload
            {
                SourceId = summary.SourceId, SourceSystem = summary.SourceSystem, DeviceId = summary.DeviceId, RecordedAtUtc = summary.RecordedAtUtc
            });
        }
        catch (JsonException) { return null; }
    }

    private static int StatusRank(EdgeOutboxStatus status) => status switch
    {
        // A pending collision still has delivery work. It must not be hidden by an
        // older sent summary that never delivered the new SmartShunt detail.
        EdgeOutboxStatus.Pending => 3,
        EdgeOutboxStatus.Sent => 2,
        EdgeOutboxStatus.Failed => 1,
        _ => 0
    };

    private static async Task<HashSet<string>> GetColumnsAsync(DefaultEdgeOutboxDbContext db, CancellationToken cancellationToken)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "PRAGMA table_info('OutboxRecords')";
        if (command.Connection!.State != System.Data.ConnectionState.Open) await command.Connection.OpenAsync(cancellationToken);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) columns.Add(reader.GetString(1));
        return columns;
    }
}
