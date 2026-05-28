namespace HVO.Edge.Contracts.PowerSystem;

public sealed record PowerSystemPvSnapshot(
    SourcedValue<double>? PowerW = null);
