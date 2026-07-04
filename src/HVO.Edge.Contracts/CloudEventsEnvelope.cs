using System.Text.Json.Serialization;

namespace HVO.Edge.Contracts;

/// <summary>
/// CloudEvents 1.0 envelope wrapping any telemetry payload for standards-compliant
/// delivery. The <see cref="Data"/> property carries the original gateway payload.
/// </summary>
/// <remarks>
/// Spec: https://github.com/cloudevents/spec/blob/v1.0.2/cloudevents/spec.md
/// </remarks>
public sealed record CloudEventsEnvelope<T>
{
    /// <summary>The CloudEvents specification version. Always "1.0".</summary>
    [JsonPropertyName("specversion")]
    public string SpecVersion { get; init; } = CloudEventsConstants.SpecVersion;

    /// <summary>
    /// Event type in reverse-DNS notation.
    /// Example: "com.hvo.weather.raw.v1"
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    /// <summary>
    /// Identifies the context in which the event happened.
    /// Example: "/gateways/davis/hvo-davis-01"
    /// </summary>
    [JsonPropertyName("source")]
    public string Source { get; init; } = string.Empty;

    /// <summary>
    /// Unique identifier for this event. Generated per-message.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Timestamp of when the occurrence happened (not when the event was published).
    /// </summary>
    [JsonPropertyName("time")]
    public DateTime Time { get; init; }

    /// <summary>
    /// Content type of the <see cref="Data"/> value. Always "application/json".
    /// </summary>
    [JsonPropertyName("datacontenttype")]
    public string DataContentType { get; init; } = CloudEventsConstants.JsonContentType;

    /// <summary>
    /// The domain-specific payload. This is the existing telemetry data
    /// (weather readings, BMS readings, power readings, etc.).
    /// </summary>
    [JsonPropertyName("data")]
    public T Data { get; init; } = default!;

    /// <summary>
    /// Creates a CloudEvents envelope from a telemetry envelope and source URI.
    /// </summary>
    public static CloudEventsEnvelope<TPayload> FromTelemetry<TPayload>(
        TelemetryEnvelope<TPayload> telemetry,
        string cloudEventType,
        string source)
        where TPayload : notnull
    {
        return new CloudEventsEnvelope<TPayload>
        {
            Type = cloudEventType,
            Source = source,
            Id = Guid.NewGuid().ToString("D"),
            Time = telemetry.RecordedAtUtc,
            Data = telemetry.Payload,
        };
    }
}
