using Bunit;
using FluentAssertions;
using HVO.Edge.Contracts;
using HVO.Edge.Contracts.PowerSystem;
using HVO.WebSite.v9.Components.Pages;
using HVO.WebSite.v9.Models;
using HVO.WebSite.v9.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HVO.WebSite.UnitTests;

[TestClass]
public sealed class PowerStatusCardTests : BunitContext
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
                    StateOfChargePercent: Value(100d, PowerMetricSource.SolarAssistant, observedAt, "solarassistant-total", "inverter-total"),
                    VoltageV: Value(54.1d, PowerMetricSource.VictronSmartShunt, observedAt, "smartshunt-main", "smartshunt"),
                    CurrentA: Value(-16.6d, PowerMetricSource.VictronSmartShunt, observedAt, "smartshunt-main", "smartshunt"),
                    PowerW: Value(-900d, PowerMetricSource.VictronSmartShunt, observedAt, "smartshunt-main", "smartshunt"),
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
                        PowerW: Value(-129.7d, PowerMetricSource.JkBms, observedAt),
                        StateOfHealthPercent: Value(98d, PowerMetricSource.JkBms, observedAt),
                        MinCellVoltageV: Value(3.371d, PowerMetricSource.JkBms, observedAt),
                        MaxCellVoltageV: Value(3.374d, PowerMetricSource.JkBms, observedAt),
                        DeltaCellVoltageV: Value(0.003d, PowerMetricSource.JkBms, observedAt),
                        AverageCellVoltageV: Value(3.372d, PowerMetricSource.JkBms, observedAt),
                        BatteryTemperature1C: Value(24.5d, PowerMetricSource.JkBms, observedAt),
                        BalancingActive: Value(true, PowerMetricSource.JkBms, observedAt),
                        BalancingCurrentA: Value(0.12d, PowerMetricSource.JkBms, observedAt),
                        HasAlarms: Value(false, PowerMetricSource.JkBms, observedAt)),
                 ],
                Notes: ["Aggregate branch comparison may include additional DC loads."],
                BatteryObservations:
                [
                    new PowerBatteryObservation("smartshunt-main", "smartshunt", PowerMetricSource.VictronSmartShunt,
                        PowerMeasurementRole.BusNet, "battery-bus-net", observedAt.AddSeconds(-5),
                        VoltageV: 54.1, CurrentA: -16.6, PowerW: -900, StateOfChargePercent: 92),
                    new PowerBatteryObservation("solarassistant-total", "inverter-total", PowerMetricSource.SolarAssistant,
                        PowerMeasurementRole.AggregateEstimate, "solarassistant-battery-aggregate", observedAt.AddSeconds(-10),
                        VoltageV: 54.0, CurrentA: -15, PowerW: -810, StateOfChargePercent: 100,
                        Provenance: PowerObservationProvenance.SourceAggregate),
                    new PowerBatteryObservation("eg4-inverter-a", "inverter-a", PowerMetricSource.Eg46500Ex,
                        PowerMeasurementRole.InverterBranch, "inverter-battery-branch", observedAt.AddSeconds(-20),
                        VoltageV: 54.4, CurrentA: -8, PowerW: -435),
                    new PowerBatteryObservation("eg4-inverter-b", "inverter-b", PowerMetricSource.Eg46500Ex,
                        PowerMeasurementRole.InverterBranch, "inverter-battery-branch", observedAt.AddMinutes(-4),
                        VoltageV: 54.3, CurrentA: -7, PowerW: -380),
                    new PowerBatteryObservation("derived-6500ex-branch-sum", "all-inverters", PowerMetricSource.Derived,
                        PowerMeasurementRole.DerivedAggregate, "6500ex-branch-sum", observedAt.AddSeconds(-20),
                        CurrentA: -15, PowerW: -815, Provenance: PowerObservationProvenance.Derived,
                        Inputs:
                        [
                            new PowerObservationInput("eg4-inverter-a", observedAt.AddSeconds(-20), "inverter-a"),
                            new PowerObservationInput("eg4-inverter-b", observedAt.AddMinutes(-4), "inverter-b"),
                        ]),
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
        var gatewayStatus = new GatewayStatusSnapshotResponse
        {
            SourceId = "solarassistant-total",
            SourceSystem = "solarassistant",
            IsPresent = true,
            IsStale = false,
            RecordedAtUtc = observedAt,
            Identity = new GatewayIdentity("solarassistant", "SolarAssistant Gateway", GatewayDomain.Power, "solarassistant-total", "total"),
            Health = new GatewayHealthSnapshot(
                GatewayHealthState.Warning,
                observedAt,
                [new GatewayHealthAlert("outbox-failed", GatewayAlertSeverity.Warning, "Historical failed outbox rows are present.")],
                GatewaySampleState.Live,
                OutboxState: "warning",
                ApiSyncState: "healthy"),
            Rest = new GatewayRuntimeSignal(GatewaySampleState.Live, observedAt, Detail: "124 REST metric(s)"),
            Mqtt = new GatewayRuntimeSignal(GatewaySampleState.Live, observedAt, Detail: "48 entit(ies), 42 state topic(s)"),
            Outbox = new GatewayOutboxStatus(PendingCount: 0, FailedCount: 71, LastSentAtUtc: observedAt, LastBatchCount: 1),
            RestMetricCount = 124,
            MqttEntityCount = 48,
            MqttCommandTopicCount = 14,
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
            inverterDetail,
            gatewayStatus));
        Services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PowerStatus:SolarAssistantGatewayUrl"] = "http://192.168.1.145:5300/",
            })
            .Build());

        var component = Render<PowerStatusCard>();

        component.Markup.Should().Contain("Live Power Snapshot");
        component.Markup.Should().Contain("3098 W");
        component.Markup.Should().Contain("-900 W");
        component.Markup.Should().Contain("Charging");
        component.Markup.Should().Contain("SmartShunt");
        component.Markup.Should().Contain("Battery power source");
        component.Markup.Should().Contain("Battery SOC source");
        component.Markup.Should().Contain("Battery Source Comparison");
        component.Find("[aria-label='Scrollable battery source comparison']").GetAttribute("tabindex").Should().Be("0");
        component.Find("table[aria-label='Battery source observations']").ClassList.Should().Contain("hvo-table");
        component.FindAll("tr[data-role='Inverter branch']").Should().HaveCount(2);
        component.Find("tr[data-source-id='eg4-inverter-a']").TextContent.Should().Contain("Inverter branch");
        component.Find("tr[data-source-id='eg4-inverter-a']").TextContent.Should().NotContain("Battery bus net");
        component.Find("tr[data-source-id='smartshunt-main']").TextContent.Should().Contain("Preferred bus: voltage, current, power");
        component.Find("tr[data-source-id='solarassistant-total']").TextContent.Should().Contain("Preferred SOC");
        component.Find("tr[data-source-id='eg4-inverter-b'] .hvo-chip-danger").TextContent.Should().Contain("stale");
        component.Find("tr[data-source-id='derived-6500ex-branch-sum']").TextContent.Should().Contain("Inputs: eg4-inverter-a/inverter-a, eg4-inverter-b/inverter-b");
        component.Markup.Should().Contain("Aggregate branch comparison may include additional DC loads.");
        component.Markup.Should().Contain("bank-1a");
        component.Find("article.power-bank").ClassList.Should().Contain("power-bank--fresh");
        component.Find("article.power-bank").GetAttribute("aria-labelledby").Should().Be("power-bank-1");
        component.Find("article.power-bank h3").TextContent.Should().Be("bank-1a");
        component.Markup.Should().Contain("54.04 V");
        component.Markup.Should().Contain("3 mV");
        component.Markup.Should().Contain("Health");
        component.Markup.Should().Contain("98%");
        component.Markup.Should().Contain("Balancing");
        component.Markup.Should().Contain("Active (+0.1 A)");
        component.Markup.Should().Contain("2 min ago");
        component.Markup.Should().Contain("All banks fresh");
        component.Markup.Should().Contain("No alarms");
        component.Markup.Should().Contain("SolarAssistant Inventory");
        component.Markup.Should().Contain("Current");
        component.Markup.Should().Contain("EG4 6500EX 6500EX");
        component.Markup.Should().Contain("writes disabled");
        component.Markup.Should().Contain("SolarAssistant Gateway");
        component.Markup.Should().Contain("Warning with 1 alert(s)");
        component.Markup.Should().Contain("Live; 124 REST metric(s)");
        component.Markup.Should().Contain("0 pending, 71 failed, last batch 1");
        component.Markup.Should().Contain("outbox-failed");
        component.Markup.Should().Contain("SolarAssistant Detail");
        component.Markup.Should().Contain("1 energy counter(s)");
        component.Markup.Should().Contain("PV energy 123.45 kWh");
        component.Markup.Should().Contain("String 1");
        component.Markup.Should().Contain("612 W");
        component.Markup.Should().Contain("44 C");
        component.Markup.Should().Contain("Open local SolarAssistant gateway diagnostics");
    }

    [TestMethod]
    public void PowerStatusCard_PreservesHeadlineWhenSourceObservationsAreAbsent()
    {
        var observedAt = new DateTime(2026, 5, 27, 19, 35, 0, DateTimeKind.Utc);
        Services.AddSingleton<IPowerSystemSnapshotProvider>(new StubPowerSystemSnapshotProvider(
            new PowerSystemSnapshot(
                ObservedAtUtc: observedAt,
                Battery: new PowerSystemBatterySnapshot(
                    PowerW: Value(250d, PowerMetricSource.SolarAssistant, observedAt)))));
        Services.AddSingleton<IPowerInventoryConfigurationProvider>(new StubPowerInventoryConfigurationProvider(
            new PowerDeviceInventorySnapshotResponse(),
            new PowerConfigurationSnapshotResponse()));
        Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());

        var component = Render<PowerStatusCard>();

        component.Markup.Should().Contain("+250 W");
        component.Markup.Should().Contain("No source-level battery observations are available.");
        component.FindAll("table[aria-label='Battery source observations']").Should().BeEmpty();
    }

    private static SourcedValue<T> Value<T>(
        T value,
        PowerMetricSource source,
        DateTime recordedAt,
        string? sourceId = null,
        string? deviceId = null)
        => new(value, source, recordedAt, sourceId, deviceId);

    private sealed class StubPowerSystemSnapshotProvider(PowerSystemSnapshot? snapshot) : IPowerSystemSnapshotProvider
    {
        public Task<PowerSystemSnapshot?> GetLatestAsync(int lookbackMinutes = 60, CancellationToken ct = default)
            => Task.FromResult(snapshot);
    }

    private sealed class StubPowerInventoryConfigurationProvider(
        PowerDeviceInventorySnapshotResponse inventory,
        PowerConfigurationSnapshotResponse configuration,
        PowerEnergySnapshotResponse? energy = null,
        PowerInverterDetailSnapshotResponse? inverterDetail = null,
        GatewayStatusSnapshotResponse? gatewayStatus = null) : IPowerInventoryConfigurationProvider
    {
        public Task<(PowerDeviceInventorySnapshotResponse Inventory, PowerConfigurationSnapshotResponse Configuration)> GetLatestAsync(
            string sourceId = "solarassistant-total",
            int staleAfterMinutes = 1440,
            CancellationToken ct = default) => Task.FromResult((inventory, configuration));

        public Task<(
            PowerDeviceInventorySnapshotResponse Inventory,
            PowerConfigurationSnapshotResponse Configuration,
            PowerEnergySnapshotResponse Energy,
            PowerInverterDetailSnapshotResponse InverterDetail,
            GatewayStatusSnapshotResponse GatewayStatus)> GetLatestCentralAsync(
            string sourceId = "solarassistant-total",
            int staleAfterMinutes = 1440,
            CancellationToken ct = default) => Task.FromResult((
                inventory,
                configuration,
                energy ?? new PowerEnergySnapshotResponse { SourceId = sourceId, IsPresent = false, IsStale = true },
                inverterDetail ?? new PowerInverterDetailSnapshotResponse { SourceId = sourceId, IsPresent = false, IsStale = true },
                gatewayStatus ?? new GatewayStatusSnapshotResponse { SourceId = sourceId, IsPresent = false, IsStale = true }));
    }
}
