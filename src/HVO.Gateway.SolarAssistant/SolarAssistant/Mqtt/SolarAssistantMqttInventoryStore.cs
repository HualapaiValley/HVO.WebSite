using System.Text.Json;

namespace HVO.Gateway.SolarAssistant.SolarAssistant.Mqtt;

public sealed class SolarAssistantMqttInventoryStore
{
    private readonly object _lock = new();
    private readonly Dictionary<string, SolarAssistantMqttEntitySummary> _entities = new(StringComparer.Ordinal);
    private readonly Dictionary<string, MutableState> _states = new(StringComparer.Ordinal);
    private IReadOnlyList<string> _subscriptions = [];
    private string _connectionState = "disabled";
    private DateTime? _lastConnectedAtUtc;
    private DateTime? _lastDisconnectedAtUtc;
    private DateTime? _lastMessageAtUtc;
    private string? _lastError;

    public SolarAssistantMqttInventory Snapshot
    {
        get
        {
            lock (_lock)
            {
                var entities = _entities.Values
                    .OrderBy(e => e.DeviceName)
                    .ThenBy(e => e.Component)
                    .ThenBy(e => e.Name)
                    .ToArray();
                var states = _states.Values
                    .Select(s => s.ToSummary())
                    .OrderBy(s => s.Topic, StringComparer.Ordinal)
                    .ToArray();
                var devices = entities
                    .Where(e => !string.IsNullOrWhiteSpace(e.DeviceName))
                    .GroupBy(e => new DeviceKey(e.DeviceName!, e.Manufacturer, e.Model, e.SoftwareVersion))
                    .Select(g => new SolarAssistantMqttDeviceSummary
                    {
                        Name = g.Key.Name,
                        Manufacturer = g.Key.Manufacturer,
                        Model = g.Key.Model,
                        SoftwareVersion = g.Key.SoftwareVersion,
                        EntityCount = g.Count(),
                    })
                    .OrderBy(d => d.Name, StringComparer.Ordinal)
                    .ToArray();

                return new SolarAssistantMqttInventory
                {
                    ConnectionState = _connectionState,
                    LastConnectedAtUtc = _lastConnectedAtUtc,
                    LastDisconnectedAtUtc = _lastDisconnectedAtUtc,
                    LastMessageAtUtc = _lastMessageAtUtc,
                    LastError = _lastError,
                    Subscriptions = _subscriptions,
                    EntityCount = entities.Length,
                    StateTopicCount = states.Length,
                    CommandTopicCount = entities.Count(e => !string.IsNullOrWhiteSpace(e.CommandTopic)),
                    RetainedStateCount = states.Count(s => s.Retain),
                    ComponentCounts = CountBy(entities.Select(e => e.Component)),
                    DeviceClassCounts = CountBy(entities.Select(e => e.DeviceClass)),
                    StateClassCounts = CountBy(entities.Select(e => e.StateClass)),
                    UnitCounts = CountBy(entities.Select(e => e.Unit)),
                    ClassificationCounts = CountBy(entities.Select(e => e.Classification)),
                    Devices = devices,
                    Entities = entities,
                    States = states,
                };
            }
        }
    }

    public void MarkDisabled(string reason)
    {
        lock (_lock)
        {
            _connectionState = "disabled";
            _lastError = reason;
        }
    }

    public void MarkConnecting(IReadOnlyList<string> subscriptions)
    {
        lock (_lock)
        {
            _connectionState = "connecting";
            _subscriptions = subscriptions.ToArray();
        }
    }

    public void MarkConnected()
    {
        lock (_lock)
        {
            _connectionState = "connected";
            _lastConnectedAtUtc = DateTime.UtcNow;
            _lastError = null;
        }
    }

    public void MarkDisconnected(string? error)
    {
        lock (_lock)
        {
            _connectionState = "disconnected";
            _lastDisconnectedAtUtc = DateTime.UtcNow;
            _lastError = string.IsNullOrWhiteSpace(error) ? null : error;
        }
    }

    public void Apply(SolarAssistantMqttMessage message)
    {
        lock (_lock)
        {
            _lastMessageAtUtc = message.ReceivedAtUtc;
            if (IsHomeAssistantDiscovery(message.Topic))
            {
                var entity = ParseDiscovery(message);
                if (entity is not null)
                    _entities[message.Topic] = entity;
            }
            else if (message.Topic.StartsWith("solar_assistant/", StringComparison.Ordinal))
            {
                if (!_states.TryGetValue(message.Topic, out var state))
                {
                    state = new MutableState(message.Topic);
                    _states[message.Topic] = state;
                }

                state.Apply(message);
            }
        }
    }

