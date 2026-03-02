using System.Text.Json.Serialization;

namespace HVO.NinaClient.Models;

/// <summary>
/// Guide step information
/// </summary>
public record GuideStep
{
    [JsonPropertyName("RADistanceRaw")]
    public double RADistanceRaw { get; init; }

    [JsonPropertyName("DECDistanceRaw")]
    public double DECDistanceRaw { get; init; }

    [JsonPropertyName("RADuration")]
    public double RADuration { get; init; }

    [JsonPropertyName("DECDuration")]
    public double DECDuration { get; init; }
}
