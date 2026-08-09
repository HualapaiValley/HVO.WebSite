using FluentAssertions;
using HVO.Edge.Contracts.PowerSystem;
using HVO.Hardware.Eg4.Telemetry;

namespace HVO.Hardware.Eg4.Tests.Telemetry;

[TestClass]
public sealed class Eg4PowerReadingMapperTests
{
    [TestMethod]
    public void Map_PreservesCanonicalBatteryFieldsAndLeavesPvAcAndSystemFieldsEmpty()
    {
        var observedAt = new DateTime(2026, 8, 9, 19, 0, 0, DateTimeKind.Utc);
        var observation = new PowerBatteryObservation(
            "eg4-6500ex-a", "6500ex-a", PowerMetricSource.Eg46500Ex,
            PowerMeasurementRole.InverterBranch, "inverter-battery-branch", observedAt,
            54.4, -68, -3699.2, 100, PowerObservationProvenance.Derived);

        var payload = Eg4PowerReadingMapper.Map(observation);

        payload.SourceId.Should().Be("eg4-6500ex-a");
        payload.SourceSystem.Should().Be("eg4-6500ex");
        payload.DeviceId.Should().Be("6500ex-a");
        payload.RecordedAtUtc.Should().Be(observedAt);
        payload.BatteryVoltageV.Should().Be(54.4);
        payload.BatteryCurrentA.Should().Be(-68);
        payload.BatteryPowerW.Should().Be(-3699.2);
        payload.BatteryStateOfChargePercent.Should().Be(100);
        payload.PvPowerW.Should().BeNull();
        payload.LoadPowerW.Should().BeNull();
        payload.GridPowerW.Should().BeNull();
        payload.SystemPowerW.Should().BeNull();
        payload.GridVoltageV.Should().BeNull();
        payload.OutputVoltageV.Should().BeNull();
        payload.InverterMode.Should().BeNull();
        payload.OutputSourcePriority.Should().BeNull();
        payload.ChargerSourcePriority.Should().BeNull();
    }

    [TestMethod]
    public void Map_RejectsUnvalidatedSourceOrRole()
    {
        var observation = new PowerBatteryObservation(
            "source", "device", PowerMetricSource.Eg4Mppt10048Hv,
            PowerMeasurementRole.ChargeControllerBranch, "branch", DateTime.UtcNow);

        FluentActions.Invoking(() => Eg4PowerReadingMapper.Map(observation))
            .Should().Throw<ArgumentException>();
    }
}
