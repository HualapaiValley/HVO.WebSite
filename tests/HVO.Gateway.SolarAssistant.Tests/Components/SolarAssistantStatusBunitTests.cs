using System.Reflection;
using Bunit;
using FluentAssertions;
using HVO.Edge.Outbox;
using HVO.Gateway.SolarAssistant.Components.Pages;
using HVO.Gateway.SolarAssistant.Configuration;
using HVO.Gateway.SolarAssistant.Outbox;
using HVO.Gateway.SolarAssistant.SolarAssistant;
using HVO.Gateway.SolarAssistant.SolarAssistant.Health;
using HVO.Gateway.SolarAssistant.SolarAssistant.Mqtt;
using HVO.Gateway.SolarAssistant.Workers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MudBlazor.Services;

namespace HVO.Gateway.SolarAssistant.Tests.Components;

[TestClass]
public sealed class SolarAssistantStatusBunitTests : BunitContext
{
    public SolarAssistantStatusBunitTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddHttpClient();
    }

    [TestMethod]
    public void RendersSnapshotHealthOutboxAndInventorySections()
    {
        var fixture = RegisterFixture(withSnapshot: true, withInventory: true, mqttConnected: true);

        var component = Render<Status>();

        component.Markup.Should().Contain("Current Snapshot");
        component.Markup.Should().Contain("1250 W");
        component.Markup.Should().Contain("Website Forwarding");
        component.Markup.Should().Contain("REST Metric Inventory");
        component.Markup.Should().Contain("Home Assistant Discovery");
        component.Markup.Should().Contain("Gateway healthy");
        fixture.SnapshotWorker.Dispose();
    }

    [TestMethod]
    public void RendersAlertsWhenHealthIsWarning()
    {
        var fixture = RegisterFixture(withSnapshot: false, withInventory: false, mqttConnected: false);

        var component = Render<Status>();

        component.Markup.Should().Contain("Gateway Alerts");
        component.Markup.Should().Contain("rest-waiting");
        component.Markup.Should().Contain("Waiting for first REST snapshot.");
        fixture.SnapshotWorker.Dispose();
    }

    [TestMethod]
    public void RendersEmptyHistoryState()
    {
        var fixture = RegisterFixture(withSnapshot: true, withInventory: false, mqttConnected: true, withHistory: false);

        var component = Render<Status>();

        component.Markup.Should().Contain("Recent Power Trends");
        component.Markup.Should().Contain("Waiting for history data");
        component.Markup.Should().Contain("No samples");
        fixture.SnapshotWorker.Dispose();
    }

    private SolarFixture RegisterFixture(bool withSnapshot, bool withInventory, bool mqttConnected, bool withHistory = true)
    {
        var solarOptions = Options.Create(new SolarAssistantOptions { Host = "solar.local", EnableMqttDiscovery = true });
        var outboxOptions = Options.Create(new OutboxOptions { ApiEndpoint = "https://example.invalid/api/v1/power/readings", ApiKey = "test" });
        var provider = new ServiceCollection().BuildServiceProvider();
        var snapshotWorker = new SolarAssistantSnapshotWorker(provider.GetRequiredService<IServiceScopeFactory>(), new EmptySolarAssistantClient(), solarOptions, NullLogger<SolarAssistantSnapshotWorker>.Instance);
        var store = new SolarAssistantMqttInventoryStore();
        if (mqttConnected)
        {
            store.MarkConnected();
            store.Apply(new SolarAssistantMqttMessage { Topic = "homeassistant/sensor/solar_pv/config", Payload = "{\"name\":\"PV Power\",\"stat_t\":\"solar_assistant/total/pv_power/state\",\"unit_of_meas\":\"W\",\"dev\":{\"name\":\"SolarAssistant\"}}", ReceivedAtUtc = DateTime.UtcNow });
        }

        var mqttWorker = new SolarAssistantMqttDiscoveryWorker(solarOptions, store, NullLogger<SolarAssistantMqttDiscoveryWorker>.Instance);
        var forwarder = new PowerApiForwarder(provider.GetRequiredService<IServiceScopeFactory>(), new EmptyHttpClientFactory(), outboxOptions, new RuntimeOutboxSettings(), NullLogger<PowerApiForwarder>.Instance);
        var health = new SolarAssistantGatewayHealthService(snapshotWorker, mqttWorker, forwarder, solarOptions);

        if (withSnapshot)
        {
            var snapshot = new PowerReadingPayload
            {
                SourceId = "solarassistant-total",
                SourceSystem = "solarassistant",
                DeviceId = "total",
                RecordedAtUtc = DateTime.UtcNow,
                PvPowerW = 1250,
                LoadPowerW = 620,
                GridPowerW = 0,
                BatteryPowerW = -180,
                SystemPowerW = 1250,
                BatteryStateOfChargePercent = 84,
                BatteryVoltageV = 53.2,
                BatteryCurrentA = -3.4,
                BatteryCapacityKwh = 14.2,
                LoadPercentage = 18,
                InverterMode = "Solar/Battery"
            };
            SetField(snapshotWorker, "_lastSnapshot", snapshot);
            SetField(snapshotWorker, "_lastMetricCount", 4);
            SetField(snapshotWorker, "_lastSnapshotAtTicks", snapshot.RecordedAtUtc.Ticks);
            if (withHistory)
            {
                SetField(snapshotWorker, "_history", new Queue<PowerSnapshotHistoryPoint>([new PowerSnapshotHistoryPoint { RecordedAtUtc = snapshot.RecordedAtUtc, PvPowerW = 1250, LoadPowerW = 620, GridPowerW = 0, BatteryPowerW = -180 }]));
            }
        }

        if (withInventory)
        {
            SetField(snapshotWorker, "_lastInventory", Inventory());
        }

        Services.AddSingleton(snapshotWorker);
        Services.AddSingleton(mqttWorker);
        Services.AddSingleton(forwarder);
        Services.AddSingleton(health);
        Services.AddSingleton<IOptions<SolarAssistantOptions>>(solarOptions);
        Services.AddSingleton<IOptions<OutboxOptions>>(outboxOptions);
        return new SolarFixture(snapshotWorker);
    }

    private static SolarAssistantMetricInventory Inventory() => new()
    {
        RecordedAtUtc = DateTime.UtcNow,
        MetricCount = 2,
        Topics =
        [
            new SolarAssistantMetricSummary { Topic = "total/pv_power", Group = "total", Name = "PV Power", Unit = "W", Classification = SolarAssistantMetricClassification.DbCandidate },
            new SolarAssistantMetricSummary { Topic = "inverter_1/firmware", Group = "inverter", Name = "Firmware", Classification = SolarAssistantMetricClassification.LocalOnly },
        ],
        ClassificationCounts = new Dictionary<string, int> { [SolarAssistantMetricClassification.DbCandidate] = 1, [SolarAssistantMetricClassification.LocalOnly] = 1 },
        GroupCounts = new Dictionary<string, int> { ["total"] = 1, ["inverter"] = 1 },
        UnitCounts = new Dictionary<string, int> { ["W"] = 1 }
    };

    private static void SetField<T>(SolarAssistantSnapshotWorker worker, string name, T value) =>
        typeof(SolarAssistantSnapshotWorker).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(worker, value);

    private sealed record SolarFixture(SolarAssistantSnapshotWorker SnapshotWorker);

    private sealed class EmptySolarAssistantClient : ISolarAssistantClient
    {
        public Task<IReadOnlyList<SolarAssistantMetric>> GetMetricsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<SolarAssistantMetric>>([]);
    }

    private sealed class EmptyHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
