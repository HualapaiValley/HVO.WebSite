using System.Text.Json;
using FluentAssertions;
using HVO.Edge.Contracts;
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
    [TestMethod]
    public async Task EnqueueAsync_PersistsOneCompleteCanonicalBundleAndDeduplicates()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var dbOptions = new DbContextOptionsBuilder<DefaultEdgeOutboxDbContext>().UseSqlite(connection).Options;
        await using var db = new DefaultEdgeOutboxDbContext(dbOptions);
        await EdgeOutboxSqliteDatabaseInitializer.EnsureCreatedAsync(db, EdgePayloadTypes.BmsReading, "1");
        var writer = new BmsOutboxWriter(
            new EdgeOutboxStore<DefaultEdgeOutboxDbContext>(db),
            NullLogger<BmsOutboxWriter>.Instance);
        var recordedAt = new DateTime(2026, 8, 11, 12, 0, 0, DateTimeKind.Utc);
        var bundle = Bundle(recordedAt);

        (await writer.EnqueueAsync("AA:BB:CC:DD:EE:FF", "bank-1a", recordedAt, bundle, CancellationToken.None)).Should().BeTrue();
        (await writer.EnqueueAsync("AA:BB:CC:DD:EE:FF", "bank-1a", recordedAt, bundle, CancellationToken.None)).Should().BeFalse();

        var row = await db.OutboxRecords.SingleAsync();
        row.PayloadType.Should().Be(EdgePayloadTypes.BmsReading);
        row.DeviceId.Should().Be("bank-1a");
        var persisted = JsonSerializer.Deserialize<BmsIngressRecord>(row.PayloadJson, JsonSerializerOptions.Web);
        persisted.Should().NotBeNull();
        persisted!.Reading.CurrentMa.Should().Be(2500);
        persisted.Config.Should().NotBeNull();
        persisted.DeviceInfo.Should().NotBeNull();
    }

    internal static BmsIngressRecord Bundle(DateTime recordedAt, string address = "AA:BB:CC:DD:EE:FF") => new()
    {
        Reading = new BmsDeviceReading
        {
            DeviceAddress = address,
            DeviceAlias = "bank-1a",
            RecordedAtUtc = recordedAt,
            TotalVoltageMv = 52_000,
            CurrentMa = 2_500,
        },
        Config = new BmsConfigPayload { CellCount = 16 },
        DeviceInfo = new BmsDeviceInfoPayload { DeviceName = "bms-a" },
    };
}
