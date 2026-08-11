using System.Text.Json;
using HVO.Edge.HomeAssistant.Mqtt;
using HVO.Edge.Hosting;
using HVO.Hardware.JkBms.Bms;
using HVO.Hardware.JkBms.Configuration;
using Microsoft.Extensions.Options;

namespace HVO.Hardware.JkBms.HomeAssistant;

public sealed class JkBmsHomeAssistantProjection
{
    private const string Measurement = "measurement";
    private readonly IHomeAssistantMqttProjection projection;
    private readonly EdgeRuntimeIdentity identity;
    private readonly string siteId;

    public JkBmsHomeAssistantProjection(
        IHomeAssistantMqttProjection projection,
        EdgeRuntimeIdentity identity,
        IOptions<JkBmsOptions> options)
    {
        this.projection = projection;
        this.identity = identity;
        siteId = identity.SiteId
            ?? throw new InvalidOperationException("Edge:Runtime:SiteId is required for JK BMS Home Assistant identity.");
        foreach (var device in (options.Value.Devices ?? []).Where(static device => device.Enabled))
            projection.UpsertDevice(CreateDefinition(device));
    }

    public bool Publish(BmsDeviceConfig device, BmsDeviceReading reading)
    {
        var voltage = reading.TotalVoltageMv / 1000d;
        var current = reading.CurrentMa / 1000d;
        var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["battery_voltage"] = JsonSerializer.SerializeToElement(voltage),
            // JK protocol current is positive while charging and negative while discharging.
            ["battery_net_current"] = JsonSerializer.SerializeToElement(current),
            ["battery_net_power"] = JsonSerializer.SerializeToElement(voltage * current),
            ["state_of_charge"] = JsonSerializer.SerializeToElement(reading.StateOfChargePercent),
            ["battery_temperature_1"] = JsonSerializer.SerializeToElement(reading.BatteryTemperature1C),
            ["battery_temperature_2"] = JsonSerializer.SerializeToElement(reading.BatteryTemperature2C),
            ["cell_delta"] = JsonSerializer.SerializeToElement(reading.DeltaCellVoltageMv),
            ["balancing"] = JsonSerializer.SerializeToElement(reading.BalancingActive),
            ["alarm"] = JsonSerializer.SerializeToElement(reading.HasAlarms),
            ["cycle_count"] = JsonSerializer.SerializeToElement(reading.CycleCount),
        };
        return projection.PublishCurrentState(new(
            Key(device),
            new DateTimeOffset(reading.RecordedAtUtc),
            values,
            available: true));
    }

    public bool PublishUnavailable(BmsDeviceConfig device, DateTime observedAtUtc) =>
        projection.PublishCurrentState(new(
            Key(device),
            new DateTimeOffset(observedAtUtc),
            Array.Empty<KeyValuePair<string, JsonElement>>(),
            available: false));

    private HomeAssistantDeviceDefinition CreateDefinition(BmsDeviceConfig device) => new(
        Key(device),
        device.Alias,
        [
            new HomeAssistantSensorDefinition("battery_voltage", "Battery voltage", "V", "voltage", Measurement),
            new HomeAssistantSensorDefinition("battery_net_current", "Battery net current", "A", "current", Measurement),
            new HomeAssistantSensorDefinition("battery_net_power", "Battery net power", "W", "power", Measurement),
            new HomeAssistantSensorDefinition("state_of_charge", "State of charge", "%", "battery", Measurement),
            new HomeAssistantSensorDefinition("battery_temperature_1", "Battery temperature 1", "°C", "temperature", Measurement, entityCategory: "diagnostic"),
            new HomeAssistantSensorDefinition("battery_temperature_2", "Battery temperature 2", "°C", "temperature", Measurement, entityCategory: "diagnostic"),
            new HomeAssistantSensorDefinition("cell_delta", "Cell voltage delta", "mV", "voltage", Measurement, entityCategory: "diagnostic"),
            new HomeAssistantBinarySensorDefinition("balancing", "Balancing", icon: "mdi:scale-balance", entityCategory: "diagnostic"),
            new HomeAssistantBinarySensorDefinition("alarm", "Alarm", deviceClass: "problem"),
            new HomeAssistantSensorDefinition("cycle_count", "Cycle count", stateClass: "total_increasing", entityCategory: "diagnostic"),
        ],
        manufacturer: "Jikong",
        model: "JK BMS");

    private HomeAssistantDeviceKey Key(BmsDeviceConfig device) =>
        new(siteId, identity.GatewayId, device.DeviceId);
}
