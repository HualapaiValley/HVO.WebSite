namespace HVO.Hardware.JkBms.Bms;

/// <summary>
/// Device firmware/hardware info snapshot included in an ingress record when the
/// device info has changed since the last successful forward. Null in steady state.
/// </summary>
public sealed class BmsDeviceInfoPayload
{
    public string? Manufacturer { get; init; }
    public string? Hardware { get; init; }
    public string? Firmware { get; init; }
    public string? SerialNumber { get; init; }
    public string? DeviceName { get; init; }
    public string? ManufacturingDate { get; init; }
    public string? UserData { get; init; }
}
