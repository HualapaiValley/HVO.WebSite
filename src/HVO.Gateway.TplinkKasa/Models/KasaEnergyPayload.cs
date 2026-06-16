namespace HVO.Gateway.TplinkKasa.Models;

public sealed class KasaEnergyPayload
{
    public string SourceId { get; init; } = string.Empty;
    public string? DeviceId { get; init; }
    public DateTime RecordedAtUtc { get; init; }
    public string SourceSystem { get; init; } = "tplink-kasa";
    public string? Model { get; init; }
    public string? Alias { get; init; }
    public double? PowerW { get; init; }
    public double? VoltageV { get; init; }
    public double? CurrentA { get; init; }
    public double? EnergyKWh { get; init; }
}
