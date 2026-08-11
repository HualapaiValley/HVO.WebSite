using FluentAssertions;
using HVO.Edge.Contracts;
using HVO.Edge.Outbox;
using HVO.Hardware.JkBms.Outbox;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace HVO.Hardware.JkBms.Tests.Outbox;

[TestClass]
public sealed class JkBmsLegacyOutboxMigratorTests
{
    [TestMethod]
    public async Task MigrateAsync_RebuildsLegacySchemaBeforeSharedInitializer()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = Context(connection);
        await CreateLegacySchemaAsync(db);

        var result = await JkBmsLegacyOutboxMigrator.MigrateAsync(db);
        db.ChangeTracker.Clear();
        await EdgeOutboxSqliteDatabaseInitializer.EnsureCreatedAsync(db, EdgePayloadTypes.BmsReading, "1");

        result.SchemaMigrated.Should().BeTrue();
        var row = await db.OutboxRecords.SingleAsync();
        row.SourceId.Should().Be("AA:BB:CC:DD:EE:FF");
        row.DeviceId.Should().Be("bank-1a");
        row.PayloadType.Should().Be(EdgePayloadTypes.BmsReading);
        row.PayloadJson.Should().Be("{}");
    }

    [TestMethod]
    public async Task MigrateAsync_CanonicalizesReadingsAndAccountsForStandaloneSnapshots()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = Context(connection);
        await EdgeOutboxSqliteDatabaseInitializer.EnsureCreatedAsync(db, EdgePayloadTypes.Legacy.BmsReading, "1");
        db.OutboxRecords.AddRange(
            Record(1, EdgePayloadTypes.Legacy.BmsReading, "2026-08-11T12:00:00Z"),
            Record(2, EdgePayloadTypes.Legacy.BmsConfig, "2026-08-11T12:00:01Z"),
            Record(3, EdgePayloadTypes.Legacy.BmsDeviceInfo, "2026-08-11T12:00:02Z"));
        await db.SaveChangesAsync();

        var result = await JkBmsLegacyOutboxMigrator.MigrateAsync(db);
        db.ChangeTracker.Clear();

        result.CanonicalizedReadingCount.Should().Be(1);
        result.AccountedSnapshotCount.Should().Be(2);
        (await db.OutboxRecords.SingleAsync(row => row.Id == 1)).PayloadType.Should().Be(EdgePayloadTypes.BmsReading);
        var snapshots = await db.OutboxRecords.Where(row => row.Id > 1).ToListAsync();
        snapshots.Should().OnlyContain(row => row.Status == EdgeOutboxStatus.Sent);
        snapshots.Should().OnlyContain(row => row.LastError!.Contains("Accounted during vNext migration", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task MigrateAsync_PreservesPayloadKindsWhileRebuildingTransitionalSchema()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = Context(connection);
        await CreateTransitionalSchemaAsync(db);

        var result = await JkBmsLegacyOutboxMigrator.MigrateAsync(db);
        db.ChangeTracker.Clear();

        result.SchemaMigrated.Should().BeTrue();
        result.CanonicalizedReadingCount.Should().Be(1);
        result.AccountedSnapshotCount.Should().Be(1);
        (await db.OutboxRecords.SingleAsync(row => row.Id == 1)).PayloadType.Should().Be(EdgePayloadTypes.BmsReading);
        var snapshot = await db.OutboxRecords.SingleAsync(row => row.Id == 2);
        snapshot.PayloadType.Should().Be(EdgePayloadTypes.Legacy.BmsConfig);
        snapshot.Status.Should().Be(EdgeOutboxStatus.Sent);
    }

    private static DefaultEdgeOutboxDbContext Context(SqliteConnection connection) => new(
        new DbContextOptionsBuilder<DefaultEdgeOutboxDbContext>().UseSqlite(connection).Options);

    private static EdgeOutboxRecord Record(long id, string payloadType, string recordedAt) => new()
    {
        Id = id,
        SourceId = "AA:BB:CC:DD:EE:FF",
        DeviceId = "bank-1a",
        PayloadType = payloadType,
        PayloadVersion = "1",
        RecordedAtUtc = DateTime.Parse(recordedAt).ToUniversalTime(),
        PayloadJson = "{}",
        CreatedAtUtc = DateTime.Parse(recordedAt).ToUniversalTime(),
    };

    private static async Task CreateLegacySchemaAsync(DefaultEdgeOutboxDbContext db)
    {
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE OutboxRecords (
                Id INTEGER NOT NULL CONSTRAINT PK_OutboxRecords PRIMARY KEY AUTOINCREMENT,
                DeviceAddress TEXT NOT NULL,
                DeviceAlias TEXT NOT NULL,
                RecordedAtUtc TEXT NOT NULL,
                Payload TEXT NOT NULL,
                Status INTEGER NOT NULL,
                AttemptCount INTEGER NOT NULL,
                LastAttemptedAtUtc TEXT NULL,
                SentAtUtc TEXT NULL,
                NextRetryAtUtc TEXT NOT NULL,
                LastError TEXT NULL,
                CreatedAtUtc TEXT NOT NULL
            )
            """);
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO OutboxRecords (
                DeviceAddress, DeviceAlias, RecordedAtUtc, Payload, Status, AttemptCount, NextRetryAtUtc, CreatedAtUtc)
            VALUES (
                'AA:BB:CC:DD:EE:FF', 'bank-1a', '2026-06-16T12:00:00Z', '{{}}', 0, 0,
                '0001-01-01T00:00:00Z', '2026-06-16T12:00:00Z')
            """);
    }

    private static async Task CreateTransitionalSchemaAsync(DefaultEdgeOutboxDbContext db)
    {
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE OutboxRecords (
                Id INTEGER NOT NULL CONSTRAINT PK_OutboxRecords PRIMARY KEY AUTOINCREMENT,
                DeviceAddress TEXT NOT NULL,
                DeviceAlias TEXT NOT NULL,
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
                CreatedAtUtc TEXT NOT NULL
            )
            """);
        await db.Database.ExecuteSqlRawAsync($"""
            INSERT INTO OutboxRecords (
                DeviceAddress, DeviceAlias, PayloadType, PayloadVersion, RecordedAtUtc, Payload,
                Status, AttemptCount, NextRetryAtUtc, CreatedAtUtc)
            VALUES
                ('AA:BB:CC:DD:EE:FF', 'bank-1a', '{EdgePayloadTypes.Legacy.BmsReading}', '1',
                 '2026-06-16T12:00:00Z', 'reading-payload', 0, 0, '0001-01-01T00:00:00Z', '2026-06-16T12:00:00Z'),
                ('AA:BB:CC:DD:EE:FF', 'bank-1a', '{EdgePayloadTypes.Legacy.BmsConfig}', '1',
                 '2026-06-16T12:00:01Z', 'config-payload', 0, 0, '0001-01-01T00:00:00Z', '2026-06-16T12:00:01Z')
            """);
    }
}
