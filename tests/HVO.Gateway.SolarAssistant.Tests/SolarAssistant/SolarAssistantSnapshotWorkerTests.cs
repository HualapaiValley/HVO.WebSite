using System.Text.Json;
using FluentAssertions;
using HVO.Edge.Contracts.PowerSystem;
using HVO.Edge.Outbox;
using HVO.Gateway.SolarAssistant.Configuration;
using HVO.Gateway.SolarAssistant.Outbox;
using HVO.Gateway.SolarAssistant.SolarAssistant;
using HVO.Gateway.SolarAssistant.SolarAssistant.Mqtt;
using HVO.Gateway.SolarAssistant.Telemetry;
using HVO.Gateway.SolarAssistant.Workers;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HVO.Gateway.SolarAssistant.Tests.SolarAssistant;

[TestClass]
public sealed class SolarAssistantSnapshotWorkerTests
{
    private SqliteConnection _conn = null!;
    private ServiceProvider _provider = null!;

    [TestInitialize]
    public void Setup()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<OutboxDbContext>(o => o.UseSqlite(_conn));
        services.AddScoped<EdgeOutboxStore<OutboxDbContext>>();
        services.AddScoped<PowerOutboxWriter>();
        services.AddScoped<PowerInventoryConfigurationWriter>();
        services.AddSingleton<SolarAssistantMqttInventoryStore>();
        services.AddSingleton<IOptions<SolarAssistantOptions>>(Options.Create(new SolarAssistantOptions
        {
            Host = "solarassistant.local",
            TotalSourceId = "solarassistant-total",
            TotalDeviceId = "total",
            HistoryCapacity = 2,
        }));
        services.AddSingleton<FakeSolarAssistantClient>(_ => new FakeSolarAssistantClient(Metrics(1234, 567, 0, -100)));
        services.AddSingleton<ISolarAssistantClient>(sp => sp.GetRequiredService<FakeSolarAssistantClient>());
        services.AddSingleton(sp => new SolarAssistantSnapshotWorker(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<ISolarAssistantClient>(),
            sp.GetRequiredService<IOptions<SolarAssistantOptions>>(),
            NullLogger<SolarAssistantSnapshotWorker>.Instance,
            new SolarAssistantTelemetry()));

