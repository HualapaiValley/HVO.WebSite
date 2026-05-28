namespace HVO.Edge.Contracts.PowerSystem;

public sealed class PowerDeviceInventoryPayload
{
    public string SourceId { get; init; } = string.Empty;
    public string SourceSystem { get; init; } = string.Empty;
    public string? DeviceId { get; init; }
    public DateTime RecordedAtUtc { get; init; }
    public IReadOnlyList<PowerDeviceInventoryDevice> Devices { get; init; } = [];
    public int RestMetricCount { get; init; }
    public int MqttEntityCount { get; init; }
    public int MqttStateTopicCount { get; init; }
}

public sealed class PowerDeviceInventoryDevice
{
    public string DeviceId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Manufacturer { get; init; }
    public string? Model { get; init; }
    public string? FirmwareVersion { get; init; }
    public int EntityCount { get; init; }
}
