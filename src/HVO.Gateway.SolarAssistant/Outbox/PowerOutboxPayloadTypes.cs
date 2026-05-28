namespace HVO.Gateway.SolarAssistant.Outbox;

public static class PowerOutboxPayloadTypes
{
    public const string PowerReading = "power.reading";
    public const string PowerReadingVersion = "1";
    public const string DeviceInventory = "power.device-inventory";
    public const string DeviceInventoryVersion = "1";
    public const string Configuration = "power.configuration";
    public const string ConfigurationVersion = "1";
    public const string Energy = "power.energy";
    public const string EnergyVersion = "1";
    public const string InverterDetail = "power.inverter-detail";
    public const string InverterDetailVersion = "1";
    public const string GatewayStatus = "gateway.status";
    public const string GatewayStatusVersion = "1";
}
