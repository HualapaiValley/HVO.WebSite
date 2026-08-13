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
            var stateValue = $"value_json.components.get({JsonSerializer.Serialize(componentId)})";
            var component = new JsonObject
            {
                ["platform"] = platform,
                ["name"] = entity.Name,
                ["unique_id"] = uniqueId,
                ["default_entity_id"] = entity.DefaultEntityId ?? $"{platform}.{uniqueId}",
                ["state_topic"] = topics.State(definition.Key),
                ["value_template"] = entity.Platform == HomeAssistantEntityPlatform.BinarySensor
                    ? $"{{% if {stateValue} %}}ON{{% else %}}OFF{{% endif %}}"
                    : $"{{{{ {stateValue} }}}}",
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
                if (sensor.SuggestedDisplayPrecision.HasValue)
                    component["suggested_display_precision"] = sensor.SuggestedDisplayPrecision.Value;
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
        AddOptional(device, "hw_version", definition.HardwareVersion);

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
        var defaultEntityIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entity in definition.Entities)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(entity.ComponentId);
            ArgumentException.ThrowIfNullOrWhiteSpace(entity.Name);
            if (entity is HomeAssistantSensorDefinition { SuggestedDisplayPrecision: < 0 })
                throw new ArgumentException("Suggested display precision cannot be negative.", nameof(definition));
            var componentId = HomeAssistantMqttIdentity.Normalize(entity.ComponentId);
            if (!normalized.Add(componentId))
                throw new ArgumentException($"Component identifier normalization collision for '{componentId}'.", nameof(definition));
            var expectedDomain = Platform(entity.Platform);
            var effectiveDefaultEntityId = entity.DefaultEntityId
                ?? $"{expectedDomain}.{HomeAssistantMqttIdentity.EntityUniqueId(definition.Key, entity.ComponentId)}";
            if (!IsValidEntityId(effectiveDefaultEntityId, expectedDomain))
                throw new ArgumentException($"Default entity ID '{effectiveDefaultEntityId}' is invalid for platform '{expectedDomain}'.", nameof(definition));
            if (!defaultEntityIds.Add(effectiveDefaultEntityId))
                throw new ArgumentException($"Default entity ID collision for '{effectiveDefaultEntityId}'.", nameof(definition));
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

    private static bool IsValidEntityId(string entityId, string expectedDomain)
    {
        var separator = entityId.IndexOf('.');
        if (separator <= 0 || separator == entityId.Length - 1
            || !entityId.AsSpan(0, separator).SequenceEqual(expectedDomain))
            return false;

        foreach (var character in entityId.AsSpan(separator + 1))
        {
            if (character is not (>= 'a' and <= 'z') and not (>= '0' and <= '9') and not '_')
                return false;
        }
        return true;
    }
}
