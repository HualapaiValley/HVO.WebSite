using FluentAssertions;
using HVO.Edge.Contracts.PowerSystem;
using HVO.WebSite.v9.Models;

namespace HVO.WebSite.UnitTests;

[TestClass]
public sealed class PowerStatusViewModelTests
{
    [TestMethod]
    public void FromSnapshot_FormatsLivePowerValuesAndSources()
    {
        var observedAt = new DateTime(2026, 5, 27, 19, 30, 0, DateTimeKind.Utc);
        var snapshot = new PowerSystemSnapshot(
            ObservedAtUtc: observedAt,
            Ac: new PowerSystemAcSnapshot(
                LoadPowerW: Value(474d, PowerMetricSource.SolarAssistant, observedAt),
                GridPowerW: Value(0d, PowerMetricSource.SolarAssistant, observedAt),
                GridFlowDirection: Value(PowerFlowDirection.Idle, PowerMetricSource.SolarAssistant, observedAt),
                InverterMode: new SourcedValue<string>("Solar/Battery", PowerMetricSource.SolarAssistant, observedAt)),
            Pv: new PowerSystemPvSnapshot(Value(3098d, PowerMetricSource.SolarAssistant, observedAt)),
            Battery: new PowerSystemBatterySnapshot(
                StateOfChargePercent: Value(100d, PowerMetricSource.SolarAssistant, observedAt),
                PowerW: Value(2700d, PowerMetricSource.VictronSmartShunt, observedAt),
                FlowDirection: Value(PowerFlowDirection.Discharging, PowerMetricSource.VictronSmartShunt, observedAt),
                BankCount: Value(7, PowerMetricSource.JkBms, observedAt),
                HasAlarms: Value(false, PowerMetricSource.JkBms, observedAt)),
            BatteryBanks:
            [
                new PowerSystemBatteryBankSnapshot(
                    BankId: "bank-2a",
                    RecordedAtUtc: observedAt.AddMinutes(-45),
                    Source: PowerMetricSource.JkBms,
                    StateOfChargePercent: Value(91d, PowerMetricSource.JkBms, observedAt),
                    VoltageV: Value(54.412d, PowerMetricSource.JkBms, observedAt),
                    CurrentA: Value(-3.24d, PowerMetricSource.JkBms, observedAt),
                    DeltaCellVoltageV: Value(0.004d, PowerMetricSource.JkBms, observedAt),
                    HasAlarms: Value(false, PowerMetricSource.JkBms, observedAt)),
                new PowerSystemBatteryBankSnapshot(
                    BankId: "bank-1a",
                    RecordedAtUtc: observedAt.AddMinutes(-4),
                    Source: PowerMetricSource.JkBms,
                    StateOfChargePercent: Value(90d, PowerMetricSource.JkBms, observedAt),
                    VoltageV: Value(54.037d, PowerMetricSource.JkBms, observedAt),
                    CurrentA: Value(1.2d, PowerMetricSource.JkBms, observedAt),
                    DeltaCellVoltageV: Value(0.003d, PowerMetricSource.JkBms, observedAt),
                    HasAlarms: Value(true, PowerMetricSource.JkBms, observedAt)),
            ]);

        var model = PowerStatusViewModel.FromSnapshot(snapshot);

        model.PvPower.Should().Be("3098 W");
        model.LoadPower.Should().Be("474 W");
        model.BatteryPower.Should().Be("+2700 W");
        model.BatteryFlow.Should().Be("Discharging");
        model.BatteryStateOfCharge.Should().Be("100%");
        model.BatterySource.Should().Be("SmartShunt");
        model.GridPower.Should().Be("0 W");
        model.GridFlow.Should().Be("Idle");
        model.InverterMode.Should().Be("Solar/Battery");
        model.BatteryBankCount.Should().Be("7 banks");
        model.BatteryAlarmState.Should().Be("No alarms");
        model.BatteryBanks.Should().HaveCount(2);
        model.BatteryBanks[0].BankId.Should().Be("bank-1a");
        model.BatteryBanks[0].HeadingId.Should().Be("power-bank-1");
        model.BatteryBanks[0].Seen.Should().Be("4 min ago");
        model.BatteryBanks[0].StateOfCharge.Should().Be("90%");
        model.BatteryBanks[0].Voltage.Should().Be("54.04 V");
        model.BatteryBanks[0].Current.Should().Be("+1.2 A");
        model.BatteryBanks[0].DeltaCellVoltage.Should().Be("3 mV");
        model.BatteryBanks[0].AlarmState.Should().Be("Active alarm");
        model.BatteryBanks[0].IsAlarmed.Should().BeTrue();
        model.BatteryBanks[1].BankId.Should().Be("bank-2a");
        model.BatteryBanks[1].HeadingId.Should().Be("power-bank-2");
        model.BatteryBanks[1].Seen.Should().Be("45 min ago");
        model.SnapshotState.Should().Be("Live");
    }

    [TestMethod]
    public void FromSnapshot_ReturnsWaitingModelWhenSnapshotIsMissing()
    {
        var model = PowerStatusViewModel.FromSnapshot(null);

        model.Should().Be(PowerStatusViewModel.Empty);
    }

    [TestMethod]
    public void FromSnapshot_TruncatesBankFreshnessBelowHourBoundary()
    {
        var observedAt = new DateTime(2026, 5, 27, 19, 30, 0, DateTimeKind.Utc);
        var snapshot = new PowerSystemSnapshot(
            ObservedAtUtc: observedAt,
            BatteryBanks:
            [
                new PowerSystemBatteryBankSnapshot(
                    BankId: "bank-1a",
                    RecordedAtUtc: observedAt.AddMinutes(-59).AddSeconds(-45),
                    Source: PowerMetricSource.JkBms),
                new PowerSystemBatteryBankSnapshot(
                    BankId: "bank-2a",
                    RecordedAtUtc: observedAt.AddHours(-2).AddMinutes(-30),
                    Source: PowerMetricSource.JkBms),
            ]);

        var model = PowerStatusViewModel.FromSnapshot(snapshot);

        model.BatteryBanks[0].Seen.Should().Be("59 min ago");
        model.BatteryBanks[1].Seen.Should().Be("2 hr ago");
    }

    private static SourcedValue<T> Value<T>(T value, PowerMetricSource source, DateTime recordedAt)
        => new(value, source, recordedAt);
}
