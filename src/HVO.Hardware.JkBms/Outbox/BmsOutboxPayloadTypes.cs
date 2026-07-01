using HVO.Edge.Contracts;

namespace HVO.Hardware.JkBms.Outbox;

/// <summary>
/// Payload type constants for JK BMS outbox records.
/// These delegate to the centralized <see cref="EdgePayloadTypes.Legacy"/> registry.
/// </summary>
public static class BmsOutboxPayloadTypes
{
    public const string Reading = EdgePayloadTypes.Legacy.BmsReading;
    public const string ReadingVersion = "1";
    public const string Config = EdgePayloadTypes.Legacy.BmsConfig;
    public const string ConfigVersion = "1";
    public const string DeviceInfo = EdgePayloadTypes.Legacy.BmsDeviceInfo;
    public const string DeviceInfoVersion = "1";
}
