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

    [TestMethod]
    public void Compose_AddsJkBmsBatteryBanksAndAggregateStatus()
    {
        var now = DateTime.Parse("2026-05-27T18:45:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind);

        var snapshot = PowerSystemSnapshotComposer.Compose(
            [SolarAssistant(now, soc: 82)],
            now,
            [
                JkBmsReading(now.AddSeconds(-5), "bank-2a", alarmBitmask: 0, cellVoltagesMv: [3361, 3364, 3362]),
                JkBmsReading(now, "bank-1a", alarmBitmask: 4, cellVoltagesMv: [3350, 3353, 3355]),
            ]);

        snapshot.BatteryBanks.Should().HaveCount(2);
        snapshot.BatteryBanks![0].BankId.Should().Be("bank-1a");
        snapshot.BatteryBanks[0].Source.Should().Be(PowerMetricSource.JkBms);
        snapshot.BatteryBanks[0].VoltageV!.Value.Should().Be(53.81);
        snapshot.BatteryBanks[0].CurrentA!.Value.Should().Be(7.5);
        snapshot.BatteryBanks[0].PowerW!.Value.Should().Be(403.575);
        snapshot.BatteryBanks[0].MinCellVoltageV!.Value.Should().Be(3.35);
        snapshot.BatteryBanks[0].MaxCellVoltageV!.Value.Should().Be(3.355);
        snapshot.BatteryBanks[0].DeltaCellVoltageV!.Value.Should().Be(0.005);
        snapshot.BatteryBanks[0].AverageCellVoltageV!.Value.Should().BeApproximately(3.3526667, 0.000001);
        snapshot.BatteryBanks[0].HasAlarms!.Value.Should().BeTrue();
        snapshot.Battery!.BankCount!.Value.Should().Be(2);
        snapshot.Battery.BankCount.Source.Should().Be(PowerMetricSource.JkBms);
        snapshot.Battery.HasAlarms!.Value.Should().BeTrue();
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

    private static BmsReading JkBmsReading(
        DateTime recordedAt,
        string alias,
        long alarmBitmask,
        int[] cellVoltagesMv) => new()
        {
            Id = alias == "bank-1a" ? 10 : 11,
            DeviceId = alias == "bank-1a" ? 1 : 2,
            Device = new BmsDevice
            {
                Id = alias == "bank-1a" ? 1 : 2,
                Address = alias == "bank-1a" ? "C8:47:8C:E4:56:B0" : "C8:47:8C:EC:1B:0F",
                Alias = alias,
            },
            RecordedAt = recordedAt,
            PackVoltageMv = 53810,
            CurrentMa = 7500,
            PowerWatts = 403.575,
            SocPercent = 91,
            SohPercent = 100,
            BatteryTemp1C = 22.1,
            BatteryTemp2C = 22.4,
            PowerTubeC = 23.6,
            BalancingActive = false,
            BalancingCurrentMa = 0,
            DeltaCellVoltageMv = 5,
            AlarmBitmask = alarmBitmask,
            CellVoltages = cellVoltagesMv
                .Select((voltage, index) => new BmsCellVoltage { CellIndex = (byte)(index + 1), VoltageMv = voltage })
                .ToArray(),
        };
}
