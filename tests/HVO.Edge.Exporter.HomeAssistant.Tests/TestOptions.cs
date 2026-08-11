namespace HVO.Edge.Exporter.HomeAssistant.Tests;

internal static class TestOptions
{
    public static HomeAssistantExporterOptions Create() => new()
    {
        Enabled = true,
        Endpoint = "ws://home-assistant.test/api/websocket",
        CentralIngestEndpoint = "https://ingest.test/",
        Mappings = [
            new()
            {
                Id = "kasa",
                Contract = HomeAssistantExportContract.PowerReading,
                SourceId = "kasa:plug-1",
                DeviceId = "plug-1",
                ExpectedPlatform = "tplink",
                Entities = [
                    new() { EntityId = "sensor.kasa_power", Metric = HomeAssistantMetric.LoadPowerW },
                    new() { EntityId = "sensor.kasa_voltage", Metric = HomeAssistantMetric.GridVoltageV, Required = false }
                ]
            },
            new()
            {
                Id = "govee",
                Contract = HomeAssistantExportContract.WeatherRaw,
                SourceId = "govee:sensor-1",
                DeviceId = "sensor-1",
                ExpectedPlatform = "govee_ble",
                Entities = [
                    new() { EntityId = "sensor.govee_temperature", Metric = HomeAssistantMetric.Temperature },
                    new() { EntityId = "sensor.govee_humidity", Metric = HomeAssistantMetric.HumidityPercent }
                ]
            }
        ]
    };

    public static HomeAssistantExporterOptions PowerOnly()
    {
        var options = Create();
        options.Mappings = [options.Mappings[0]];
        options.Mappings[0].Entities = [options.Mappings[0].Entities[0]];
        return options;
    }
}
