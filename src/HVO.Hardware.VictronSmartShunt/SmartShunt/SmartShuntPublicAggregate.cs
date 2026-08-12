namespace HVO.Hardware.VictronSmartShunt.SmartShunt;

/// <summary>Builds a coherent public-GATT sample whose age is bounded by its oldest required field.</summary>
public sealed class SmartShuntPublicAggregate
{
    private static readonly string[] RequiredFields = ["voltage", "current", "power", "soc"];
    private readonly Dictionary<string, FieldState> fields = new(StringComparer.OrdinalIgnoreCase);

    public void Reset() => fields.Clear();

    public SmartShuntLiveSample? Update(string fieldKey, byte[] value, DateTime updatedAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldKey);
        ArgumentNullException.ThrowIfNull(value);
        if (updatedAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("SmartShunt field timestamps must be UTC.", nameof(updatedAtUtc));
        fields[fieldKey] = new(value, updatedAtUtc);
        return CurrentSample();
    }

    public SmartShuntLiveSample? CurrentSample()
    {
        if (RequiredFields.Any(field => !fields.ContainsKey(field)))
            return null;
        var recordedAtUtc = RequiredFields.Min(field => fields[field].UpdatedAtUtc);
        return SmartShuntPublicProtocol.DecodeSample(
            fields.ToDictionary(static pair => pair.Key, static pair => pair.Value.Value, StringComparer.OrdinalIgnoreCase),
            recordedAtUtc);
    }

    private sealed record FieldState(byte[] Value, DateTime UpdatedAtUtc);
}
