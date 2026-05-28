using Microsoft.EntityFrameworkCore;

namespace HVO.Edge.Outbox;

public static class EdgeOutboxDatabaseInitializer
{
    public static async Task EnsureCreatedAsync(
        EdgeOutboxDbContext db,
        string defaultPayloadType,
        string defaultPayloadVersion,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        var payloadType = NormalizeDefault(defaultPayloadType, nameof(defaultPayloadType));
        var payloadVersion = NormalizeDefault(defaultPayloadVersion, nameof(defaultPayloadVersion));

        await db.Database.EnsureCreatedAsync(ct);
        await EnsureColumnAsync(db, "PayloadType", $"TEXT NOT NULL DEFAULT '{EscapeSqlLiteral(payloadType)}'", ct);
        await EnsureColumnAsync(db, "PayloadVersion", $"TEXT NOT NULL DEFAULT '{EscapeSqlLiteral(payloadVersion)}'", ct);
        await ReplaceLegacyUniqueIndexAsync(db, ct);
    }

    private static async Task EnsureColumnAsync(EdgeOutboxDbContext db, string columnName, string columnDefinition, CancellationToken ct)
    {
        var existingColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var command = db.Database.GetDbConnection().CreateCommand())
        {
            command.CommandText = "PRAGMA table_info('OutboxRecords')";
            if (command.Connection!.State != System.Data.ConnectionState.Open)
                await command.Connection.OpenAsync(ct);

            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                existingColumns.Add(reader.GetString(1));
        }

        if (existingColumns.Contains(columnName))
            return;

        var sql = $"ALTER TABLE OutboxRecords ADD COLUMN {columnName} {columnDefinition}";
        await db.Database.ExecuteSqlRawAsync(sql, ct);
    }

    private static async Task ReplaceLegacyUniqueIndexAsync(EdgeOutboxDbContext db, CancellationToken ct)
    {
        await db.Database.ExecuteSqlRawAsync("DROP INDEX IF EXISTS IX_OutboxRecords_SourceId_RecordedAtUtc", ct);
        await db.Database.ExecuteSqlRawAsync(
            "CREATE UNIQUE INDEX IF NOT EXISTS IX_OutboxRecords_SourceId_PayloadType_RecordedAtUtc ON OutboxRecords (SourceId, PayloadType, RecordedAtUtc)",
            ct);
    }

    private static string NormalizeDefault(string value, string name)
    {
        var normalized = value.Trim();
        if (normalized.Length == 0)
            throw new ArgumentException("Default outbox metadata cannot be empty.", name);
        return normalized;
    }

    private static string EscapeSqlLiteral(string value) => value.Replace("'", "''", StringComparison.Ordinal);
}
