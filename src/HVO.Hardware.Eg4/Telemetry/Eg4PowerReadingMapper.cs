using HVO.Edge.Contracts.PowerSystem;

namespace HVO.Hardware.Eg4.Telemetry;

public static class Eg4PowerReadingMapper
{
    public static PowerReadingPayload Map(PowerBatteryObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        var sourceSystem = observation.Source switch
        {
            PowerMetricSource.Eg46500Ex when observation.Role == PowerMeasurementRole.InverterBranch => "eg4-6500ex",
            PowerMetricSource.Eg4Mppt10048Hv when observation.Role == PowerMeasurementRole.ChargeControllerBranch => "eg4-mppt100-48hv",
            _ => throw new ArgumentException("Only validated EG4 battery observations can be mapped.", nameof(observation)),
        };

        return new PowerReadingPayload
        {
            SourceId = observation.SourceId,
            SourceSystem = sourceSystem,
            DeviceId = observation.DeviceId,
            RecordedAtUtc = observation.ObservedAtUtc,
            BatteryVoltageV = observation.VoltageV,
            BatteryCurrentA = observation.CurrentA,
            BatteryPowerW = observation.PowerW,
            BatteryStateOfChargePercent = observation.StateOfChargePercent,
        };
    }
}
