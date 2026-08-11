using System.Text.Json;
using System.Text.Json.Nodes;

namespace HVO.Edge.HomeAssistant.Mqtt;

internal static class HomeAssistantDiscoverySerializer
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = false };

    public static string Serialize(
        HomeAssistantDeviceDefinition definition,
        HomeAssistantMqttTopics topics,
        IEnumerable<HomeAssistantEntityDefinition>? removedComponents = null)
    {
        Validate(definition);
        var deviceId = HomeAssistantMqttIdentity.DeviceId(definition.Key);
        var components = new JsonObject();

        foreach (var entity in definition.Entities)
        {
            var componentId = HomeAssistantMqttIdentity.Normalize(entity.ComponentId);
            var uniqueId = HomeAssistantMqttIdentity.EntityUniqueId(definition.Key, entity.ComponentId);
            var platform = Platform(entity.Platform);
            var component = new JsonObject
            {
                ["platform"] = platform,
                ["name"] = entity.Name,
                ["unique_id"] = uniqueId,
                ["default_entity_id"] = $"{platform}.{uniqueId}",
                ["state_topic"] = topics.State(definition.Key),
                ["value_template"] = entity.Platform == HomeAssistantEntityPlatform.BinarySensor
                    ? $"{{% if value_json.components.{componentId} %}}ON{{% else %}}OFF{{% endif %}}"
                    : $"{{{{ value_json.components.{componentId} }}}}",
                ["availability_mode"] = "all",
                ["availability"] = new JsonArray
                {
                    Availability(topics.GatewayAvailability(definition.Key)),
                    Availability(topics.DeviceAvailability(definition.Key))
                },
                ["enabled_by_default"] = entity.EnabledByDefault
            };

            AddOptional(component, "device_class", entity.DeviceClass);
            AddOptional(component, "icon", entity.Icon);
            AddOptional(component, "entity_category", entity.EntityCategory);
            if (entity.Platform == HomeAssistantEntityPlatform.BinarySensor)
            {
                component["payload_on"] = "ON";
                component["payload_off"] = "OFF";
            }
            if (entity is HomeAssistantSensorDefinition sensor)
            {
                AddOptional(component, "unit_of_measurement", sensor.UnitOfMeasurement);
                AddOptional(component, "state_class", sensor.StateClass);
            }

            components.Add(componentId, component);
        }

        foreach (var removed in removedComponents ?? [])
        {
            var componentId = HomeAssistantMqttIdentity.Normalize(removed.ComponentId);
            if (!components.ContainsKey(componentId))
                components.Add(componentId, new JsonObject { ["platform"] = Platform(removed.Platform) });
        }

        var device = new JsonObject
        {
            ["identifiers"] = new JsonArray(deviceId),
            ["name"] = definition.Name
        };
        AddOptional(device, "manufacturer", definition.Manufacturer);
        AddOptional(device, "model", definition.Model);
        AddOptional(device, "sw_version", definition.SoftwareVersion);

        var root = new JsonObject
        {
            ["device"] = device,
            ["origin"] = new JsonObject
            {
                ["name"] = "HVO Edge",
                ["sw_version"] = typeof(HomeAssistantDiscoverySerializer).Assembly.GetName().Version?.ToString() ?? "1.0.0"
            },
            ["components"] = components
        };
        return root.ToJsonString(SerializerOptions);
    }

    public static void Validate(HomeAssistantDeviceDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.Key.SiteId);
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.Key.GatewayId);
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.Key.DeviceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.Name);
        if (definition.Entities.IsDefaultOrEmpty)
            throw new ArgumentException("A Home Assistant device must define at least one entity.", nameof(definition));

        var normalized = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entity in definition.Entities)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(entity.ComponentId);
            ArgumentException.ThrowIfNullOrWhiteSpace(entity.Name);
            var componentId = HomeAssistantMqttIdentity.Normalize(entity.ComponentId);
            if (!normalized.Add(componentId))
                throw new ArgumentException($"Component identifier normalization collision for '{componentId}'.", nameof(definition));
        }
    }

    private static JsonObject Availability(string topic) => new()
    {
        ["topic"] = topic,
        ["payload_available"] = "online",
        ["payload_not_available"] = "offline"
    };

    private static void AddOptional(JsonObject target, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            target[name] = value;
    }

    private static string Platform(HomeAssistantEntityPlatform platform) => platform switch
    {
        HomeAssistantEntityPlatform.Sensor => "sensor",
        HomeAssistantEntityPlatform.BinarySensor => "binary_sensor",
        _ => throw new ArgumentOutOfRangeException(nameof(platform))
    };
}
