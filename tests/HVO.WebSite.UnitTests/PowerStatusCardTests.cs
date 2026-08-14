using Bunit;
using FluentAssertions;
using HVO.Edge.Contracts.PowerSystem;
using HVO.WebSite.v9.Components.Pages;
using HVO.WebSite.v9.Models;
using HVO.WebSite.v9.Services;
using HVO.WebSite.Themes.Components.Charts;
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
                    LoadPowerW: Value(474d, PowerMetricSource.Eg46500Ex, observedAt),
                    InverterMode: new SourcedValue<string>("Battery", PowerMetricSource.Eg46500Ex, observedAt)),
                Pv: new PowerSystemPvSnapshot(
                    Value(3098d, PowerMetricSource.Derived, observedAt),
                    Trackers:
                    [
                        new PowerSystemPvTrackerSnapshot("eg4-6500ex-a/mppt-1", "Inverter MPPT 1", "eg4-6500ex-a", "inverter-a", observedAt, PowerMetricSource.Eg46500Ex, 332, 4.4, 1475, PowerObservationProvenance.Direct, "direct registers"),
                        new PowerSystemPvTrackerSnapshot("eg4-6500ex-a/mppt-2", "Inverter MPPT 2", "eg4-6500ex-a", "inverter-a", observedAt, PowerMetricSource.Eg46500Ex, 381, 3.8, 1462, PowerObservationProvenance.Direct, "direct registers"),
                        new PowerSystemPvTrackerSnapshot("eg4-mppt100-48hv-a/mppt-1", "External MPPT", "eg4-mppt100-48hv-a", "controller-a", observedAt, PowerMetricSource.Eg4Mppt10048Hv, 400, 0.4, 161, PowerObservationProvenance.Direct, "direct registers"),
                    ],
                    ExpectedTrackerCount: 3,
                    ReportedTrackerCount: 3),
                Battery: new PowerSystemBatterySnapshot(
                    StateOfChargePercent: Value(100d, PowerMetricSource.VictronSmartShunt, observedAt, "smartshunt-main", "smartshunt"),
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
                        VoltageV: 54.1, CurrentA: -16.6, PowerW: -900, StateOfChargePercent: 100),
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
        var eg4Inverter = new PowerInverterDetailSnapshotResponse
        {
            SourceId = "eg4-6500ex-a", DeviceId = "inverter-a", IsPresent = true, IsStale = false, RecordedAtUtc = observedAt,
            PvStrings =
            [
                new PowerPvStringDetail { StringId = "mppt-1", PowerW = 1378, VoltageV = 333, CurrentA = 4.1 },
                new PowerPvStringDetail { StringId = "mppt-2", PowerW = 1118, VoltageV = 372.6, CurrentA = 3 },
            ],
            Ac = new PowerInverterAcDetail { InputVoltageV = 0, InputFrequencyHz = 0, OutputVoltageV = 120.1, OutputFrequencyHz = 59.9 },
            Load = new PowerInverterLoadDetail { LoadPowerW = 1180, LoadApparentPowerVa = 1270 },
            Battery = new PowerInverterBatteryDetail { VoltageV = 53.8, CurrentA = -26, PowerW = -1398.8 },
            Operating = new PowerInverterOperatingDetail { Mode = "B", FaultCode = "00", LoadPercentage = 19 },
            Temperatures = [new PowerInverterTemperatureDetail { TemperatureId = "inverter", Name = "Inverter", TemperatureC = 60 }],
        };
        var eg4Controller = new PowerMpptDetailSnapshotResponse
        {
            SourceId = "eg4-mppt100-48hv-a", DeviceId = "controller-a", IsPresent = true, IsStale = false, RecordedAtUtc = observedAt,
            Trackers = [new PowerMpptTrackerDetail { TrackerId = "mppt-1", Name = "External MPPT", PowerW = 1056, VoltageV = 377.2, CurrentA = 2.8 }],
            BatteryOutput = new PowerMpptBatteryOutputDetail { VoltageV = 54.3, CurrentA = -9.1, PowerW = -494.1 },
            Temperatures = [new PowerMpptTemperatureDetail { TemperatureId = "controller", Name = "Controller", TemperatureC = 45 }],
        };
        var history = new PowerTelemetryHistoryResponse(
            [
                new PowerMpptDetailSnapshotResponse { SourceId = "eg4-6500ex-a", RecordedAtUtc = observedAt.AddMinutes(-5), Trackers = [new PowerMpptTrackerDetail { TrackerId = "mppt-1", PowerW = 1400 }, new PowerMpptTrackerDetail { TrackerId = "mppt-2", PowerW = 1300 }] },
                new PowerMpptDetailSnapshotResponse { SourceId = "eg4-mppt100-48hv-a", RecordedAtUtc = observedAt.AddMinutes(-5), Trackers = [new PowerMpptTrackerDetail { TrackerId = "mppt-1", PowerW = 1000 }] },
            ],
            [
                new PowerBatteryHistoryPoint(observedAt.AddMinutes(-5), "eg4-6500ex-a", "inverter-a", -1300),
                new PowerBatteryHistoryPoint(observedAt.AddMinutes(-5), "eg4-mppt100-48hv-a", "controller-a", -490),
            ]);
        Services.AddSingleton<IPowerInventoryConfigurationProvider>(new StubPowerInventoryConfigurationProvider(
            new PowerDeviceInventorySnapshotResponse(),
            new PowerConfigurationSnapshotResponse(),
            eg4InverterDetail: eg4Inverter,
            eg4MpptDetail: eg4Controller,
            history: history));
        Services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PowerComposition:ExpectedPvTrackerIds:0"] = "eg4-6500ex-a/mppt-1",
                ["PowerComposition:ExpectedPvTrackerIds:1"] = "eg4-6500ex-a/mppt-2",
                ["PowerComposition:ExpectedPvTrackerIds:2"] = "eg4-mppt100-48hv-a/mppt-1",
            })
            .Build());

        var component = Render<PowerStatusCard>();

        component.Markup.Should().Contain("Live Power Snapshot");
        component.Markup.Should().Contain("3098 W");
        component.Markup.Should().Contain("+900 W");
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
        component.Find("tr[data-source-id='smartshunt-main']").TextContent.Should().Contain("Preferred SOC");
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
        component.Markup.Should().NotContain("SolarAssistant Inventory");
        component.Markup.Should().NotContain("SolarAssistant Gateway");
        component.Markup.Should().NotContain("SolarAssistant Detail");
        component.Markup.Should().NotContain("Open local SolarAssistant gateway diagnostics");
        component.Markup.Should().Contain("PV Inputs").And.Contain("3 of 3 inputs").And.Contain("Canonical site PV inputs");
        component.FindAll("[aria-label='Canonical site PV inputs'] article").Should().HaveCount(3);
        component.Markup.Should().Contain("EG4 Equipment Detail").And.Contain("2496 W").And.Contain("120.1 V / 59.9 Hz");
        component.Markup.Should().Contain("+26.0 A").And.Contain("+1399 W").And.Contain("+9.1 A").And.Contain("+494 W");
        component.Find("#site-pv-history-chart").Should().NotBeNull();
        component.Find("#site-eg4-battery-history-chart").Should().NotBeNull();
    }

    [TestMethod]
    public void PowerStatusCard_PreservesHeadlineWhenSourceObservationsAreAbsent()
    {
        var observedAt = new DateTime(2026, 5, 27, 19, 35, 0, DateTimeKind.Utc);
        Services.AddSingleton<IPowerSystemSnapshotProvider>(new StubPowerSystemSnapshotProvider(
            new PowerSystemSnapshot(
                ObservedAtUtc: observedAt,
                Battery: new PowerSystemBatterySnapshot(
                    PowerW: Value(250d, PowerMetricSource.VictronSmartShunt, observedAt)))));
        Services.AddSingleton<IPowerInventoryConfigurationProvider>(new StubPowerInventoryConfigurationProvider(
            new PowerDeviceInventorySnapshotResponse(),
            new PowerConfigurationSnapshotResponse()));
        Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());

        var component = Render<PowerStatusCard>();

        component.Markup.Should().Contain("-250 W");
        component.Markup.Should().Contain("No source-level battery observations are available.");
        component.FindAll("table[aria-label='Battery source observations']").Should().BeEmpty();
    }

    [TestMethod]
    public void PowerStatusCard_DoesNotSubtotalPvSamplesOutsideDerivationSkew()
    {
        var observedAt = new DateTime(2026, 8, 10, 18, 0, 0, DateTimeKind.Utc);
        var history = new PowerTelemetryHistoryResponse(
        [
            new PowerMpptDetailSnapshotResponse { SourceId = "source-a", RecordedAtUtc = observedAt, Trackers = [new PowerMpptTrackerDetail { TrackerId = "one", PowerW = 100 }] },
            new PowerMpptDetailSnapshotResponse { SourceId = "source-b", RecordedAtUtc = observedAt.AddSeconds(20), Trackers = [new PowerMpptTrackerDetail { TrackerId = "two", PowerW = 200 }] },
            new PowerMpptDetailSnapshotResponse { SourceId = "source-c", RecordedAtUtc = observedAt.AddSeconds(40), Trackers = [new PowerMpptTrackerDetail { TrackerId = "three", PowerW = 300 }] },
        ], []);
        Services.AddSingleton<IPowerSystemSnapshotProvider>(new StubPowerSystemSnapshotProvider(null));
        Services.AddSingleton<IPowerInventoryConfigurationProvider>(new StubPowerInventoryConfigurationProvider(
            new PowerDeviceInventorySnapshotResponse(),
            new PowerConfigurationSnapshotResponse(),
            history: history));
        Services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PowerComposition:MaxDerivationSkewSeconds"] = "30",
                ["PowerComposition:ExpectedPvTrackerIds:0"] = "source-a/one",
                ["PowerComposition:ExpectedPvTrackerIds:1"] = "source-b/two",
                ["PowerComposition:ExpectedPvTrackerIds:2"] = "source-c/three",
            })
            .Build());

        var component = Render<PowerStatusCard>();

        var chart = component.FindComponent<HvoChart>();
        chart.Instance.Datasets.Single(dataset => dataset.Label == "5-minute PV subtotal").Data.Should().ContainSingle().Which.Should().BeNull();
        chart.Instance.Datasets.Where(dataset => dataset.Label != "5-minute PV subtotal")
            .Should().OnlyContain(dataset => dataset.Data.Single().HasValue);
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
        PowerInverterDetailSnapshotResponse? eg4InverterDetail = null,
        PowerMpptDetailSnapshotResponse? eg4MpptDetail = null,
        PowerTelemetryHistoryResponse? history = null) : IPowerInventoryConfigurationProvider
    {
        public Task<(PowerDeviceInventorySnapshotResponse Inventory, PowerConfigurationSnapshotResponse Configuration)> GetLatestAsync(
            string sourceId = "solarassistant-total",
            int staleAfterMinutes = 1440,
            CancellationToken ct = default) => Task.FromResult((inventory, configuration));

        public Task<PowerInverterDetailSnapshotResponse> GetLatestInverterDetailAsync(
            string sourceId, int staleAfterMinutes = 5, CancellationToken ct = default) =>
            Task.FromResult(eg4InverterDetail ?? new PowerInverterDetailSnapshotResponse { SourceId = sourceId, IsPresent = false, IsStale = true });

        public Task<PowerMpptDetailSnapshotResponse> GetLatestMpptDetailAsync(
            string sourceId, int staleAfterMinutes = 5, CancellationToken ct = default) =>
            Task.FromResult(eg4MpptDetail ?? new PowerMpptDetailSnapshotResponse { SourceId = sourceId, IsPresent = false, IsStale = true });

        public Task<PowerTelemetryHistoryResponse> GetRecentTelemetryAsync(
            IReadOnlyCollection<string> mpptSourceIds,
            IReadOnlyCollection<string> batterySourceIds,
            DateTime sinceUtc,
            CancellationToken ct = default) => Task.FromResult(history ?? PowerTelemetryHistoryResponse.Empty);
    }
}
