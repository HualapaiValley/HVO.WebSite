using Microsoft.EntityFrameworkCore;

namespace HVO.Hardware.JkBms.Outbox;

public static class JkBmsLegacyOutboxMigrator
{
    public static async Task MigrateAsync(OutboxDbContext db, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        if (!string.Equals(db.Database.ProviderName, "Microsoft.EntityFrameworkCore.Sqlite", StringComparison.Ordinal))
            return;

        var columns = await GetOutboxColumnsAsync(db, ct).ConfigureAwait(false);
        if (columns.Count == 0 || !columns.Contains("DeviceAddress"))
            return;

        await db.Database.ExecuteSqlRawAsync("DROP INDEX IF EXISTS IX_OutboxRecords_DeviceAddress_RecordedAtUtc", ct).ConfigureAwait(false);

        if (!columns.Contains("SourceId"))
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE OutboxRecords ADD COLUMN SourceId TEXT NOT NULL DEFAULT ''",
                ct).ConfigureAwait(false);
        }

        if (!columns.Contains("DeviceId"))
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE OutboxRecords ADD COLUMN DeviceId TEXT NULL",
                ct).ConfigureAwait(false);
        }

        await db.Database.ExecuteSqlRawAsync(
            "UPDATE OutboxRecords SET SourceId = DeviceAddress WHERE SourceId = '' OR SourceId IS NULL",
            ct).ConfigureAwait(false);
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE OutboxRecords SET DeviceId = DeviceAlias WHERE DeviceId IS NULL AND DeviceAlias IS NOT NULL",
            ct).ConfigureAwait(false);

        await RebuildSharedTableAsync(db, ct).ConfigureAwait(false);
    }

    private static async Task RebuildSharedTableAsync(OutboxDbContext db, CancellationToken ct)
    {
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
                COALESCE(NULLIF(SourceId, ''), DeviceAddress),
                NULLIF(DeviceId, ''),
                'bms.reading',
                '1',
                RecordedAtUtc,
                Payload,
                Status,
                AttemptCount,
                LastAttemptedAtUtc,
                SentAtUtc,
                NextRetryAtUtc,
                LastError,
                0,
                CreatedAtUtc
            FROM OutboxRecords
            """, ct).ConfigureAwait(false);

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
