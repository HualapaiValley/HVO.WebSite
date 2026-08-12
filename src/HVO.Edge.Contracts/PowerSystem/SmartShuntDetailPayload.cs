using System.Text.Json.Serialization;

namespace HVO.Edge.Contracts.PowerSystem;

/// <summary>Source-native public-GATT detail not represented by the power summary contract.</summary>
public sealed class SmartShuntDetailPayload
{
    [JsonPropertyName("sourceId")]
    public string? SourceId { get; init; }

    [JsonPropertyName("sourceSystem")]
    public string? SourceSystem { get; init; }

    [JsonPropertyName("deviceId")]
    public string? DeviceId { get; init; }

    [JsonPropertyName("recordedAtUtc")]
    public DateTime RecordedAtUtc { get; init; }

    [JsonPropertyName("consumedAh")]
    public double? ConsumedAh { get; init; }

    [JsonPropertyName("remainingMinutes")]
    public double? RemainingMinutes { get; init; }

    [JsonPropertyName("starterVoltageV")]
    public double? StarterVoltageV { get; init; }

    [JsonPropertyName("temperatureC")]
    public double? TemperatureC { get; init; }
}

/// <summary>One atomic SmartShunt observation containing summary and public-GATT detail.</summary>
public sealed record SmartShuntObservationPayload(
    [property: JsonPropertyName("summary")] PowerReadingPayload Summary,
    [property: JsonPropertyName("detail")] SmartShuntDetailPayload Detail);
