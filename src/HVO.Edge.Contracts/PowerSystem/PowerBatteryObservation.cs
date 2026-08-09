using System.Text.Json.Serialization;

namespace HVO.Edge.Contracts.PowerSystem;

/// <summary>
/// A role-safe battery observation. Positive current and power represent discharge from
/// the battery measurement point; negative values represent charge into it.
/// </summary>
public sealed record PowerBatteryObservation
{
    [JsonConstructor]
    public PowerBatteryObservation(
        string SourceId,
        string DeviceId,
        PowerMetricSource Source,
        PowerMeasurementRole Role,
        string MeasurementPoint,
        DateTime ObservedAtUtc,
        double? VoltageV = null,
        double? CurrentA = null,
        double? PowerW = null,
        double? StateOfChargePercent = null,
        PowerObservationProvenance Provenance = PowerObservationProvenance.Direct,
        string? Confidence = null,
        IReadOnlyList<PowerObservationInput>? Inputs = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(SourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(DeviceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(MeasurementPoint);
        ValidateUtc(ObservedAtUtc, nameof(ObservedAtUtc));
        ValidateEnum(Source, nameof(Source));
        ValidateEnum(Role, nameof(Role));
        ValidateEnum(Provenance, nameof(Provenance));

        if (CurrentA is not null && PowerW is not null && CurrentA != 0 && PowerW != 0 &&
            Math.Sign(CurrentA.Value) != Math.Sign(PowerW.Value))
        {
            throw new ArgumentException("Canonical battery current and power must have the same direction.", nameof(PowerW));
        }

        this.SourceId = SourceId;
        this.DeviceId = DeviceId;
        this.Source = Source;
        this.Role = Role;
        this.MeasurementPoint = MeasurementPoint;
        this.ObservedAtUtc = ObservedAtUtc;
        this.VoltageV = VoltageV;
        this.CurrentA = CurrentA;
        this.PowerW = PowerW;
        this.StateOfChargePercent = StateOfChargePercent;
        this.Provenance = Provenance;
        this.Confidence = Confidence;
        this.Inputs = Inputs;
    }

    public string SourceId { get; }
    public string DeviceId { get; }

    [JsonConverter(typeof(JsonStringEnumConverter<PowerMetricSource>))]
    public PowerMetricSource Source { get; }

    public PowerMeasurementRole Role { get; }
    public string MeasurementPoint { get; }
    public DateTime ObservedAtUtc { get; }
    public double? VoltageV { get; }
    public double? CurrentA { get; }
    public double? PowerW { get; }
    public double? StateOfChargePercent { get; }
    public PowerObservationProvenance Provenance { get; }
    public string? Confidence { get; }
    public IReadOnlyList<PowerObservationInput>? Inputs { get; }

    private static void ValidateUtc(DateTime value, string parameterName)
    {
        if (value.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Observation timestamps must be UTC.", parameterName);
    }

    private static void ValidateEnum<T>(T value, string parameterName) where T : struct, Enum
    {
        if (!Enum.IsDefined(value))
            throw new ArgumentOutOfRangeException(parameterName, value, "Enum value is not defined by the contract.");
    }
}

public sealed record PowerObservationInput
{
    [JsonConstructor]
    public PowerObservationInput(string SourceId, DateTime ObservedAtUtc, string? DeviceId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(SourceId);
        if (DeviceId is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(DeviceId);
        if (ObservedAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Observation timestamps must be UTC.", nameof(ObservedAtUtc));

        this.SourceId = SourceId;
        this.ObservedAtUtc = ObservedAtUtc;
        this.DeviceId = DeviceId;
    }

    public string SourceId { get; }
    public DateTime ObservedAtUtc { get; }
    public string? DeviceId { get; }
}
