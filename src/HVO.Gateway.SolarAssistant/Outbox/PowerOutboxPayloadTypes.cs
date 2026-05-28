namespace HVO.Gateway.SolarAssistant.Outbox;

public static class PowerOutboxPayloadTypes
{
    public const string PowerReading = "power.reading";
    public const string PowerReadingVersion = "1";
    public const string DeviceInventory = "power.device-inventory";
    public const string DeviceInventoryVersion = "1";
    public const string Configuration = "power.configuration";
    public const string ConfigurationVersion = "1";
}
