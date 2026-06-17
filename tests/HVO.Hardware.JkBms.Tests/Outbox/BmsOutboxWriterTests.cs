using FluentAssertions;
using HVO.Edge.Outbox;
using HVO.Hardware.JkBms.Bms;
using HVO.Hardware.JkBms.Outbox;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HVO.Hardware.JkBms.Tests.Outbox;

[TestClass]
public sealed class BmsOutboxWriterTests
{
    private SqliteConnection _connection = null!;
    private OutboxDbContext _db = null!;
    private BmsOutboxWriter _writer = null!;

    [TestInitialize]
    public void Initialize()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _db = new OutboxDbContext(new DbContextOptionsBuilder<OutboxDbContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();
        _writer = new BmsOutboxWriter(new EdgeOutboxStore<OutboxDbContext>(_db), NullLogger<BmsOutboxWriter>.Instance);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [TestMethod]
    public async Task EnqueueAsync_WritesReadingConfigAndDeviceInfoPayloadTypes()
    {
        var recordedAt = new DateTime(2026, 06, 16, 12, 00, 00, DateTimeKind.Utc);
        var reading = new BmsIngressRecord
        {
            Reading = new BmsDeviceReading
            {
                DeviceAddress = "AA:BB:CC:DD:EE:FF",
                DeviceAlias = "bank-1a",
                RecordedAtUtc = recordedAt,
            },
        };

        await _writer.EnqueueReadingAsync("AA:BB:CC:DD:EE:FF", "bank-1a", recordedAt, reading, CancellationToken.None);
        await _writer.EnqueueConfigAsync("AA:BB:CC:DD:EE:FF", "bank-1a", new BmsConfigPayload { CellCount = 16 }, CancellationToken.None);
        await _writer.EnqueueDeviceInfoAsync("AA:BB:CC:DD:EE:FF", "bank-1a", new BmsDeviceInfoPayload { DeviceName = "bms-a" }, CancellationToken.None);

        var rows = _db.OutboxRecords.OrderBy(r => r.Id).ToList();
        rows.Select(r => r.PayloadType).Should().Equal(
            BmsOutboxPayloadTypes.Reading,
            BmsOutboxPayloadTypes.Config,
            BmsOutboxPayloadTypes.DeviceInfo);
        rows.Should().OnlyContain(r => r.SourceId == "AA:BB:CC:DD:EE:FF" && r.DeviceId == "bank-1a");
        rows.Should().OnlyContain(r => r.Status == EdgeOutboxStatus.Pending);
    }

    [TestMethod]
    public async Task EnqueueReadingAsync_DeduplicatesBySourcePayloadTypeAndRecordedAt()
    {
        var recordedAt = new DateTime(2026, 06, 16, 12, 00, 00, DateTimeKind.Utc);
        var reading = new BmsIngressRecord
        {
            Reading = new BmsDeviceReading
            {
                DeviceAddress = "AA:BB:CC:DD:EE:FF",
                DeviceAlias = "bank-1a",
                RecordedAtUtc = recordedAt,
            },
        };

        var first = await _writer.EnqueueReadingAsync("AA:BB:CC:DD:EE:FF", "bank-1a", recordedAt, reading, CancellationToken.None);
        var duplicate = await _writer.EnqueueReadingAsync("AA:BB:CC:DD:EE:FF", "bank-1a", recordedAt, reading, CancellationToken.None);

        first.Should().BeTrue();
        duplicate.Should().BeFalse();
        _db.OutboxRecords.Should().ContainSingle();
    }
}
