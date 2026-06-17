using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace HVO.Edge.Outbox.Tests;

[TestClass]
public sealed class EdgeOutboxSqliteDatabaseInitializerTests
{
    [TestMethod]
    public async Task EnsureCreatedAsync_AddsPayloadMetadataColumnsToLegacyOutboxTable()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                CREATE TABLE OutboxRecords (
                    Id INTEGER NOT NULL CONSTRAINT PK_OutboxRecords PRIMARY KEY AUTOINCREMENT,
                    SourceId TEXT NOT NULL,
                    DeviceId TEXT NULL,
                    RecordedAtUtc TEXT NOT NULL,
                    Payload TEXT NOT NULL,
                    Status INTEGER NOT NULL,
                    AttemptCount INTEGER NOT NULL,
                    LastAttemptedAtUtc TEXT NULL,
                    SentAtUtc TEXT NULL,
                    NextRetryAtUtc TEXT NOT NULL,
                    LastError TEXT NULL,
                    CreatedAtUtc TEXT NOT NULL
                );
                CREATE UNIQUE INDEX IX_OutboxRecords_SourceId_RecordedAtUtc ON OutboxRecords (SourceId, RecordedAtUtc);
                INSERT INTO OutboxRecords (SourceId, DeviceId, RecordedAtUtc, Payload, Status, AttemptCount, NextRetryAtUtc, CreatedAtUtc)
                VALUES ('solarassistant-total', 'total', '2026-05-23T10:00:00Z', '{""pvPowerW"":1200}', 0, 0, '0001-01-01T00:00:00', '2026-05-23T10:00:00Z');
                """;
            await command.ExecuteNonQueryAsync();
        }

        await using var db = new TestOutboxDbContext(new DbContextOptionsBuilder<TestOutboxDbContext>().UseSqlite(connection).Options);

        await EdgeOutboxSqliteDatabaseInitializer.EnsureCreatedAsync(db, "power.reading", "1");

        var row = db.OutboxRecords.Single();
        row.PayloadType.Should().Be("power.reading");
        row.PayloadVersion.Should().Be("1");
        row.FailureKind.Should().Be(EdgeOutboxFailureKind.None);
        row.PayloadJson.Should().Contain("pvPowerW");

        var store = new EdgeOutboxStore<TestOutboxDbContext>(db);
        var inserted = await store.EnqueueAsync(new EdgeOutboxMessage(
            SourceId: "solarassistant-total",
            DeviceId: "total",
            RecordedAtUtc: DateTime.Parse("2026-05-23T10:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind),
            PayloadType: "power.energy",
            PayloadVersion: "1",
            PayloadJson: "{\"pvEnergyKwh\":42}"), CancellationToken.None);

        inserted.Should().BeTrue();
        db.OutboxRecords.Should().HaveCount(2);
    }

    [TestMethod]
    public async Task EnsureCreatedAsync_ThrowsClearError_WhenLegacyNotNullColumnBlocksSharedInserts()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                CREATE TABLE OutboxRecords (
                    Id INTEGER NOT NULL CONSTRAINT PK_OutboxRecords PRIMARY KEY AUTOINCREMENT,
                    SourceId TEXT NOT NULL,
                    DeviceId TEXT NULL,
                    DeviceAddress TEXT NOT NULL,
                    RecordedAtUtc TEXT NOT NULL,
                    Payload TEXT NOT NULL,
                    Status INTEGER NOT NULL,
                    AttemptCount INTEGER NOT NULL,
                    LastAttemptedAtUtc TEXT NULL,
                    SentAtUtc TEXT NULL,
                    NextRetryAtUtc TEXT NOT NULL,
                    LastError TEXT NULL,
                    CreatedAtUtc TEXT NOT NULL
                );
                """;
            await command.ExecuteNonQueryAsync();
        }

        await using var db = new TestOutboxDbContext(new DbContextOptionsBuilder<TestOutboxDbContext>().UseSqlite(connection).Options);

        var act = async () => await EdgeOutboxSqliteDatabaseInitializer.EnsureCreatedAsync(db, "bms.reading", "1");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*DeviceAddress*legacy NOT NULL column with no default*");
    }

    [TestMethod]
    public async Task ValidateAsync_ReturnsWarningOnly_ForNullableLegacyColumn()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                CREATE TABLE OutboxRecords (
                    Id INTEGER NOT NULL CONSTRAINT PK_OutboxRecords PRIMARY KEY AUTOINCREMENT,
                    SourceId TEXT NOT NULL,
                    DeviceId TEXT NULL,
                    DeviceAddress TEXT NULL,
                    PayloadType TEXT NOT NULL DEFAULT 'bms.reading',
                    PayloadVersion TEXT NOT NULL DEFAULT '1',
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
                );
                """;
            await command.ExecuteNonQueryAsync();
        }

        await using var db = new TestOutboxDbContext(new DbContextOptionsBuilder<TestOutboxDbContext>().UseSqlite(connection).Options);

        var result = await EdgeOutboxSchemaValidator.ValidateAsync(db);

        result.IsCompatible.Should().BeTrue();
        result.Errors.Should().BeEmpty();
        result.Warnings.Should().Contain(warning => warning.Contains("DeviceAddress", StringComparison.Ordinal));
    }

    private sealed class TestOutboxDbContext(DbContextOptions<TestOutboxDbContext> options) : EdgeOutboxDbContext(options);
}
