using System.Text.Json.Serialization;

namespace HVO.Gateway.SolarAssistant.SolarAssistant;

/// <summary>A metric row returned by SolarAssistant REST /api/v1/metrics.</summary>
public sealed class SolarAssistantMetric
{
    [JsonPropertyName("topic")]
    public string Topic { get; init; } = string.Empty;

    [JsonPropertyName("group")]
    public string? Group { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("value")]
    public object? Value { get; init; }

    [JsonPropertyName("unit")]
    public string? Unit { get; init; }
}
