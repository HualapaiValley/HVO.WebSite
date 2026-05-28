namespace HVO.Edge.Contracts.PowerSystem;

public sealed class PowerConfigurationPayload
{
    public string SourceId { get; init; } = string.Empty;
    public string SourceSystem { get; init; } = string.Empty;
    public string? DeviceId { get; init; }
    public DateTime RecordedAtUtc { get; init; }
    public IReadOnlyList<PowerConfigurationSetting> Settings { get; init; } = [];
    public IReadOnlyList<PowerCommandCapability> CommandCapabilities { get; init; } = [];
}

public sealed class PowerConfigurationSetting
{
    public string Key { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Value { get; init; }
    public string? Unit { get; init; }
    public string? DeviceId { get; init; }
    public string? SourceTopic { get; init; }
}

public sealed class PowerCommandCapability
{
    public string Key { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string CommandTopic { get; init; } = string.Empty;
    public string? StateTopic { get; init; }
    public string? DeviceId { get; init; }
}
