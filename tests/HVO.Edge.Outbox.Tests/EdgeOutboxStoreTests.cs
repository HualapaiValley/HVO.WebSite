using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace HVO.Edge.Outbox.Tests;

[TestClass]
public sealed class EdgeOutboxStoreTests
{
    private SqliteConnection _conn = null!;
    private TestOutboxDbContext _db = null!;
    private EdgeOutboxStore<TestOutboxDbContext> _store = null!;

    [TestInitialize]
    public void Setup()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();
        _db = new TestOutboxDbContext(new DbContextOptionsBuilder<TestOutboxDbContext>().UseSqlite(_conn).Options);
        _db.Database.EnsureCreated();
        _store = new EdgeOutboxStore<TestOutboxDbContext>(_db);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _db.Dispose();
        _conn.Dispose();
    }

    [TestMethod]
    public async Task EnqueueAsync_PersistsPayloadMetadata()
    {
        var inserted = await _store.EnqueueAsync(Message("power.reading", "2026-05-23T10:00:00Z"), CancellationToken.None);

        inserted.Should().BeTrue();
        var row = _db.OutboxRecords.Single();
        row.SourceId.Should().Be("solarassistant-total");
        row.DeviceId.Should().Be("total");
        row.PayloadType.Should().Be("power.reading");
        row.PayloadVersion.Should().Be("1");
        row.Status.Should().Be(EdgeOutboxStatus.Pending);
        row.PayloadJson.Should().Contain("pvPowerW");
    }

    [TestMethod]
    public async Task EnqueueAsync_DedupesBySourcePayloadTypeAndTimestamp()
    {
        var first = await _store.EnqueueAsync(Message("power.reading", "2026-05-23T10:00:00Z"), CancellationToken.None);
        var duplicate = await _store.EnqueueAsync(Message("power.reading", "2026-05-23T10:00:00Z"), CancellationToken.None);
        var differentType = await _store.EnqueueAsync(Message("power.energy", "2026-05-23T10:00:00Z"), CancellationToken.None);

        first.Should().BeTrue();
        duplicate.Should().BeFalse();
        differentType.Should().BeTrue();
        _db.OutboxRecords.Should().HaveCount(2);
    }

    [TestMethod]
    public async Task GetReadyBatchAsync_IsolatesPayloadTypes()
    {
        await _store.EnqueueAsync(Message("power.reading", "2026-05-23T10:00:00Z"), CancellationToken.None);
        await _store.EnqueueAsync(Message("power.energy", "2026-05-23T10:00:00Z"), CancellationToken.None);

        var batch = await _store.GetReadyBatchAsync("power.reading", DateTime.UtcNow, 10, CancellationToken.None);

        batch.Should().ContainSingle();
        batch[0].PayloadType.Should().Be("power.reading");
    }

    [TestMethod]
    public async Task ScheduleRetry_MarksFailedAtRetryLimit()
    {
        await _store.EnqueueAsync(Message("power.reading", "2026-05-23T10:00:00Z"), CancellationToken.None);
        var record = _db.OutboxRecords.Single();
        _store.MarkAttempt([record], DateTime.UtcNow);

        _store.ScheduleRetry(record, "temporary", DateTime.UtcNow, maxRetryAttempts: 1, maxBackoffSeconds: 30);

        record.Status.Should().Be(EdgeOutboxStatus.Failed);
        record.LastError.Should().Be("temporary");
    }

    [TestMethod]
    public async Task CompactSentAsync_DeletesExpiredSentRecords()
    {
        await _store.EnqueueAsync(Message("power.reading", "2026-05-23T10:00:00Z"), CancellationToken.None);
        var record = _db.OutboxRecords.Single();
        _store.MarkSent(record, DateTime.UtcNow.AddDays(-8));
        await _store.SaveChangesAsync(CancellationToken.None);

        var deleted = await _store.CompactSentAsync(TimeSpan.FromDays(7), CancellationToken.None);

        deleted.Should().Be(1);
        _db.OutboxRecords.Should().BeEmpty();
    }

    private static EdgeOutboxMessage Message(string payloadType, string recordedAt) => new(
        SourceId: "solarassistant-total",
        DeviceId: "total",
        RecordedAtUtc: DateTime.Parse(recordedAt, null, System.Globalization.DateTimeStyles.RoundtripKind),
        PayloadType: payloadType,
        PayloadVersion: "1",
        PayloadJson: "{\"pvPowerW\":1200}");

    private sealed class TestOutboxDbContext(DbContextOptions<TestOutboxDbContext> options) : EdgeOutboxDbContext(options);
}
