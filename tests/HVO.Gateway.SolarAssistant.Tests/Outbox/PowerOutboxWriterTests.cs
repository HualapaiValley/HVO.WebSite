using FluentAssertions;
using HVO.Gateway.SolarAssistant.Outbox;
using HVO.Gateway.SolarAssistant.SolarAssistant;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HVO.Gateway.SolarAssistant.Tests.Outbox;

[TestClass]
public sealed class PowerOutboxWriterTests
{
    private SqliteConnection _conn = null!;
    private OutboxDbContext _db = null!;
    private PowerOutboxWriter _writer = null!;

    [TestInitialize]
    public void Setup()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();
        _db = new OutboxDbContext(new DbContextOptionsBuilder<OutboxDbContext>().UseSqlite(_conn).Options);
        _db.Database.EnsureCreated();
        _writer = new PowerOutboxWriter(_db, NullLogger<PowerOutboxWriter>.Instance);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _db.Dispose();
        _conn.Dispose();
    }

    [TestMethod]
    public async Task EnqueueAsync_NewPayload_PersistsPendingRecord()
    {
        var payload = MakePayload("2026-05-23T10:00:00Z");

        var inserted = await _writer.EnqueueAsync(payload, CancellationToken.None);

        inserted.Should().BeTrue();
        var row = _db.OutboxRecords.Single();
        row.SourceId.Should().Be("solarassistant-total");
        row.DeviceId.Should().Be("total");
        row.Status.Should().Be(OutboxStatus.Pending);
        row.Payload.Should().Contain("pvPowerW");
    }

    [TestMethod]
    public async Task EnqueueAsync_DuplicateSourceAndTimestamp_SkipsSecondRecord()
    {
        var payload = MakePayload("2026-05-23T10:05:00Z");

        var first = await _writer.EnqueueAsync(payload, CancellationToken.None);
        var second = await _writer.EnqueueAsync(payload, CancellationToken.None);

        first.Should().BeTrue();
        second.Should().BeFalse();
        _db.OutboxRecords.Count().Should().Be(1);
    }

    private static PowerReadingPayload MakePayload(string recordedAt) => new()
    {
        SourceId = "solarassistant-total",
        SourceSystem = "solarassistant",
        DeviceId = "total",
        RecordedAtUtc = DateTime.Parse(recordedAt, null, System.Globalization.DateTimeStyles.RoundtripKind),
        PvPowerW = 1200,
    };
}
