using Microsoft.EntityFrameworkCore;

namespace HVO.Hardware.DavisVantagePro2.Outbox;

public static class DavisLegacyOutboxMigrator
{
    public static async Task MigrateAsync(OutboxDbContext db, string stationId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        var sourceId = string.IsNullOrWhiteSpace(stationId) ? "davis-vantage-pro2" : stationId.Trim();
        if (!string.Equals(db.Database.ProviderName, "Microsoft.EntityFrameworkCore.Sqlite", StringComparison.Ordinal))
            return;

        var columns = await GetOutboxColumnsAsync(db, ct).ConfigureAwait(false);
        if (columns.Count == 0)
            return;

        await db.Database.ExecuteSqlRawAsync("DROP INDEX IF EXISTS IX_OutboxRecords_RecordedAtUtc", ct).ConfigureAwait(false);
        await db.Database.ExecuteSqlRawAsync("DROP INDEX IF EXISTS IX_OutboxRecords_SourceId_RecordedAtUtc", ct).ConfigureAwait(false);

        if (!columns.Contains("SourceId"))
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE OutboxRecords ADD COLUMN SourceId TEXT NULL",
                ct).ConfigureAwait(false);
        }

        if (!columns.Contains("DeviceId"))
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE OutboxRecords ADD COLUMN DeviceId TEXT NULL",
                ct).ConfigureAwait(false);
        }

        if (!columns.Contains("PayloadType"))
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE OutboxRecords ADD COLUMN PayloadType TEXT NULL",
                ct).ConfigureAwait(false);
        }

        if (!columns.Contains("PayloadVersion"))
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE OutboxRecords ADD COLUMN PayloadVersion TEXT NULL",
                ct).ConfigureAwait(false);
        }

        await db.Database.ExecuteSqlRawAsync(
            "UPDATE OutboxRecords SET SourceId = {0} WHERE SourceId = '' OR SourceId IS NULL",
            [sourceId],
            ct).ConfigureAwait(false);

        await db.Database.ExecuteSqlRawAsync(
            "UPDATE OutboxRecords SET PayloadVersion = {0} WHERE PayloadVersion = '' OR PayloadVersion IS NULL",
            [DavisOutboxPayloadTypes.RawVersion],
            ct).ConfigureAwait(false);

        if (columns.Contains("IsArchiveRecord"))
        {
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE OutboxRecords SET PayloadType = CASE WHEN IsArchiveRecord = 1 THEN {0} ELSE {1} END",
                [DavisOutboxPayloadTypes.Archive, DavisOutboxPayloadTypes.Raw],
                ct).ConfigureAwait(false);
        }
        else
        {
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE OutboxRecords SET PayloadType = {0} WHERE PayloadType = '' OR PayloadType IS NULL",
                [DavisOutboxPayloadTypes.Raw],
                ct).ConfigureAwait(false);
        }

        if (columns.Contains("IsArchiveRecord"))
            await RebuildSharedTableAsync(db, columns, ct).ConfigureAwait(false);
    }

    private static async Task RebuildSharedTableAsync(OutboxDbContext db, IReadOnlySet<string> columns, CancellationToken ct)
    {
        var failureKindSelect = columns.Contains("FailureKind") ? "FailureKind" : "0";
        await db.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS OutboxRecords_shared", ct).ConfigureAwait(false);
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE OutboxRecords_shared (
                Id INTEGER NOT NULL CONSTRAINT PK_OutboxRecords PRIMARY KEY AUTOINCREMENT,
                SourceId TEXT NOT NULL,
                DeviceId TEXT NULL,
                PayloadType TEXT NOT NULL,
                PayloadVersion TEXT NOT NULL,
                RecordedAtUtc TEXT NOT NULL,
                Payload TEXT NOT NULL,
                Status INTEGER NOT NULL,
                AttemptCount INTEGER NOT NULL,
                LastAttemptedAtUtc TEXT NULL,
                SentAtUtc TEXT NULL,
                NextRetryAtUtc TEXT NOT NULL,
                LastError TEXT NULL,
                FailureKind INTEGER NOT NULL DEFAULT 0,
                CreatedAtUtc TEXT NOT NULL
            )
            """, ct).ConfigureAwait(false);

        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO OutboxRecords_shared (
                Id, SourceId, DeviceId, PayloadType, PayloadVersion, RecordedAtUtc, Payload, Status,
                AttemptCount, LastAttemptedAtUtc, SentAtUtc, NextRetryAtUtc, LastError, FailureKind, CreatedAtUtc)
            SELECT
                Id,
                SourceId,
                DeviceId,
                PayloadType,
                PayloadVersion,
                RecordedAtUtc,
                Payload,
                Status,
                AttemptCount,
                LastAttemptedAtUtc,
                SentAtUtc,
                NextRetryAtUtc,
                LastError,
                __FAILURE_KIND__,
                CreatedAtUtc
            FROM OutboxRecords
            """.Replace("__FAILURE_KIND__", failureKindSelect, StringComparison.Ordinal), ct).ConfigureAwait(false);

        await db.Database.ExecuteSqlRawAsync("DROP TABLE OutboxRecords", ct).ConfigureAwait(false);
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE OutboxRecords_shared RENAME TO OutboxRecords", ct).ConfigureAwait(false);
        await db.Database.ExecuteSqlRawAsync(
            "CREATE UNIQUE INDEX IF NOT EXISTS IX_OutboxRecords_SourceId_PayloadType_RecordedAtUtc ON OutboxRecords (SourceId, PayloadType, RecordedAtUtc)",
            ct).ConfigureAwait(false);
        await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS IX_OutboxRecords_Status ON OutboxRecords (Status)", ct).ConfigureAwait(false);
    }

    private static async Task<HashSet<string>> GetOutboxColumnsAsync(OutboxDbContext db, CancellationToken ct)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "PRAGMA table_info('OutboxRecords')";
        if (command.Connection!.State != System.Data.ConnectionState.Open)
            await command.Connection.OpenAsync(ct).ConfigureAwait(false);

        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            columns.Add(reader.GetString(1));

        return columns;
    }
}
