using System.Text.Json;
using HVO.Edge.HomeAssistant.Mqtt;
using HVO.Edge.Hosting;
using HVO.Hardware.VictronSmartShunt.Configuration;
using HVO.Hardware.VictronSmartShunt.SmartShunt;
using Microsoft.Extensions.Options;

namespace HVO.Hardware.VictronSmartShunt.HomeAssistant;

public interface ISmartShuntHomeAssistantProjection
{
    bool Publish(SmartShuntLiveSample sample);
    bool PublishUnavailable(DateTime observedAtUtc);
}

public sealed class SmartShuntHomeAssistantProjection : ISmartShuntHomeAssistantProjection
{
    private readonly IHomeAssistantMqttProjection projection;
    private readonly HomeAssistantDeviceKey key;

    public SmartShuntHomeAssistantProjection(IHomeAssistantMqttProjection projection, EdgeRuntimeIdentity identity, IOptions<SmartShuntOptions> options)
    {
        this.projection = projection;
        key = new(identity.SiteId ?? throw new InvalidOperationException("Edge:Runtime:SiteId is required."), identity.GatewayId, options.Value.DeviceId);
        projection.UpsertDevice(new(key, "Victron SmartShunt", [
            new HomeAssistantSensorDefinition("battery_voltage", "Battery voltage", "V", "voltage", "measurement", suggestedDisplayPrecision: 2),
            new HomeAssistantSensorDefinition("battery_net_current", "Battery net current", "A", "current", "measurement", suggestedDisplayPrecision: 3),
            new HomeAssistantSensorDefinition("battery_net_power", "Battery net power", "W", "power", "measurement", suggestedDisplayPrecision: 0),
            new HomeAssistantSensorDefinition("state_of_charge", "State of charge", "%", "battery", "measurement", suggestedDisplayPrecision: 2),
            new HomeAssistantSensorDefinition("consumed_ah", "Consumed amp hours", "Ah", stateClass: "measurement", suggestedDisplayPrecision: 1),
            new HomeAssistantSensorDefinition("remaining_time", "Remaining time", "min", "duration", "measurement", suggestedDisplayPrecision: 0),
        ], manufacturer: "Victron Energy", model: "SmartShunt"));
    }

    public bool Publish(SmartShuntLiveSample sample)
    {
        var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        Add(values, "battery_voltage", sample.VoltageV);
        Add(values, "battery_net_current", sample.CurrentA);
        Add(values, "battery_net_power", sample.PowerW);
        Add(values, "state_of_charge", sample.StateOfChargePercent);
        Add(values, "consumed_ah", sample.ConsumedAh);
        Add(values, "remaining_time", sample.RemainingMinutes);
        return projection.PublishCurrentState(new(key, new DateTimeOffset(sample.RecordedAtUtc), values, available: true));
    }

    public bool PublishUnavailable(DateTime observedAtUtc) => projection.PublishCurrentState(new(
        key, new DateTimeOffset(observedAtUtc), Array.Empty<KeyValuePair<string, JsonElement>>(), available: false));

    private static void Add(IDictionary<string, JsonElement> values, string id, double? value)
    {
        if (value.HasValue) values[id] = JsonSerializer.SerializeToElement(value.Value);
    }
}
