namespace HVO.Edge.Contracts.PowerSystem;

public sealed class PowerEnergyPayload
{
    public string SourceId { get; init; } = string.Empty;
    public string SourceSystem { get; init; } = string.Empty;
    public string? DeviceId { get; init; }
    public DateTime RecordedAtUtc { get; init; }
    public bool CounterResetDetected { get; init; }
    public IReadOnlyList<PowerEnergyCounter> Counters { get; init; } = [];
}

public sealed class PowerEnergyCounter
{
    public string Key { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public double ValueKwh { get; init; }
    public string? DeviceId { get; init; }
    public string? SourceTopic { get; init; }
}
