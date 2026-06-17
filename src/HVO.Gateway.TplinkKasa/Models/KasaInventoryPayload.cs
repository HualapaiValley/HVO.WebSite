namespace HVO.Gateway.TplinkKasa.Models;

public sealed class KasaInventoryPayload
{
    public string SourceId { get; init; } = string.Empty;
    public string? DeviceId { get; init; }
    public DateTime RecordedAtUtc { get; init; }
    public string SourceSystem { get; init; } = "tplink-kasa";
    public string? DeviceKind { get; init; }
    public string? Model { get; init; }
    public string? Alias { get; init; }
    public string? HardwareVersion { get; init; }
    public string? SoftwareVersion { get; init; }
    public string? MacAddress { get; init; }
    public string? HardwareId { get; init; }
    public string? FirmwareId { get; init; }
    public string? OemId { get; init; }
    public IReadOnlyList<string>? Capabilities { get; init; }
}
