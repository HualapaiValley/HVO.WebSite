using HVO.Edge.Contracts;

namespace HVO.Hardware.DavisVantagePro2.Outbox;

/// <summary>
/// Payload type constants for Davis weather outbox records.
/// These delegate to the centralized <see cref="EdgePayloadTypes.Legacy"/> registry.
/// </summary>
public static class DavisOutboxPayloadTypes
{
    public const string Raw = EdgePayloadTypes.WeatherRaw;
    public const string RawVersion = "1";
    public const string Archive = EdgePayloadTypes.WeatherArchive;
    public const string ArchiveVersion = "1";
    public const string Config = EdgePayloadTypes.Legacy.WeatherConfig;
    public const string ConfigVersion = "1";
}
