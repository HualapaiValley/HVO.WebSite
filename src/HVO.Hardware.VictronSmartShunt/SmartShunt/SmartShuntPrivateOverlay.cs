namespace HVO.Hardware.VictronSmartShunt.SmartShunt;

public sealed class SmartShuntPrivateOverlay
{
    public DateTime? RecordedAtUtc { get; init; }
    public double? StateOfChargePercent { get; init; }
    public double? RemainingMinutes { get; init; }
    public double? DeepestDischargeAh { get; init; }
    public double? LastDischargeAh { get; init; }
    public double? AverageDischargeAh { get; init; }
    public uint? TotalChargeCycles { get; init; }
    public uint? FullDischarges { get; init; }
    public double? CumulativeAhDrawn { get; init; }
    public double? MinBatteryVoltageV { get; init; }
    public double? MaxBatteryVoltageV { get; init; }
    public int? TimeSinceLastFullSeconds { get; init; }
    public uint? Synchronizations { get; init; }
    public uint? LowVoltageAlarms { get; init; }
    public uint? HighVoltageAlarms { get; init; }
    public double? MinStarterVoltageV { get; init; }
    public double? MaxStarterVoltageV { get; init; }
    public double? DischargedEnergyKwh { get; init; }
    public double? ChargedEnergyKwh { get; init; }
    public double? AlarmLowVoltageSetV { get; init; }
    public double? AlarmLowVoltageClearV { get; init; }
    public double? AlarmHighVoltageSetV { get; init; }
    public double? AlarmHighVoltageClearV { get; init; }
    public double? AlarmLowStarterSetV { get; init; }
    public double? AlarmLowStarterClearV { get; init; }
    public double? AlarmHighStarterSetV { get; init; }
    public double? AlarmHighStarterClearV { get; init; }
    public double? AlarmLowSocSetPercent { get; init; }
    public double? AlarmLowSocClearPercent { get; init; }
    public uint? StreamingCounter { get; init; }
    public double? ChargeStatusCoarsePercent { get; init; }
    public double? CurrentCoarseA { get; init; }
}
