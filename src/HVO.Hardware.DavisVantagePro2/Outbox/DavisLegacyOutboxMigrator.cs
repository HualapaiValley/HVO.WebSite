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
