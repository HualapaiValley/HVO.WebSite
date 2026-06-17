namespace HVO.Gateway.TplinkKasa.Models;

public sealed class KasaEnergyPayload
{
    public string SourceId { get; init; } = string.Empty;
    public string? DeviceId { get; init; }
    public DateTime RecordedAtUtc { get; init; }
    public string SourceSystem { get; init; } = "tplink-kasa";
    public double? LoadPowerW { get; init; }
    public double? GridVoltageV { get; init; }
}
