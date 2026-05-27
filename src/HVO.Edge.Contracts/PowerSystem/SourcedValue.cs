namespace HVO.Edge.Contracts.PowerSystem;

public sealed record SourcedValue<T>(
    T Value,
    PowerMetricSource Source,
    DateTime RecordedAtUtc,
    string? SourceId = null,
    string? DeviceId = null,
    string? Confidence = null);