        _provider = services.BuildServiceProvider();
        using var scope = _provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<OutboxDbContext>().Database.EnsureCreated();
    }

    [TestCleanup]
    public void Cleanup()
    {
        _provider.Dispose();
        _conn.Dispose();
    }

    [TestMethod]
    public async Task PollOnceAsync_QueuesMappedPowerSnapshot()
    {
        var worker = _provider.GetRequiredService<SolarAssistantSnapshotWorker>();

        var queued = await worker.PollOnceAsync(CancellationToken.None);

        queued.Should().BeTrue();
        worker.LastMetricCount.Should().Be(4);
        worker.LastSnapshotAt.Should().NotBeNull();
        worker.LastError.Should().BeNull();
        worker.LastSnapshot.Should().NotBeNull();
        worker.LastSnapshot!.SourceId.Should().Be("solarassistant-total");
        worker.LastSnapshot.PvPowerW.Should().Be(1234);
        worker.LastInventory.Should().NotBeNull();
        worker.LastInventory!.MetricCount.Should().Be(4);
        worker.LastInventory.Topics.Should().Contain(t => t.Topic == "total/pv_power");
        worker.History.Should().ContainSingle();
        worker.History[0].PvPowerW.Should().Be(1234);

        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OutboxDbContext>();
        var row = db.OutboxRecords.Single();
        row.SourceId.Should().Be("solarassistant-total");
        row.PayloadType.Should().Be(PowerOutboxPayloadTypes.PowerReading);
        row.PayloadVersion.Should().Be(PowerOutboxPayloadTypes.PowerReadingVersion);
        row.PayloadJson.Should().Contain("1234");
        row.PayloadJson.Should().Contain("pvPowerW");
    }

    [TestMethod]
    public async Task PollOnceAsync_RetainsConfiguredRollingHistoryCapacity()
    {
        var client = _provider.GetRequiredService<FakeSolarAssistantClient>();
        var worker = _provider.GetRequiredService<SolarAssistantSnapshotWorker>();

        await worker.PollOnceAsync(CancellationToken.None);
        client.SetMetrics(Metrics(2000, 700, 10, -150));
        await worker.PollOnceAsync(CancellationToken.None);
        client.SetMetrics(Metrics(3000, 800, 20, -200));
        await worker.PollOnceAsync(CancellationToken.None);

        worker.History.Should().HaveCount(2);
        worker.History[0].PvPowerW.Should().Be(2000);
        worker.History[1].PvPowerW.Should().Be(3000);
        worker.History.Select(h => h.RecordedAtUtc).Should().BeInAscendingOrder();
    }

    [TestMethod]
    public async Task PollOnceAsync_QueuesMpptDetailForEverySnapshotEvenWhenValuesRepeat()
    {
        var client = _provider.GetRequiredService<FakeSolarAssistantClient>();
        client.SetMetrics(
        [
            .. Metrics(1234, 567, 0, -100),
            new SolarAssistantMetric { Topic = "inverter_1/pv_power_1", Value = 612.5 },
            new SolarAssistantMetric { Topic = "inverter_1/pv_voltage_1", Value = 121.4 },
            new SolarAssistantMetric { Topic = "inverter_1/pv_current_1", Value = 5.05 },
        ]);
        var worker = _provider.GetRequiredService<SolarAssistantSnapshotWorker>();

        await worker.PollOnceAsync(CancellationToken.None);
        await worker.PollOnceAsync(CancellationToken.None);

        using var scope = _provider.CreateScope();
        var rows = await scope.ServiceProvider.GetRequiredService<OutboxDbContext>().OutboxRecords
            .Where(r => r.PayloadType == PowerOutboxPayloadTypes.MpptDetail)
            .OrderBy(r => r.RecordedAtUtc)
            .ToArrayAsync();
        rows.Should().HaveCount(2);
        rows.Select(r => r.RecordedAtUtc).Distinct().Should().HaveCount(2);
        rows.Should().OnlyContain(r => r.SourceId == "solarassistant-total" && r.DeviceId == "inverter_1");
        var payloads = rows.Select(r => JsonSerializer.Deserialize<PowerMpptDetailPayload>(
            r.PayloadJson,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!).ToArray();
        payloads.Should().OnlyContain(p => p.Trackers.Count == 1 && p.Trackers[0].PowerW == 612.5);
    }

    [TestMethod]
    public async Task PollOnceAsync_NoMetrics_HydratesLatestSnapshotFromOutbox()
    {
        var recordedAt = DateTime.Parse("2026-05-23T10:05:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind);
        using (var scope = _provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OutboxDbContext>();
            db.OutboxRecords.Add(new EdgeOutboxRecord
            {
                SourceId = "solarassistant-total",
                DeviceId = "total",
                PayloadType = PowerOutboxPayloadTypes.PowerReading,
                PayloadVersion = PowerOutboxPayloadTypes.PowerReadingVersion,
                RecordedAtUtc = recordedAt.AddMinutes(-5),
                PayloadJson = JsonSerializer.Serialize(new PowerReadingPayload
                {
                    SourceId = "solarassistant-total",
                    DeviceId = "total",
                    RecordedAtUtc = recordedAt.AddMinutes(-5),
                    PvPowerW = 111,
                }, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            });
            db.OutboxRecords.Add(new EdgeOutboxRecord
            {
                SourceId = "solarassistant-total",
                DeviceId = "total",
                PayloadType = PowerOutboxPayloadTypes.PowerReading,
                PayloadVersion = PowerOutboxPayloadTypes.PowerReadingVersion,
                RecordedAtUtc = recordedAt,
                PayloadJson = JsonSerializer.Serialize(new PowerReadingPayload
                {
                    SourceId = "solarassistant-total",
                    DeviceId = "total",
                    RecordedAtUtc = recordedAt,
                    PvPowerW = 222,
                    LoadPowerW = 333,
                }, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            });
            await db.SaveChangesAsync();
        }

        var client = _provider.GetRequiredService<FakeSolarAssistantClient>();
        client.SetMetrics([]);
        var worker = _provider.GetRequiredService<SolarAssistantSnapshotWorker>();

        var queued = await worker.PollOnceAsync(CancellationToken.None);

        queued.Should().BeFalse();
        worker.LastMetricCount.Should().Be(0);
        worker.LastSnapshot.Should().NotBeNull();
        worker.LastSnapshot!.PvPowerW.Should().Be(222);
        worker.LastSnapshot.LoadPowerW.Should().Be(333);
        worker.LastSnapshotAt.Should().Be(recordedAt);
        worker.History.Should().ContainSingle(h => h.PvPowerW == 222);
    }

    [TestMethod]
    public async Task PollOnceAsync_NoMetrics_IgnoresInvalidOutboxPayload()
    {
        using (var scope = _provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OutboxDbContext>();
            db.OutboxRecords.Add(new EdgeOutboxRecord
            {
                SourceId = "solarassistant-total",
                DeviceId = "total",
                PayloadType = PowerOutboxPayloadTypes.PowerReading,
                PayloadVersion = PowerOutboxPayloadTypes.PowerReadingVersion,
                RecordedAtUtc = DateTime.UtcNow,
                PayloadJson = "{not-json",
            });
            await db.SaveChangesAsync();
        }

        var client = _provider.GetRequiredService<FakeSolarAssistantClient>();
        client.SetMetrics([]);
        var worker = _provider.GetRequiredService<SolarAssistantSnapshotWorker>();

        var queued = await worker.PollOnceAsync(CancellationToken.None);

        queued.Should().BeFalse();
        worker.LastSnapshot.Should().BeNull();
        worker.History.Should().BeEmpty();
    }

    private sealed class FakeSolarAssistantClient : ISolarAssistantClient
    {
        private IReadOnlyList<SolarAssistantMetric> _metrics;

        public FakeSolarAssistantClient(IReadOnlyList<SolarAssistantMetric> metrics)
        {
            _metrics = metrics;
        }

        public void SetMetrics(IReadOnlyList<SolarAssistantMetric> metrics) => _metrics = metrics;

        public Task<IReadOnlyList<SolarAssistantMetric>> GetMetricsAsync(CancellationToken ct) => Task.FromResult(_metrics);
    }

    private static IReadOnlyList<SolarAssistantMetric> Metrics(double pv, double load, double grid, double battery) =>
    [
        new SolarAssistantMetric { Topic = "total/pv_power", Value = pv },
        new SolarAssistantMetric { Topic = "total/load_power", Value = load },
        new SolarAssistantMetric { Topic = "total/grid_power", Value = grid },
        new SolarAssistantMetric { Topic = "total/battery_power", Value = battery },
    ];

}
