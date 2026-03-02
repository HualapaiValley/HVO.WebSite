using System.Text.Json.Serialization;

namespace HVO.NinaClient.Models;

/// <summary>
/// Star detection result
/// </summary>
public record StarDetectionResult
{
    [JsonPropertyName("DetectedStars")]
    public int DetectedStars { get; init; }

    [JsonPropertyName("HFR")]
    public double HFR { get; init; }

    [JsonPropertyName("HFRStDev")]
    public double HFRStDev { get; init; }

    [JsonPropertyName("Success")]
    public bool Success { get; init; }
}
