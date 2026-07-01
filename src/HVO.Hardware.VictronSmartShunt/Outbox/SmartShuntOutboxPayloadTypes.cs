using HVO.Edge.Contracts;

namespace HVO.Hardware.VictronSmartShunt.Outbox;

/// <summary>
/// Payload type constants for Victron SmartShunt outbox records.
/// These delegate to the centralized <see cref="EdgePayloadTypes.Legacy"/> registry.
/// </summary>
public static class SmartShuntOutboxPayloadTypes
{
    public const string Reading = EdgePayloadTypes.Legacy.SmartShuntReading;
    public const string ReadingVersion = "1";
}
