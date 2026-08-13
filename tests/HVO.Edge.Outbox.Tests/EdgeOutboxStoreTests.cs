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
    public async Task EnqueueAsync_ConcurrentFileBackedDuplicates_InsertExactlyOnce()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"hvo-outbox-{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<TestOutboxDbContext>()
                .UseSqlite($"Data Source={databasePath};Default Timeout=30")
                .Options;
            await using (var initializer = new TestOutboxDbContext(options))
                await initializer.Database.EnsureCreatedAsync();

            var attempts = Enumerable.Range(0, 8).Select(async _ =>
            {
                await using var context = new TestOutboxDbContext(options);
                return await new EdgeOutboxStore<TestOutboxDbContext>(context)
                    .EnqueueAsync(Message("power.reading", "2026-05-23T10:00:00Z"), CancellationToken.None);
            });

            var results = await Task.WhenAll(attempts);
            results.Should().ContainSingle(inserted => inserted);
            await using var verification = new TestOutboxDbContext(options);
            (await verification.OutboxRecords.CountAsync()).Should().Be(1);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
            File.Delete($"{databasePath}-shm");
            File.Delete($"{databasePath}-wal");
        }
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
    public async Task CountAsync_CanFilterByPayloadType()
    {
        await _store.EnqueueAsync(Message("power.reading", "2026-05-23T10:00:00Z"), CancellationToken.None);
        await _store.EnqueueAsync(Message("power.energy", "2026-05-23T10:00:00Z"), CancellationToken.None);
        var failed = _db.OutboxRecords.Single(r => r.PayloadType == "power.energy");
        _store.MarkFailed(failed, "bad data");
        await _store.SaveChangesAsync(CancellationToken.None);

        var readingPending = await _store.CountPendingAsync("power.reading", CancellationToken.None);
        var readingFailed = await _store.CountFailedAsync("power.reading", CancellationToken.None);

        readingPending.Should().Be(1);
        readingFailed.Should().Be(0);
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
    public async Task MarkFailed_SetsFailureKind_ToPermanentByDefault()
    {
        await _store.EnqueueAsync(Message("power.reading", "2026-05-23T10:00:00Z"), CancellationToken.None);
        var record = _db.OutboxRecords.Single();

        _store.MarkFailed(record, "bad data");

        record.Status.Should().Be(EdgeOutboxStatus.Failed);
        record.FailureKind.Should().Be(EdgeOutboxFailureKind.Permanent);
        record.LastError.Should().Be("bad data");
    }

    [TestMethod]
    public async Task ScheduleRetry_SetsFailureKind_ToRetryExhausted_AtLimit()
    {
        await _store.EnqueueAsync(Message("power.reading", "2026-05-23T10:00:00Z"), CancellationToken.None);
        var record = _db.OutboxRecords.Single();
        _store.MarkAttempt([record], DateTime.UtcNow);

        _store.ScheduleRetry(record, "temporary", DateTime.UtcNow, maxRetryAttempts: 1, maxBackoffSeconds: 30);

        record.Status.Should().Be(EdgeOutboxStatus.Failed);
        record.FailureKind.Should().Be(EdgeOutboxFailureKind.RetryExhausted);
    }

    [TestMethod]
    public async Task ScheduleRetry_ResetsFailureKind_ToNone_WhenRetrying()
    {
        await _store.EnqueueAsync(Message("power.reading", "2026-05-23T10:00:00Z"), CancellationToken.None);
        var record = _db.OutboxRecords.Single();
        record.FailureKind = EdgeOutboxFailureKind.Permanent;
        _store.MarkAttempt([record], DateTime.UtcNow);

        _store.ScheduleRetry(record, "temporary", DateTime.UtcNow, maxRetryAttempts: 5, maxBackoffSeconds: 30);

        record.Status.Should().Be(EdgeOutboxStatus.Pending);
        record.FailureKind.Should().Be(EdgeOutboxFailureKind.None);
        record.NextRetryAtUtc.Should().BeAfter(DateTime.MinValue);
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

    [TestMethod]
    public async Task CompactFailedAsync_DeletesExpiredFailedRecords()
    {
        await _store.EnqueueAsync(Message("power.reading", "2026-05-23T10:00:00Z"), CancellationToken.None);
        var record = _db.OutboxRecords.Single();
        _store.MarkFailed(record, "bad data", EdgeOutboxFailureKind.Permanent);
        record.CreatedAtUtc = DateTime.UtcNow.AddDays(-31);
        await _store.SaveChangesAsync(CancellationToken.None);

        var deleted = await _store.CompactFailedAsync(TimeSpan.FromDays(30), CancellationToken.None);

        deleted.Should().Be(1);
        _db.OutboxRecords.Should().BeEmpty();
    }

    [TestMethod]
    public async Task CompactFailedAsync_UsesLastAttemptedAtUtc_WhenPresent()
    {
        await _store.EnqueueAsync(Message("power.reading", "2026-05-23T10:00:00Z"), CancellationToken.None);
        var record = _db.OutboxRecords.Single();
        record.Status = EdgeOutboxStatus.Failed;
        record.FailureKind = EdgeOutboxFailureKind.RetryExhausted;
        record.CreatedAtUtc = DateTime.UtcNow.AddDays(-31);
        record.LastAttemptedAtUtc = DateTime.UtcNow.AddMinutes(-5);
        await _store.SaveChangesAsync(CancellationToken.None);

        var deleted = await _store.CompactFailedAsync(TimeSpan.FromDays(30), CancellationToken.None);

        deleted.Should().Be(0);
        _db.OutboxRecords.Should().ContainSingle();
    }

    [TestMethod]
    public async Task ReclaimFreePagesAsync_RejectsNonPositiveLimit()
    {
        var act = async () => await _store.ReclaimFreePagesAsync(0, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [TestMethod]
    public async Task RequeueRetryExhaustedAsync_MovesRecords_BackToPending()
    {
        await _store.EnqueueAsync(Message("power.reading", "2026-05-23T10:00:00Z"), CancellationToken.None);
        var record = _db.OutboxRecords.Single();
        _store.MarkAttempt([record], DateTime.UtcNow);
        _store.ScheduleRetry(record, "temporary", DateTime.UtcNow, maxRetryAttempts: 1, maxBackoffSeconds: 30);
        await _store.SaveChangesAsync(CancellationToken.None);

        var requeued = await _store.RequeueRetryExhaustedAsync(CancellationToken.None);

        requeued.Should().Be(1);
        record.Status.Should().Be(EdgeOutboxStatus.Pending);
        record.FailureKind.Should().Be(EdgeOutboxFailureKind.None);
        record.AttemptCount.Should().Be(0);
        record.NextRetryAtUtc.Should().Be(DateTime.MinValue);
        record.LastError.Should().Contain("temporary");
        record.LastError.Should().Contain("Requeued after retry exhaustion");
    }

    [TestMethod]
    public async Task CountFailedAsync_FiltersByFailureKind()
    {
        await _store.EnqueueAsync(Message("power.reading", "2026-05-23T10:00:00Z"), CancellationToken.None);
        await _store.EnqueueAsync(Message("power.energy", "2026-05-23T10:00:00Z"), CancellationToken.None);
        var permanent = _db.OutboxRecords.Single(r => r.PayloadType == "power.reading");
        var retryExhausted = _db.OutboxRecords.Single(r => r.PayloadType == "power.energy");
        _store.MarkFailed(permanent, "bad data", EdgeOutboxFailureKind.Permanent);
        _store.MarkFailed(retryExhausted, "offline", EdgeOutboxFailureKind.RetryExhausted);
        await _store.SaveChangesAsync(CancellationToken.None);

        var permanentCount = await _store.CountFailedAsync(EdgeOutboxFailureKind.Permanent, CancellationToken.None);
        var retryExhaustedCount = await _store.CountFailedAsync(EdgeOutboxFailureKind.RetryExhausted, CancellationToken.None);

        permanentCount.Should().Be(1);
        retryExhaustedCount.Should().Be(1);
    }

    [TestMethod]
    public async Task DiagnosticsReader_ReturnsCountsByFailureKindAndMaintenanceState()
    {
        await _store.EnqueueAsync(Message("power.reading", "2026-05-23T10:00:00Z"), CancellationToken.None);
        await _store.EnqueueAsync(Message("power.energy", "2026-05-23T10:01:00Z"), CancellationToken.None);
        var sent = _db.OutboxRecords.Single(record => record.PayloadType == "power.reading");
        var failed = _db.OutboxRecords.Single(record => record.PayloadType == "power.energy");
        _store.MarkSent(sent, DateTime.UtcNow.AddMinutes(-1));
        _store.MarkFailed(failed, "HTTP 400", EdgeOutboxFailureKind.Permanent);
        await _store.SaveChangesAsync(CancellationToken.None);

        var diagnostics = await EdgeOutboxDiagnosticsReader.ReadAsync(_db, CancellationToken.None);

        diagnostics.SentCount.Should().Be(1);
        diagnostics.FailedCount.Should().Be(1);
        diagnostics.FailedCountByKind.Should().ContainKey("Permanent").WhoseValue.Should().Be(1);
        diagnostics.LastError.Should().Be("HTTP 400");
        diagnostics.LastFailureKind.Should().Be("Permanent");
        diagnostics.Schema.IsCompatible.Should().BeTrue();
        diagnostics.MaintenanceState.Should().Be("failed-records-present");
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
