using FluentAssertions;
using HVO.DataModels.Models.V9;
using HVO.Edge.Contracts.PowerSystem;
using HVO.WebSite.v9.Models;

namespace HVO.WebSite.UnitTests;

[TestClass]
public sealed class PowerSystemSnapshotComposerTests
{
    [TestMethod]
    public void Compose_PrefersSolarAssistantForAcPvAndSoc()
    {
        var now = DateTime.Parse("2026-05-27T18:45:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind);
        var snapshot = PowerSystemSnapshotComposer.Compose(
            [
                SolarAssistant(now.AddSeconds(-10), pvPowerW: 1300, loadPowerW: 875, gridPowerW: -25, soc: 82),
                SmartShunt(now, voltageV: 53.74, currentA: -20.72, powerW: -1113, soc: 0),
            ],
            now);

        snapshot.Pv!.PowerW!.Source.Should().Be(PowerMetricSource.SolarAssistant);
        snapshot.Ac!.LoadPowerW!.Source.Should().Be(PowerMetricSource.SolarAssistant);
        snapshot.Ac.GridFlowDirection!.Value.Should().Be(PowerFlowDirection.Export);
        snapshot.Battery!.StateOfChargePercent!.Source.Should().Be(PowerMetricSource.SolarAssistant);
        snapshot.Battery.StateOfChargePercent.Value.Should().Be(82);
    }

    [TestMethod]
    public void Compose_PrefersSmartShuntForBatteryBusTelemetry()
    {
        var now = DateTime.Parse("2026-05-27T18:45:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind);
        var snapshot = PowerSystemSnapshotComposer.Compose(
            [
                SolarAssistant(now.AddSeconds(-10), batteryPowerW: 250, voltageV: 53.2, currentA: 4.7, soc: 82),
                SmartShunt(now, voltageV: 53.74, currentA: -20.72, powerW: -1113, soc: 0),
            ],
            now);

        snapshot.Battery!.VoltageV!.Source.Should().Be(PowerMetricSource.VictronSmartShunt);
        snapshot.Battery.VoltageV.Value.Should().Be(53.74);
        snapshot.Battery.CurrentA!.Value.Should().Be(-20.72);
        snapshot.Battery.PowerW!.Value.Should().Be(-1113);
        snapshot.Battery.FlowDirection!.Value.Should().Be(PowerFlowDirection.Charging);
    }

    [TestMethod]
    public void Compose_MarksSmartShuntSocFallbackAsUntrusted()
    {
        var now = DateTime.Parse("2026-05-27T18:45:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind);
        var snapshot = PowerSystemSnapshotComposer.Compose(
            [SmartShunt(now, voltageV: 53.74, currentA: 12.3, powerW: 661, soc: 91)],
            now);

        snapshot.Battery!.StateOfChargePercent!.Source.Should().Be(PowerMetricSource.VictronSmartShunt);
        snapshot.Battery.StateOfChargePercent.Confidence.Should().Be("fallback-untrusted");
        snapshot.Battery.FlowDirection!.Value.Should().Be(PowerFlowDirection.Discharging);
    }

    private static PowerReading SolarAssistant(
        DateTime recordedAt,
        double? pvPowerW = null,
        double? loadPowerW = null,
        double? gridPowerW = null,
        double? batteryPowerW = null,
        double? voltageV = null,
        double? currentA = null,
        double? soc = null) => new()
        {
            Id = 1,
            SourceId = "solarassistant-total",
            SourceSystem = "solarassistant",
            DeviceId = "total",
            RecordedAt = recordedAt,
            PvPowerW = pvPowerW,
            LoadPowerW = loadPowerW,
            GridPowerW = gridPowerW,
            BatteryPowerW = batteryPowerW,
            BatteryVoltageV = voltageV,
            BatteryCurrentA = currentA,
            BatteryStateOfChargePercent = soc,
            GridVoltageV = 240,
            GridFrequencyHz = 60,
            OutputVoltageV = 120,
            OutputFrequencyHz = 60,
        };

    private static PowerReading SmartShunt(
        DateTime recordedAt,
        double? voltageV,
        double? currentA,
        double? powerW,
        double? soc) => new()
        {
            Id = 2,
            SourceId = "smartshunt-main",
            SourceSystem = "victron-smartshunt",
            DeviceId = "smartshunt-lifepo4",
            RecordedAt = recordedAt,
            BatteryVoltageV = voltageV,
            BatteryCurrentA = currentA,
            BatteryPowerW = powerW,
            BatteryStateOfChargePercent = soc,
        };
}
