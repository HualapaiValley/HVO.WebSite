using System.Text.Json;
using HVO.Edge.HomeAssistant.Mqtt;
using HVO.Edge.Hosting;
using HVO.Hardware.Eg4.Configuration;
using HVO.Hardware.Eg4.Telemetry;
using Microsoft.Extensions.Options;

namespace HVO.Hardware.Eg4.HomeAssistant;

internal sealed class Eg4HomeAssistantProjection
{
    private const string Measurement = "measurement";
    private readonly IHomeAssistantMqttProjection projection;
    private readonly EdgeRuntimeIdentity identity;

    public Eg4HomeAssistantProjection(
        IHomeAssistantMqttProjection projection,
        EdgeRuntimeIdentity identity,
        IOptions<Eg4Options> options)
    {
        this.projection = projection;
        this.identity = identity;
        foreach (var device in (options.Value.Devices ?? []).Where(static device => device.Enabled))
            projection.UpsertDevice(CreateDefinition(device));
    }

    public bool Publish(Eg4DeviceOptions device, Eg4TelemetrySample sample)
    {
        var observation = sample.BatteryObservation ?? throw new ArgumentException("An available EG4 sample requires a battery observation.", nameof(sample));
        var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        Add(values, "battery_voltage", observation.VoltageV);
        Add(values, "battery_net_current", observation.CurrentA);
        Add(values, "battery_net_power", observation.PowerW);
        Add(values, "battery_state_of_charge", observation.StateOfChargePercent);
        Add(values, "pv_power", TotalPvPower(device, sample));
        Add(values, "load_power", sample.InverterDetail?.Load?.LoadPowerW);
        Add(values, "temperature", sample.InverterDetail?.TemperatureC
            ?? sample.MpptDetail?.Temperatures.FirstOrDefault(static temperature => temperature.TemperatureC.HasValue)?.TemperatureC);
        Add(values, "operating_mode", sample.InverterDetail?.Operating?.Mode);
        return projection.PublishCurrentState(new(
            Key(device),
            new DateTimeOffset(observation.ObservedAtUtc),
            values,
            available: true));
    }

    public bool PublishUnavailable(Eg4DeviceOptions device, DateTime observedAtUtc) =>
        projection.PublishCurrentState(new(
            Key(device),
            new DateTimeOffset(observedAtUtc),
            Array.Empty<KeyValuePair<string, JsonElement>>(),
            available: false));

    private HomeAssistantDeviceDefinition CreateDefinition(Eg4DeviceOptions device)
    {
        var entities = new List<HomeAssistantEntityDefinition>
        {
            new HomeAssistantSensorDefinition("battery_voltage", "Battery voltage", "V", "voltage", Measurement),
            new HomeAssistantSensorDefinition("battery_net_current", "Battery net current", "A", "current", Measurement),
            new HomeAssistantSensorDefinition("battery_net_power", "Battery net power", "W", "power", Measurement),
            new HomeAssistantSensorDefinition("battery_state_of_charge", "Inverter-reported battery state of charge", "%", "battery", Measurement, entityCategory: "diagnostic", enabledByDefault: false),
            new HomeAssistantSensorDefinition("pv_power", "PV power", "W", "power", Measurement),
            new HomeAssistantSensorDefinition("temperature", "Temperature", "°C", "temperature", Measurement, entityCategory: "diagnostic"),
        };
        if (device.Type == Eg4DeviceType.Inverter6500Ex)
        {
            entities.Add(new HomeAssistantSensorDefinition("load_power", "Load power", "W", "power", Measurement));
            entities.Add(new HomeAssistantSensorDefinition("operating_mode", "Operating mode", entityCategory: "diagnostic"));
        }
        return new(
            Key(device),
            device.Alias,
            entities,
            manufacturer: "EG4 Electronics",
            model: device.Type == Eg4DeviceType.Inverter6500Ex ? "6500EX" : "MPPT100-48HV");
    }

    private HomeAssistantDeviceKey Key(Eg4DeviceOptions device) =>
        new(identity.SiteId!, identity.GatewayId, device.DeviceId);

    private static void Add(Dictionary<string, JsonElement> values, string componentId, double? value)
    {
        if (value.HasValue)
            values[componentId] = JsonSerializer.SerializeToElement(value.Value);
    }

    private static void Add(Dictionary<string, JsonElement> values, string componentId, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            values[componentId] = JsonSerializer.SerializeToElement(value);
    }

    private static double? TotalPvPower(Eg4DeviceOptions device, Eg4TelemetrySample sample)
    {
        var trackers = sample.MpptDetail?.Trackers ?? [];
        var expectedCount = device.Type == Eg4DeviceType.Inverter6500Ex ? 2 : 1;
        return trackers.Count == expectedCount && trackers.All(static tracker => tracker.PowerW.HasValue)
            ? trackers.Sum(static tracker => tracker.PowerW!.Value)
            : null;
    }
}
