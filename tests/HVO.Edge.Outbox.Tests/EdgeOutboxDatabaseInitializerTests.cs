using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace HVO.Edge.Outbox.Tests;

[TestClass]
public sealed class EdgeOutboxDatabaseInitializerTests
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

        await EdgeOutboxDatabaseInitializer.EnsureCreatedAsync(db, "power.reading", "1");

        var row = db.OutboxRecords.Single();
        row.PayloadType.Should().Be("power.reading");
        row.PayloadVersion.Should().Be("1");
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

    private sealed class TestOutboxDbContext(DbContextOptions<TestOutboxDbContext> options) : EdgeOutboxDbContext(options);
}
