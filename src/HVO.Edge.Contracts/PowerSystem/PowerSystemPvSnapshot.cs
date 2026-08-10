namespace HVO.Edge.Contracts.PowerSystem;

public sealed record PowerSystemPvSnapshot(
    SourcedValue<double>? PowerW = null,
    IReadOnlyList<PowerSystemPvTrackerSnapshot>? Trackers = null,
    int? ExpectedTrackerCount = null,
    int? ReportedTrackerCount = null);
