using HVO.Edge.Contracts.PowerSystem;

namespace HVO.Hardware.Eg4.Telemetry;

public static class Eg4PowerReadingMapper
{
    public static PowerReadingPayload Map(PowerBatteryObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        if (observation.Source != PowerMetricSource.Eg46500Ex || observation.Role != PowerMeasurementRole.InverterBranch)
            throw new ArgumentException("Only validated 6500EX inverter-branch observations can be mapped.", nameof(observation));

        return new PowerReadingPayload
        {
            SourceId = observation.SourceId,
            SourceSystem = "eg4-6500ex",
            DeviceId = observation.DeviceId,
            RecordedAtUtc = observation.ObservedAtUtc,
            BatteryVoltageV = observation.VoltageV,
            BatteryCurrentA = observation.CurrentA,
            BatteryPowerW = observation.PowerW,
            BatteryStateOfChargePercent = observation.StateOfChargePercent,
        };
    }
}
