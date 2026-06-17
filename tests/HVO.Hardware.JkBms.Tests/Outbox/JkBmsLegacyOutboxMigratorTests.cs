using FluentAssertions;
using HVO.Edge.Outbox;
using HVO.Hardware.JkBms.Outbox;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace HVO.Hardware.JkBms.Tests.Outbox;

[TestClass]
public sealed class JkBmsLegacyOutboxMigratorTests
{
    [TestMethod]
    public async Task MigrateAsync_CopiesLegacyDeviceColumns_BeforeSharedInitialization()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<OutboxDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new OutboxDbContext(options);
        await CreateLegacySchemaAsync(db);

        await JkBmsLegacyOutboxMigrator.MigrateAsync(db);
        await EdgeOutboxSqliteDatabaseInitializer.EnsureCreatedAsync(
            db,
            BmsOutboxPayloadTypes.Reading,
            BmsOutboxPayloadTypes.ReadingVersion);

        var row = await db.OutboxRecords.SingleAsync();
        row.SourceId.Should().Be("AA:BB:CC:DD:EE:FF");
        row.DeviceId.Should().Be("bank-1a");
        row.PayloadType.Should().Be(BmsOutboxPayloadTypes.Reading);
        row.PayloadVersion.Should().Be(BmsOutboxPayloadTypes.ReadingVersion);
        row.PayloadJson.Should().Be("{}");
    }

    private static async Task CreateLegacySchemaAsync(OutboxDbContext db)
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
            CREATE UNIQUE INDEX IX_OutboxRecords_DeviceAddress_RecordedAtUtc
            ON OutboxRecords (DeviceAddress, RecordedAtUtc)
            """);
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO OutboxRecords (
                DeviceAddress,
                DeviceAlias,
                RecordedAtUtc,
                Payload,
                Status,
                AttemptCount,
                NextRetryAtUtc,
                CreatedAtUtc)
            VALUES (
                'AA:BB:CC:DD:EE:FF',
                'bank-1a',
                '2026-06-16T12:00:00Z',
                '{{}}',
                0,
                0,
                '0001-01-01T00:00:00Z',
                '2026-06-16T12:00:00Z')
            """);
    }
}
