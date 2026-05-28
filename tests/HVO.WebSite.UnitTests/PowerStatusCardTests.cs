using Bunit;
using FluentAssertions;
using HVO.Edge.Contracts.PowerSystem;
using HVO.WebSite.v9.Components.Pages;
using HVO.WebSite.v9.Models;
using HVO.WebSite.v9.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HVO.WebSite.UnitTests;

[TestClass]
public sealed class PowerStatusCardTests : Bunit.TestContext
{
    [TestMethod]
    public void PowerStatusCard_RendersSnapshotValues()
    {
        var observedAt = new DateTime(2026, 5, 27, 19, 35, 0, DateTimeKind.Utc);
        Services.AddSingleton<IPowerSystemSnapshotProvider>(new StubPowerSystemSnapshotProvider(
            new PowerSystemSnapshot(
                ObservedAtUtc: observedAt,
                Ac: new PowerSystemAcSnapshot(
                    LoadPowerW: Value(474d, PowerMetricSource.SolarAssistant, observedAt),
                    GridPowerW: Value(0d, PowerMetricSource.SolarAssistant, observedAt),
                    GridFlowDirection: Value(PowerFlowDirection.Idle, PowerMetricSource.SolarAssistant, observedAt),
                    InverterMode: new SourcedValue<string>("Solar/Battery", PowerMetricSource.SolarAssistant, observedAt)),
                Pv: new PowerSystemPvSnapshot(Value(3098d, PowerMetricSource.SolarAssistant, observedAt)),
                Battery: new PowerSystemBatterySnapshot(
                    StateOfChargePercent: Value(100d, PowerMetricSource.SolarAssistant, observedAt),
                    PowerW: Value(-900d, PowerMetricSource.VictronSmartShunt, observedAt),
                    FlowDirection: Value(PowerFlowDirection.Charging, PowerMetricSource.VictronSmartShunt, observedAt),
                    BankCount: Value(1, PowerMetricSource.JkBms, observedAt),
                    HasAlarms: Value(false, PowerMetricSource.JkBms, observedAt)),
                     BatteryBanks:
                [
                    new PowerSystemBatteryBankSnapshot(
                        BankId: "bank-1a",
                        RecordedAtUtc: observedAt.AddMinutes(-2),
                        Source: PowerMetricSource.JkBms,
                        StateOfChargePercent: Value(91d, PowerMetricSource.JkBms, observedAt),
                        VoltageV: Value(54.037d, PowerMetricSource.JkBms, observedAt),
                        CurrentA: Value(-2.4d, PowerMetricSource.JkBms, observedAt),
                        DeltaCellVoltageV: Value(0.003d, PowerMetricSource.JkBms, observedAt),
                        HasAlarms: Value(false, PowerMetricSource.JkBms, observedAt)),
                 ])));
        var energy = new PowerEnergySnapshotResponse
        {
            SourceId = "solarassistant-total",
            IsPresent = true,
            IsStale = false,
            Counters = [new PowerEnergyCounter { Key = "pv_energy", Name = "PV energy", ValueKwh = 123.45 }],
        };
        var inverterDetail = new PowerInverterDetailSnapshotResponse
        {
            SourceId = "solarassistant-total",
            DeviceId = "inverter_1",
            IsPresent = true,
            IsStale = false,
            PvStrings = [new PowerPvStringDetail { StringId = "1", PowerW = 612, VoltageV = 120.4, CurrentA = 5.1 }],
            Load = new PowerInverterLoadDetail { LoadPowerW = 474, LoadApparentPowerVa = 700 },
            Battery = new PowerInverterBatteryDetail { PowerW = -240, VoltageV = 53.1 },
            TemperatureC = 44,
            Statuses = [new PowerInverterStatusDetail { Key = "inverter_1.status_1", Value = "normal" }],
        };
        Services.AddSingleton<IPowerInventoryConfigurationProvider>(new StubPowerInventoryConfigurationProvider(
            new PowerDeviceInventorySnapshotResponse
            {
                SourceId = "solarassistant-total",
                IsPresent = true,
                IsStale = false,
                RestMetricCount = 124,
                MqttEntityCount = 48,
                Devices = [new PowerDeviceInventoryDevice { DeviceId = "eg4-6500ex", Name = "EG4 6500EX", Model = "6500EX" }],
            },
            new PowerConfigurationSnapshotResponse
            {
                SourceId = "solarassistant-total",
                IsPresent = true,
                IsStale = false,
                Settings = [new PowerConfigurationSetting { Key = "inverter_1.output_source_priority", Name = "Output source priority" }],
                CommandCapabilities = [new PowerCommandCapability { Key = "inverter_1.output_source_priority", Name = "Output source priority", CommandTopic = "solar_assistant/inverter_1/output_source_priority/set" }],
            },
            energy,
            inverterDetail));
        Services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PowerStatus:SolarAssistantGatewayUrl"] = "http://192.168.1.145:5300/",
            })
            .Build());

        var component = RenderComponent<PowerStatusCard>();

        component.Markup.Should().Contain("Live Power Snapshot");
        component.Markup.Should().Contain("3098 W");
        component.Markup.Should().Contain("-900 W");
        component.Markup.Should().Contain("Charging");
        component.Markup.Should().Contain("SmartShunt");
        component.Markup.Should().Contain("bank-1a");
        component.Find("article.power-bank").ClassList.Should().Contain("power-bank--fresh");
        component.Find("article.power-bank").GetAttribute("aria-labelledby").Should().Be("power-bank-1");
        component.Find("article.power-bank h3").TextContent.Should().Be("bank-1a");
        component.Markup.Should().Contain("54.04 V");
        component.Markup.Should().Contain("3 mV");
        component.Markup.Should().Contain("2 min ago");
        component.Markup.Should().Contain("All banks fresh");
        component.Markup.Should().Contain("No alarms");
        component.Markup.Should().Contain("SolarAssistant Inventory");
        component.Markup.Should().Contain("Current");
        component.Markup.Should().Contain("EG4 6500EX 6500EX");
        component.Markup.Should().Contain("writes disabled");
        component.Markup.Should().Contain("SolarAssistant Detail");
        component.Markup.Should().Contain("1 energy counter(s)");
        component.Markup.Should().Contain("PV energy 123.45 kWh");
        component.Markup.Should().Contain("String 1");
        component.Markup.Should().Contain("612 W");
        component.Markup.Should().Contain("44 C");
        component.Markup.Should().Contain("Open local SolarAssistant gateway diagnostics");
    }

    private static SourcedValue<T> Value<T>(T value, PowerMetricSource source, DateTime recordedAt)
        => new(value, source, recordedAt);

    private sealed class StubPowerSystemSnapshotProvider(PowerSystemSnapshot? snapshot) : IPowerSystemSnapshotProvider
    {
        public Task<PowerSystemSnapshot?> GetLatestAsync(int lookbackMinutes = 60, CancellationToken ct = default)
            => Task.FromResult(snapshot);
    }

    private sealed class StubPowerInventoryConfigurationProvider(
        PowerDeviceInventorySnapshotResponse inventory,
        PowerConfigurationSnapshotResponse configuration,
        PowerEnergySnapshotResponse? energy = null,
        PowerInverterDetailSnapshotResponse? inverterDetail = null) : IPowerInventoryConfigurationProvider
    {
        public Task<(PowerDeviceInventorySnapshotResponse Inventory, PowerConfigurationSnapshotResponse Configuration)> GetLatestAsync(
            string sourceId = "solarassistant-total",
            int staleAfterMinutes = 1440,
            CancellationToken ct = default) => Task.FromResult((inventory, configuration));

        public Task<(
            PowerDeviceInventorySnapshotResponse Inventory,
            PowerConfigurationSnapshotResponse Configuration,
            PowerEnergySnapshotResponse Energy,
            PowerInverterDetailSnapshotResponse InverterDetail)> GetLatestCentralAsync(
            string sourceId = "solarassistant-total",
            int staleAfterMinutes = 1440,
            CancellationToken ct = default) => Task.FromResult((
                inventory,
                configuration,
                energy ?? new PowerEnergySnapshotResponse { SourceId = sourceId, IsPresent = false, IsStale = true },
                inverterDetail ?? new PowerInverterDetailSnapshotResponse { SourceId = sourceId, IsPresent = false, IsStale = true }));
    }
}
