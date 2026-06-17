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
