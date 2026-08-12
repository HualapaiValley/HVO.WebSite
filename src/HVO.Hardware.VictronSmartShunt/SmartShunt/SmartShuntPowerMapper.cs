using HVO.Edge.Contracts.PowerSystem;
using HVO.Hardware.VictronSmartShunt.Configuration;

namespace HVO.Hardware.VictronSmartShunt.SmartShunt;

public static class SmartShuntPowerMapper
{
    public static PowerReadingPayload MapSummary(SmartShuntLiveSample sample, SmartShuntOptions options) => new()
    {
        SourceId = options.SourceId,
        SourceSystem = "victron-smartshunt",
        DeviceId = options.DeviceId,
        RecordedAtUtc = sample.RecordedAtUtc,
        BatteryStateOfChargePercent = sample.StateOfChargePercent,
        BatteryVoltageV = sample.VoltageV,
        // SmartShunt signs are source-native: positive charging, negative discharging.
        BatteryCurrentA = sample.CurrentA,
        BatteryPowerW = sample.PowerW,
        SystemPowerW = sample.PowerW,
    };

    public static SmartShuntDetailPayload MapDetail(SmartShuntLiveSample sample, SmartShuntOptions options) => new()
    {
        SourceId = options.SourceId,
        SourceSystem = "victron-smartshunt",
        DeviceId = options.DeviceId,
        RecordedAtUtc = sample.RecordedAtUtc,
        ConsumedAh = sample.ConsumedAh,
        RemainingMinutes = sample.RemainingMinutes,
        StarterVoltageV = sample.StarterVoltageV,
        TemperatureC = sample.TemperatureC,
    };
}