    public static SolarAssistantMqttEntitySummary? ParseDiscovery(SolarAssistantMqttMessage message)
    {
        if (!IsHomeAssistantDiscovery(message.Topic))
            return null;

        try
        {
            using var document = JsonDocument.Parse(message.Payload);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return null;

            var component = ReadComponent(message.Topic);
            var stateTopic = ReadString(root, "stat_t", "state_topic");
            var commandTopic = ReadString(root, "cmd_t", "command_topic");
            var availabilityTopic = ReadString(root, "avty_t", "availability_topic");
            var device = ReadObject(root, "dev", "device");
            var name = ReadString(root, "name", "object_id") ?? string.Empty;
            var unit = ReadString(root, "unit_of_meas", "unit_of_measurement");
            var deviceClass = ReadString(root, "dev_cla", "device_class");
            var stateClass = ReadString(root, "stat_cla", "state_class");

            return new SolarAssistantMqttEntitySummary
            {
                DiscoveryTopic = message.Topic,
                Component = component,
                Name = name,
                DeviceName = ReadString(device, "name"),
                Manufacturer = ReadString(device, "mf", "manufacturer"),
                Model = ReadString(device, "mdl", "model"),
                SoftwareVersion = ReadString(device, "sw", "sw_version"),
                DeviceClass = deviceClass,
                StateClass = stateClass,
                Unit = unit,
                EntityCategory = ReadString(root, "ent_cat", "entity_category"),
                Icon = ReadString(root, "icon"),
                StateTopic = stateTopic,
                CommandTopic = commandTopic,
                AvailabilityTopic = availabilityTopic,
                Classification = Classify(stateTopic, unit, name),
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsHomeAssistantDiscovery(string topic) =>
        topic.StartsWith("homeassistant/", StringComparison.Ordinal) && topic.EndsWith("/config", StringComparison.Ordinal);

    private static string ReadComponent(string topic)
    {
        var levels = topic.Split('/');
        return levels.Length > 1 ? levels[1] : string.Empty;
    }

    private static JsonElement? ReadObject(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object)
                return value;
        }

        return null;
    }

    private static string? ReadString(JsonElement? root, params string[] names)
    {
        if (root is null)
            return null;

        foreach (var name in names)
        {
            if (!root.Value.TryGetProperty(name, out var value))
                continue;

            var text = value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
            if (!string.IsNullOrWhiteSpace(text))
                return text.Trim();
        }

        return null;
    }

    private static string Classify(string? stateTopic, string? unit, string name)
    {
        if (string.IsNullOrWhiteSpace(stateTopic))
            return SolarAssistantMetricClassification.LocalOnly;

        return SolarAssistantMetricInventoryBuilder.Classify(new SolarAssistantMetric
        {
            Topic = NormalizeStateTopic(stateTopic),
            Unit = unit,
            Name = name,
        });
    }

    private static string NormalizeStateTopic(string stateTopic)
    {
        var topic = stateTopic.Trim().Trim('/');
        if (topic.StartsWith("solar_assistant/", StringComparison.OrdinalIgnoreCase))
            topic = topic["solar_assistant/".Length..];
        if (topic.EndsWith("/state", StringComparison.OrdinalIgnoreCase))
            topic = topic[..^"/state".Length];
        return topic;
    }

    private static IReadOnlyDictionary<string, int> CountBy(IEnumerable<string?> values) => values
        .Where(v => !string.IsNullOrWhiteSpace(v))
        .GroupBy(v => v!, StringComparer.OrdinalIgnoreCase)
        .OrderByDescending(g => g.Count())
        .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

    private sealed class MutableState
    {
        public MutableState(string topic)
        {
            Topic = topic;
        }

        public string Topic { get; }
        public DateTime LastSeenAtUtc { get; private set; }
        public int MessageCount { get; private set; }
        public bool Retain { get; private set; }
        public int Qos { get; private set; }
        public string PayloadKind { get; private set; } = "unknown";
        public int PayloadLength { get; private set; }

        public void Apply(SolarAssistantMqttMessage message)
        {
            LastSeenAtUtc = message.ReceivedAtUtc;
            MessageCount++;
            Retain = message.Retain;
            Qos = message.Qos;
            PayloadLength = message.Payload.Length;
            PayloadKind = ClassifyPayload(message.Payload);
        }

        public SolarAssistantMqttStateSummary ToSummary() => new()
        {
            Topic = Topic,
            LastSeenAtUtc = LastSeenAtUtc,
            MessageCount = MessageCount,
            Retain = Retain,
            Qos = Qos,
            PayloadKind = PayloadKind,
            PayloadLength = PayloadLength,
        };

        private static string ClassifyPayload(string payload)
        {
            if (string.IsNullOrWhiteSpace(payload))
                return "empty";
            if (double.TryParse(payload, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _))
                return "number";
            if (bool.TryParse(payload, out _))
                return "boolean";
            return "text";
        }
    }

    private readonly record struct DeviceKey(string Name, string? Manufacturer, string? Model, string? SoftwareVersion);
}
