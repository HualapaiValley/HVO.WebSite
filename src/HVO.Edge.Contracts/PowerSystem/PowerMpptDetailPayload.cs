namespace HVO.Edge.Contracts.PowerSystem;

/// <summary>Version 1 detail payload for independently measured MPPT trackers.</summary>
public sealed class PowerMpptDetailPayload
{
    public string SourceId { get; init; } = string.Empty;
    public string SourceSystem { get; init; } = string.Empty;
    public string? DeviceId { get; init; }
    public DateTime RecordedAtUtc { get; init; }
    public IReadOnlyList<PowerMpptTrackerDetail> Trackers { get; init; } = [];
    public PowerMpptBatteryOutputDetail? BatteryOutput { get; init; }
    public IReadOnlyList<PowerMpptTemperatureDetail> Temperatures { get; init; } = [];
    public IReadOnlyList<PowerMpptDiagnosticDetail> Diagnostics { get; init; } = [];
}

public sealed class PowerMpptTrackerDetail
{
    public string TrackerId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public double? VoltageV { get; init; }
    public double? CurrentA { get; init; }
    public double? PowerW { get; init; }
    public PowerObservationProvenance Provenance { get; init; }
    public string? Confidence { get; init; }
}

/// <summary>Controller output using canonical battery signs: charging current and power are negative.</summary>
public sealed class PowerMpptBatteryOutputDetail
{
    public double? VoltageV { get; init; }
    public double? CurrentA { get; init; }
    public double? PowerW { get; init; }
    public PowerObservationProvenance Provenance { get; init; }
    public string? Confidence { get; init; }
}

public sealed class PowerMpptTemperatureDetail
{
    public string TemperatureId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public double? TemperatureC { get; init; }
    public string? Confidence { get; init; }
}

public sealed class PowerMpptDiagnosticDetail
{
    public string Key { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
}
