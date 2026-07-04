using HVO.Edge.Contracts;

namespace HVO.Gateway.SolarAssistant.Outbox;

/// <summary>
/// Payload type constants for SolarAssistant power outbox records.
/// These delegate to the centralized <see cref="EdgePayloadTypes.Legacy"/> registry.
/// </summary>
public static class PowerOutboxPayloadTypes
{
    public const string PowerReading = EdgePayloadTypes.Legacy.PowerReading;
    public const string PowerReadingVersion = "1";
    public const string DeviceInventory = EdgePayloadTypes.Legacy.PowerDeviceInventory;
    public const string DeviceInventoryVersion = "1";
    public const string Configuration = EdgePayloadTypes.Legacy.PowerConfiguration;
    public const string ConfigurationVersion = "1";
    public const string Energy = EdgePayloadTypes.Legacy.PowerEnergy;
    public const string EnergyVersion = "1";
    public const string InverterDetail = EdgePayloadTypes.Legacy.PowerInverterDetail;
    public const string InverterDetailVersion = "1";
    public const string GatewayStatus = EdgePayloadTypes.Legacy.GatewayStatus;
    public const string GatewayStatusVersion = "1";
}
