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
    public async Task RequeueRetryExhaustedAsync_RequeuesSharedRetryExhaustedRows()
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
            db.OutboxRecords.Add(new EdgeOutboxRecord
            {
                SourceId = "hvo-davis-01",
                PayloadType = DavisOutboxPayloadTypes.Raw,
                PayloadVersion = DavisOutboxPayloadTypes.RawVersion,
                RecordedAtUtc = new DateTime(2026, 5, 28, 22, 0, 0, DateTimeKind.Utc),
                PayloadJson = "{}",
                Status = EdgeOutboxStatus.Failed,
                FailureKind = EdgeOutboxFailureKind.RetryExhausted,
                AttemptCount = 10,
                NextRetryAtUtc = new DateTime(2026, 5, 28, 23, 0, 0, DateTimeKind.Utc),
                LastError = "HTTP 503",
            });
            await db.SaveChangesAsync();

            var store = new EdgeOutboxStore<OutboxDbContext>(db);
            var requeued = await store.RequeueRetryExhaustedAsync(CancellationToken.None);
            requeued.Should().Be(1);
        }

        await using (var db = new OutboxDbContext(options))
        {
            var row = await db.OutboxRecords.SingleAsync();
            row.Status.Should().Be(EdgeOutboxStatus.Pending);
            row.FailureKind.Should().Be(EdgeOutboxFailureKind.None);
            row.AttemptCount.Should().Be(0);
            row.NextRetryAtUtc.Should().Be(DateTime.MinValue);
            row.LastError.Should().Contain("HTTP 503");
            row.LastError.Should().Contain("Requeued after retry exhaustion");
        }
    }

    private static DbContextOptions<OutboxDbContext> CreateOptions(SqliteConnection connection) =>
        new DbContextOptionsBuilder<OutboxDbContext>()
            .UseSqlite(connection)
            .Options;
}
