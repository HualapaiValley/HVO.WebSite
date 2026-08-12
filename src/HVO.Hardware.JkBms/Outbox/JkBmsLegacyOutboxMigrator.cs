using HVO.Edge.Contracts;
using HVO.Edge.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace HVO.Hardware.JkBms.Outbox;

public sealed record JkBmsLegacyMigrationResult(
    bool SchemaMigrated,
    int CanonicalizedReadingCount,
    int AccountedSnapshotCount);

public static class JkBmsLegacyOutboxMigrator
{
    public static async Task<JkBmsLegacyMigrationResult> MigrateAsync(
        DefaultEdgeOutboxDbContext db,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        if (!string.Equals(db.Database.ProviderName, "Microsoft.EntityFrameworkCore.Sqlite", StringComparison.Ordinal))
            return new(false, 0, 0);

        var columns = await GetOutboxColumnsAsync(db, cancellationToken);
        if (columns.Count == 0)
            return new(false, 0, 0);

        var schemaMigrated = columns.Contains("DeviceAddress");
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (schemaMigrated)
            await RebuildSharedTableAsync(db, columns, cancellationToken);

        var canonicalized = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE OutboxRecords
            SET PayloadType = {EdgePayloadTypes.BmsReading}, PayloadVersion = '1'
            WHERE PayloadType = {EdgePayloadTypes.Legacy.BmsReading}
            """, cancellationToken);

        // v1 wrote config/device-info twice: embedded in the reading and as standalone rows.
        // The central BMS contract accepts only the embedded form, so account for redundant
        // standalone rows explicitly instead of leaving them permanently invisible to the
        // single-payload shared forwarder.
        var accounted = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE OutboxRecords
            SET Status = 1,
                SentAtUtc = COALESCE(SentAtUtc, {DateTime.UtcNow}),
                LastError = 'Accounted during vNext migration: redundant standalone snapshot is embedded in its BMS reading.',
                FailureKind = 0
            WHERE PayloadType IN (
                {EdgePayloadTypes.Legacy.BmsConfig},
                {EdgePayloadTypes.Legacy.BmsDeviceInfo},
                {EdgePayloadTypes.BmsConfig},
                {EdgePayloadTypes.BmsDeviceInfo})
              AND Status <> 1
            """, cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return new(schemaMigrated, canonicalized, accounted);
    }

    private static async Task RebuildSharedTableAsync(
        DefaultEdgeOutboxDbContext db,
        IReadOnlySet<string> columns,
        CancellationToken cancellationToken)
    {
        await db.Database.ExecuteSqlRawAsync("DROP INDEX IF EXISTS IX_OutboxRecords_DeviceAddress_RecordedAtUtc", cancellationToken);
        await db.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS OutboxRecords_vnext", cancellationToken);
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE OutboxRecords_vnext (
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
            """, cancellationToken);

        var sourceExpression = columns.Contains("SourceId")
            ? "COALESCE(NULLIF(SourceId, ''), DeviceAddress)"
            : "DeviceAddress";
        var deviceExpression = columns.Contains("DeviceId")
            ? "COALESCE(NULLIF(DeviceId, ''), DeviceAlias)"
            : "DeviceAlias";
        var payloadTypeExpression = columns.Contains("PayloadType")
            ? "PayloadType"
            : $"'{EdgePayloadTypes.Legacy.BmsReading}'";
        var payloadVersionExpression = columns.Contains("PayloadVersion")
            ? "PayloadVersion"
            : "'1'";
        var failureExpression = columns.Contains("FailureKind") ? "FailureKind" : "0";
        await ExecuteNonQueryAsync(db, $"""
            INSERT INTO OutboxRecords_vnext (
                Id, SourceId, DeviceId, PayloadType, PayloadVersion, RecordedAtUtc, Payload, Status,
                AttemptCount, LastAttemptedAtUtc, SentAtUtc, NextRetryAtUtc, LastError, FailureKind, CreatedAtUtc)
            SELECT
                Id, {sourceExpression}, {deviceExpression}, {payloadTypeExpression}, {payloadVersionExpression}, RecordedAtUtc,
                Payload, Status, AttemptCount, LastAttemptedAtUtc, SentAtUtc, NextRetryAtUtc, LastError,
                {failureExpression}, CreatedAtUtc
            FROM OutboxRecords
            """, cancellationToken);
        await db.Database.ExecuteSqlRawAsync("DROP TABLE OutboxRecords", cancellationToken);
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE OutboxRecords_vnext RENAME TO OutboxRecords", cancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            "CREATE UNIQUE INDEX IX_OutboxRecords_SourceId_PayloadType_RecordedAtUtc ON OutboxRecords (SourceId, PayloadType, RecordedAtUtc)",
            cancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IX_OutboxRecords_Status ON OutboxRecords (Status)",
            cancellationToken);
    }

    private static async Task<HashSet<string>> GetOutboxColumnsAsync(
        DefaultEdgeOutboxDbContext db,
        CancellationToken cancellationToken)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "PRAGMA table_info('OutboxRecords')";
        if (command.Connection!.State != System.Data.ConnectionState.Open)
            await command.Connection.OpenAsync(cancellationToken);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            columns.Add(reader.GetString(1));
        return columns;
    }

    private static async Task ExecuteNonQueryAsync(
        DefaultEdgeOutboxDbContext db,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
