using System.Text.Json.Serialization;

namespace HVO.NinaClient.Models;

/// <summary>
/// Image statistics
/// </summary>
public record ImageStatistics
{
    [JsonPropertyName("Stars")]
    public int Stars { get; init; }

    [JsonPropertyName("HFR")]
    public double HFR { get; init; }

    [JsonPropertyName("Median")]
    public double Median { get; init; }

    [JsonPropertyName("MedianAbsoluteDeviation")]
    public double MedianAbsoluteDeviation { get; init; }

    [JsonPropertyName("Mean")]
    public double Mean { get; init; }

    [JsonPropertyName("Max")]
    public int Max { get; init; }

    [JsonPropertyName("Min")]
    public int Min { get; init; }

    [JsonPropertyName("StDev")]
    public double StDev { get; init; }
}
