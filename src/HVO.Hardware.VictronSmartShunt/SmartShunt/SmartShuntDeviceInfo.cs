namespace HVO.Hardware.VictronSmartShunt.SmartShunt;

public sealed class SmartShuntDeviceInfo
{
    public string? SerialNumber { get; init; }
    public string? FirmwareVersion { get; init; }
    public string? ProductId { get; init; }
    public string? ProductFamilyRaw { get; init; }
    public string? ProductMetadata { get; init; }
    public string? ProductMetadataRaw { get; init; }
    public SmartShuntPrivateOverlay? Overlay { get; init; }
    public DateTime? RecordedAtUtc { get; init; }
}
