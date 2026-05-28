using FluentAssertions;
using HVO.Edge.Outbox;
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
    private EdgeOutboxStore<OutboxDbContext> _store = null!;
    private PowerOutboxWriter _writer = null!;

    [TestInitialize]
    public void Setup()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();
        _db = new OutboxDbContext(new DbContextOptionsBuilder<OutboxDbContext>().UseSqlite(_conn).Options);
        _db.Database.EnsureCreated();
        _store = new EdgeOutboxStore<OutboxDbContext>(_db);
        _writer = new PowerOutboxWriter(_store, NullLogger<PowerOutboxWriter>.Instance);
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
        row.PayloadType.Should().Be(PowerOutboxPayloadTypes.PowerReading);
        row.PayloadVersion.Should().Be(PowerOutboxPayloadTypes.PowerReadingVersion);
        row.Status.Should().Be(EdgeOutboxStatus.Pending);
        row.PayloadJson.Should().Contain("pvPowerW");
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
