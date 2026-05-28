namespace HVO.Hardware.VictronSmartShunt.SmartShunt;

public sealed class SmartShuntLiveSample
{
    public DateTime RecordedAtUtc { get; init; }
    public double? StateOfChargePercent { get; init; }
    public double? VoltageV { get; init; }
    public double? CurrentA { get; init; }
    public double? PowerW { get; init; }
    public double? ConsumedAh { get; init; }
    public double? StarterVoltageV { get; init; }
    public double? TemperatureC { get; init; }
    public double? RemainingMinutes { get; init; }
    public bool PublicSessionActive { get; init; }
    public bool PrivateEnrichmentActive { get; init; }
    public string DataPath { get; init; } = "public";
}
