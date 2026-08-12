using Microsoft.EntityFrameworkCore;
using HVO.Edge.Outbox;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace HVO.Hardware.DavisVantagePro2.Outbox;

public static class DavisLegacyOutboxMigrator
{
    public static async Task MigrateAsync(
        EdgeOutboxDbContext db,
        string stationId,
        TimeSpan? consoleUtcOffset = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        var sourceId = string.IsNullOrWhiteSpace(stationId) ? "davis-vantage-pro2" : stationId.Trim();
        if (!string.Equals(db.Database.ProviderName, "Microsoft.EntityFrameworkCore.Sqlite", StringComparison.Ordinal))
            return;

        var columns = await GetOutboxColumnsAsync(db, ct).ConfigureAwait(false);
        if (columns.Count == 0)
            return;

        await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);

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

        await db.Database.ExecuteSqlRawAsync(
            "UPDATE OutboxRecords SET PayloadType = {0} WHERE PayloadType = {1}",
            [DavisOutboxPayloadTypes.Raw, HVO.Edge.Contracts.EdgePayloadTypes.Legacy.WeatherRaw],
            ct).ConfigureAwait(false);
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE OutboxRecords SET PayloadType = {0} WHERE PayloadType = {1}",
            [DavisOutboxPayloadTypes.Archive, HVO.Edge.Contracts.EdgePayloadTypes.Legacy.WeatherArchive],
            ct).ConfigureAwait(false);

        if (columns.Contains("IsArchiveRecord"))
            await RebuildSharedTableAsync(db, columns, ct).ConfigureAwait(false);

        await MigrateArchivePayloadsAsync(db, consoleUtcOffset ?? TimeSpan.Zero, ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }

    private static async Task MigrateArchivePayloadsAsync(
        EdgeOutboxDbContext db,
        TimeSpan consoleUtcOffset,
        CancellationToken ct)
    {
        var updates = new List<(long Id, string Payload)>();
        await using (var command = db.Database.GetDbConnection().CreateCommand())
        {
            command.CommandText = "SELECT Id, Payload FROM OutboxRecords WHERE PayloadType = $payloadType";
            var parameter = command.CreateParameter();
            parameter.ParameterName = "$payloadType";
            parameter.Value = DavisOutboxPayloadTypes.Archive;
            command.Parameters.Add(parameter);
            if (command.Connection!.State != System.Data.ConnectionState.Open)
                await command.Connection.OpenAsync(ct).ConfigureAwait(false);
            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var json = reader.GetString(1);
                JsonObject? payload;
                try
                {
                    payload = JsonNode.Parse(json)?.AsObject();
                }
                catch (JsonException)
                {
                    continue;
                }
                if (payload is null)
                    continue;

                var changed = false;
                if (!HasProperty(payload, "recordedAtUtc") || !HasProperty(payload, "consoleRecordedAtLocal"))
                {
                    var recordedAtText = ReadString(payload, "recordedAtUtc") ?? ReadString(payload, "recordedAt");
                    if (!DateTime.TryParse(recordedAtText, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var recordedAt))
                        continue;
                    var utc = recordedAt.ToUniversalTime();
                    if (!HasProperty(payload, "recordedAtUtc"))
                        payload["recordedAtUtc"] = utc;
                    if (!HasProperty(payload, "consoleRecordedAtLocal"))
                    {
                        payload["consoleRecordedAtLocal"] = DateTime.SpecifyKind(
                            new DateTimeOffset(utc, TimeSpan.Zero).ToOffset(consoleUtcOffset).DateTime,
                            DateTimeKind.Unspecified);
                    }
                    changed = true;
                }
                if (!HasProperty(payload, "downloadRecordType"))
                {
                    payload["downloadRecordType"] = 0;
                    changed = true;
                }
                if (changed)
                    updates.Add((reader.GetInt64(0), payload.ToJsonString(JsonSerializerOptions.Web)));
            }
        }

        foreach (var update in updates)
        {
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE OutboxRecords SET Payload = {0} WHERE Id = {1}",
                [update.Payload, update.Id],
                ct).ConfigureAwait(false);
        }
    }

    private static bool HasProperty(JsonObject payload, string name) =>
        payload.Any(property => string.Equals(property.Key, name, StringComparison.OrdinalIgnoreCase));

    private static string? ReadString(JsonObject payload, string name)
    {
        var value = payload.FirstOrDefault(property => string.Equals(property.Key, name, StringComparison.OrdinalIgnoreCase)).Value;
        return value is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var text) ? text : null;
    }

    private static async Task RebuildSharedTableAsync(EdgeOutboxDbContext db, IReadOnlySet<string> columns, CancellationToken ct)
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

    private static async Task<HashSet<string>> GetOutboxColumnsAsync(EdgeOutboxDbContext db, CancellationToken ct)
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
