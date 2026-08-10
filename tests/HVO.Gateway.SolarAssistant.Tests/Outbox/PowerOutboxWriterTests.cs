using FluentAssertions;
using HVO.Edge.Contracts.PowerSystem;
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

    [TestMethod]
    public async Task MpptWriter_PreservesRepeatedHistoricalSamplesAndSameTimePayloadTypes()
    {
        var detailWriter = new PowerInventoryConfigurationWriter(_store);
        var recordedAt = DateTime.Parse("2026-08-10T18:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind);
        var mppt = MakeMpptPayload(recordedAt);

        (await _writer.EnqueueAsync(MakePayload("2026-08-10T18:00:00Z"), CancellationToken.None)).Should().BeTrue();
        (await detailWriter.EnqueueMpptDetailAsync(mppt, CancellationToken.None)).Should().BeTrue();
        (await detailWriter.EnqueueMpptDetailAsync(MakeMpptPayload(recordedAt.AddSeconds(30)), CancellationToken.None)).Should().BeTrue();

        var rows = _db.OutboxRecords.OrderBy(r => r.RecordedAtUtc).ThenBy(r => r.PayloadType).ToArray();
        rows.Should().HaveCount(3);
        rows.Count(r => r.PayloadType == PowerOutboxPayloadTypes.MpptDetail).Should().Be(2);
        rows.Where(r => r.RecordedAtUtc == recordedAt).Select(r => r.PayloadType).Should().BeEquivalentTo(
            PowerOutboxPayloadTypes.PowerReading,
            PowerOutboxPayloadTypes.MpptDetail);
        rows.Where(r => r.PayloadType == PowerOutboxPayloadTypes.MpptDetail)
            .Should().OnlyContain(r => r.PayloadVersion == PowerOutboxPayloadTypes.MpptDetailVersion);
    }

    private static PowerReadingPayload MakePayload(string recordedAt) => new()
    {
        SourceId = "solarassistant-total",
        SourceSystem = "solarassistant",
        DeviceId = "total",
        RecordedAtUtc = DateTime.Parse(recordedAt, null, System.Globalization.DateTimeStyles.RoundtripKind),
        PvPowerW = 1200,
    };

    private static PowerMpptDetailPayload MakeMpptPayload(DateTime recordedAt) => new()
    {
        SourceId = "solarassistant-total",
        SourceSystem = "solarassistant",
        DeviceId = "inverter_1",
        RecordedAtUtc = recordedAt,
        Trackers =
        [
            new PowerMpptTrackerDetail
            {
                TrackerId = "mppt-1",
                Name = "6500EX MPPT 1",
                VoltageV = 121.4,
                CurrentA = 5.05,
                PowerW = 612.5,
                Provenance = PowerObservationProvenance.Direct,
                Confidence = "source-direct",
            },
        ],
    };
}
