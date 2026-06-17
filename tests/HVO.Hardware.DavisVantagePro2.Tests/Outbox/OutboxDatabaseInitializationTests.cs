using FluentAssertions;
using HVO.Edge.Outbox;
using HVO.Hardware.DavisVantagePro2.Outbox;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace HVO.Hardware.DavisVantagePro2.Tests.Outbox;

[TestClass]
public class OutboxDatabaseInitializationTests
{
    [TestMethod]
    public async Task EnsureCreatedAsync_CreatesSharedOutboxSchemaWithPayloadMetadata()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = CreateOptions(connection);

        await using (var db = new OutboxDbContext(options))
        {
            await EdgeOutboxSqliteDatabaseInitializer.EnsureCreatedAsync(
                db,
                DavisOutboxPayloadTypes.Raw,
                DavisOutboxPayloadTypes.RawVersion);
        }

        await using (var db = new OutboxDbContext(options))
        {
            var store = new EdgeOutboxStore<OutboxDbContext>(db);
            var inserted = await store.EnqueueAsync(new EdgeOutboxMessage(
                SourceId: "hvo-davis-01",
                RecordedAtUtc: new DateTime(2026, 5, 28, 22, 0, 0, DateTimeKind.Utc),
                PayloadType: DavisOutboxPayloadTypes.Raw,
                PayloadVersion: DavisOutboxPayloadTypes.RawVersion,
                PayloadJson: "{}"), CancellationToken.None);

            inserted.Should().BeTrue();
            var row = await db.OutboxRecords.SingleAsync();
            row.PayloadType.Should().Be(DavisOutboxPayloadTypes.Raw);
            row.PayloadVersion.Should().Be(DavisOutboxPayloadTypes.RawVersion);
        }
    }

    [TestMethod]
    public async Task DavisLegacyOutboxMigrator_MigratesLegacyArchiveRowsBeforeSharedInitialization()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = CreateOptions(connection);

        await using (var db = new OutboxDbContext(options))
        {
            await CreateLegacyDavisOutboxSchemaAsync(db);
            await DavisLegacyOutboxMigrator.MigrateAsync(db, "hvo-davis-01");
            await EdgeOutboxSqliteDatabaseInitializer.EnsureCreatedAsync(
                db,
                DavisOutboxPayloadTypes.Raw,
                DavisOutboxPayloadTypes.RawVersion);
        }

        await using (var db = new OutboxDbContext(options))
        {
            var row = await db.OutboxRecords.SingleAsync();
            row.SourceId.Should().Be("hvo-davis-01");
            row.DeviceId.Should().BeNull();
            row.PayloadType.Should().Be(DavisOutboxPayloadTypes.Archive);
            row.PayloadVersion.Should().Be(DavisOutboxPayloadTypes.RawVersion);
            row.PayloadJson.Should().Be("{}");
        }
    }

    private static async Task CreateLegacyDavisOutboxSchemaAsync(OutboxDbContext db)
    {
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE OutboxRecords (
                Id INTEGER NOT NULL CONSTRAINT PK_OutboxRecords PRIMARY KEY AUTOINCREMENT,
                RecordedAtUtc TEXT NOT NULL,
                Payload TEXT NOT NULL,
                IsArchiveRecord INTEGER NOT NULL,
                Status INTEGER NOT NULL,
                AttemptCount INTEGER NOT NULL,
                LastAttemptedAtUtc TEXT NULL,
                SentAtUtc TEXT NULL,
                NextRetryAtUtc TEXT NOT NULL,
                LastError TEXT NULL,
                FailureKind INTEGER NOT NULL,
                CreatedAtUtc TEXT NOT NULL
            )
            """);
        await db.Database.ExecuteSqlRawAsync("""
            CREATE UNIQUE INDEX IX_OutboxRecords_RecordedAtUtc
            ON OutboxRecords (RecordedAtUtc)
            """);
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO OutboxRecords (
                RecordedAtUtc,
                Payload,
                IsArchiveRecord,
                Status,
                AttemptCount,
                NextRetryAtUtc,
                FailureKind,
                CreatedAtUtc)
            VALUES (
                '2026-05-28T22:00:00Z',
                '{{}}',
                1,
                0,
                0,
                '0001-01-01T00:00:00Z',
                0,
                '2026-05-28T22:00:00Z')
            """);
    }

    private static DbContextOptions<OutboxDbContext> CreateOptions(SqliteConnection connection) =>
        new DbContextOptionsBuilder<OutboxDbContext>()
            .UseSqlite(connection)
            .Options;
}
