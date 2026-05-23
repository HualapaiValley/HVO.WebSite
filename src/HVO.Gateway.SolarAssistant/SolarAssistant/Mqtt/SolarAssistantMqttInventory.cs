namespace HVO.Gateway.SolarAssistant.SolarAssistant.Mqtt;

public sealed class SolarAssistantMqttInventory
{
    public string ConnectionState { get; init; } = "disabled";

    public DateTime? LastConnectedAtUtc { get; init; }

    public DateTime? LastDisconnectedAtUtc { get; init; }

    public DateTime? LastMessageAtUtc { get; init; }

    public string? LastError { get; init; }

    public IReadOnlyList<string> Subscriptions { get; init; } = [];

    public int EntityCount { get; init; }

    public int StateTopicCount { get; init; }

    public int CommandTopicCount { get; init; }

    public int RetainedStateCount { get; init; }

    public IReadOnlyDictionary<string, int> ComponentCounts { get; init; } = new Dictionary<string, int>();

    public IReadOnlyDictionary<string, int> DeviceClassCounts { get; init; } = new Dictionary<string, int>();

    public IReadOnlyDictionary<string, int> StateClassCounts { get; init; } = new Dictionary<string, int>();

    public IReadOnlyDictionary<string, int> UnitCounts { get; init; } = new Dictionary<string, int>();

    public IReadOnlyDictionary<string, int> ClassificationCounts { get; init; } = new Dictionary<string, int>();

    public IReadOnlyList<SolarAssistantMqttDeviceSummary> Devices { get; init; } = [];

    public IReadOnlyList<SolarAssistantMqttEntitySummary> Entities { get; init; } = [];

    public IReadOnlyList<SolarAssistantMqttStateSummary> States { get; init; } = [];
}

public sealed class SolarAssistantMqttDeviceSummary
{
    public string Name { get; init; } = string.Empty;

    public string? Manufacturer { get; init; }

    public string? Model { get; init; }

    public string? SoftwareVersion { get; init; }

    public int EntityCount { get; init; }
}

public sealed class SolarAssistantMqttEntitySummary
{
    public string DiscoveryTopic { get; init; } = string.Empty;

    public string Component { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string? DeviceName { get; init; }

    public string? Manufacturer { get; init; }

    public string? Model { get; init; }

    public string? SoftwareVersion { get; init; }

    public string? DeviceClass { get; init; }

    public string? StateClass { get; init; }

    public string? Unit { get; init; }

    public string? EntityCategory { get; init; }

    public string? Icon { get; init; }

    public string? StateTopic { get; init; }

    public string? CommandTopic { get; init; }

    public string? AvailabilityTopic { get; init; }

    public string Classification { get; init; } = SolarAssistantMetricClassification.LocalOnly;
}

public sealed class SolarAssistantMqttStateSummary
{
    public string Topic { get; init; } = string.Empty;

    public DateTime LastSeenAtUtc { get; init; }

    public int MessageCount { get; init; }

    public bool Retain { get; init; }

    public int Qos { get; init; }

    public string PayloadKind { get; init; } = "unknown";

    public int PayloadLength { get; init; }
}
