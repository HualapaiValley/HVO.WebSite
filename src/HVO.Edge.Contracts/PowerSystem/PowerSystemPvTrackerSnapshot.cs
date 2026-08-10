namespace HVO.Edge.Contracts.PowerSystem;

public sealed record PowerSystemPvTrackerSnapshot(
    string TrackerId,
    string Name,
    string SourceId,
    string? DeviceId,
    DateTime RecordedAtUtc,
    PowerMetricSource Source,
    double? VoltageV = null,
    double? CurrentA = null,
    double? PowerW = null,
    PowerObservationProvenance Provenance = PowerObservationProvenance.Unknown,
    string? Confidence = null);
