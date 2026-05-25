namespace HVO.Hardware.VictronSmartShunt.SmartShunt;

public sealed class SmartShuntHistoryPoint
{
    public DateTime RecordedAtUtc { get; init; }
    public double? StateOfChargePercent { get; init; }
    public double? VoltageV { get; init; }
    public double? CurrentA { get; init; }
    public double? PowerW { get; init; }
    public double? ConsumedAh { get; init; }
}
