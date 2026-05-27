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
                FlowDirection: Value(PowerFlowDirection.Discharging, PowerMetricSource.VictronSmartShunt, observedAt)));

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
        model.SnapshotState.Should().Be("Live");
    }

    [TestMethod]
    public void FromSnapshot_ReturnsWaitingModelWhenSnapshotIsMissing()
    {
        var model = PowerStatusViewModel.FromSnapshot(null);

        model.Should().Be(PowerStatusViewModel.Empty);
    }

    private static SourcedValue<T> Value<T>(T value, PowerMetricSource source, DateTime recordedAt)
        => new(value, source, recordedAt);
}
