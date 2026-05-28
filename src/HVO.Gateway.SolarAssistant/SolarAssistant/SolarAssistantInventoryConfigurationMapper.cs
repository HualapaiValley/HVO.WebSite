using HVO.Edge.Contracts.PowerSystem;
using HVO.Gateway.SolarAssistant.SolarAssistant.Mqtt;

namespace HVO.Gateway.SolarAssistant.SolarAssistant;

public static class SolarAssistantInventoryConfigurationMapper
{
    private static readonly string[] ConfigurationFragments =
    [
        "charge",
        "current",
        "priority",
        "shutdown",
        "source",
        "voltage",
    ];

    public static PowerDeviceInventoryPayload MapDeviceInventory(
        IReadOnlyList<SolarAssistantMetric> metrics,
        SolarAssistantMqttInventory mqttInventory,
        DateTime recordedAtUtc)
    {
        var devices = mqttInventory.Devices
            .Select(d => new PowerDeviceInventoryDevice
            {
                DeviceId = MakeDeviceId(d.Name),
                Name = d.Name,
                Manufacturer = NormalizeOptional(d.Manufacturer),
                Model = NormalizeOptional(d.Model),
                FirmwareVersion = NormalizeOptional(d.SoftwareVersion),
                EntityCount = d.EntityCount,
            })
            .ToList();

        if (devices.Count == 0)
        {
            var model = ReadMetricString(metrics, "model_name", "model_number", "inverter_1/model_name", "inverter_1/model_number");
            var firmware = ReadMetricString(metrics, "firmware_version", "inverter_1/firmware_version");
            if (!string.IsNullOrWhiteSpace(model) || !string.IsNullOrWhiteSpace(firmware))
            {
                devices.Add(new PowerDeviceInventoryDevice
                {
                    DeviceId = "solarassistant-inverter",
                    Name = model ?? "SolarAssistant inverter",
                    Model = model,
                    FirmwareVersion = firmware,
                });
            }
        }

        return new PowerDeviceInventoryPayload
        {
            SourceId = "solarassistant-total",
            SourceSystem = "solarassistant",
            DeviceId = "total",
            RecordedAtUtc = recordedAtUtc.ToUniversalTime(),
            Devices = devices.OrderBy(d => d.DeviceId, StringComparer.OrdinalIgnoreCase).ToArray(),
            RestMetricCount = metrics.Count,
            MqttEntityCount = mqttInventory.EntityCount,
            MqttStateTopicCount = mqttInventory.StateTopicCount,
        };
    }

    public static PowerConfigurationPayload MapConfiguration(
        IReadOnlyList<SolarAssistantMetric> metrics,
        SolarAssistantMqttInventory mqttInventory,
        DateTime recordedAtUtc)
    {
        var settings = metrics
            .Where(m => IsConfigurationTopic(m.Topic, m.Name))
            .Select(m => new PowerConfigurationSetting
            {
                Key = NormalizeKey(m.Topic),
                Name = NormalizeOptional(m.Name) ?? NormalizeKey(m.Topic),
                Value = FormatValue(m.Value),
                Unit = NormalizeOptional(m.Unit),
                DeviceId = ReadDeviceId(m.Topic),
                SourceTopic = NormalizeTopic(m.Topic),
            })
            .Concat(mqttInventory.Entities
                .Where(e => IsConfigurationTopic(e.StateTopic, e.Name) || !string.IsNullOrWhiteSpace(e.CommandTopic))
                .Select(e => new PowerConfigurationSetting
                {
                    Key = NormalizeKey(e.StateTopic ?? e.DiscoveryTopic),
                    Name = NormalizeOptional(e.Name) ?? NormalizeKey(e.StateTopic ?? e.DiscoveryTopic),
                    Unit = NormalizeOptional(e.Unit),
                    DeviceId = ReadDeviceId(e.StateTopic),
                    SourceTopic = NormalizeOptional(e.StateTopic),
                }))
            .GroupBy(s => s.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(s => s.Key, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var commandCapabilities = mqttInventory.Entities
            .Where(e => !string.IsNullOrWhiteSpace(e.CommandTopic))
            .Select(e => new PowerCommandCapability
            {
                Key = NormalizeKey(e.CommandTopic!),
                Name = NormalizeOptional(e.Name) ?? NormalizeKey(e.CommandTopic!),
                CommandTopic = e.CommandTopic!,
                StateTopic = NormalizeOptional(e.StateTopic),
                DeviceId = ReadDeviceId(e.CommandTopic),
            })
            .OrderBy(c => c.CommandTopic, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new PowerConfigurationPayload
        {
            SourceId = "solarassistant-total",
            SourceSystem = "solarassistant",
            DeviceId = "total",
            RecordedAtUtc = recordedAtUtc.ToUniversalTime(),
            Settings = settings,
            CommandCapabilities = commandCapabilities,
        };
    }

    private static bool IsConfigurationTopic(string? topic, string? name)
    {
        var value = $"{topic} {name}";
        return ConfigurationFragments.Any(fragment => value.Contains(fragment, StringComparison.OrdinalIgnoreCase));
    }

    private static string? ReadMetricString(IEnumerable<SolarAssistantMetric> metrics, params string[] topics) =>
        metrics.FirstOrDefault(m => topics.Contains(NormalizeTopic(m.Topic), StringComparer.OrdinalIgnoreCase))?.Value?.ToString()?.Trim();

    private static string NormalizeTopic(string? topic)
    {
        var normalized = (topic ?? string.Empty).Trim().Trim('/');
        if (normalized.StartsWith("solar_assistant/", StringComparison.OrdinalIgnoreCase))
            normalized = normalized["solar_assistant/".Length..];
        if (normalized.EndsWith("/state", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[..^"/state".Length];
        if (normalized.EndsWith("/set", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[..^"/set".Length];
        return normalized;
    }

    private static string NormalizeKey(string? topic) => NormalizeTopic(topic).Replace('/', '.');

    private static string? ReadDeviceId(string? topic)
    {
        var normalized = NormalizeTopic(topic);
        var parts = normalized.Split('/', 2, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 1 ? parts[0] : null;
    }

    private static string MakeDeviceId(string name) =>
        string.Join('-', name.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? FormatValue(object? value) =>
        value switch
        {
            null => null,
            System.Text.Json.JsonElement element => element.ToString(),
            _ => value.ToString(),
        };
}
