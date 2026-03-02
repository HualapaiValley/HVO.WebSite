using System.Text.Json.Serialization;

namespace HVO.NinaClient.Models;

/// <summary>
/// Plate solve settings from NINA profile
/// </summary>
public record PlateSolveSettings
{
    [JsonPropertyName("Binning")]
    public int Binning { get; init; }

    [JsonPropertyName("BlindFailoverEnabled")]
    public bool BlindFailoverEnabled { get; init; }

    [JsonPropertyName("Downsample")]
    public int Downsample { get; init; }

    [JsonPropertyName("ExposureTime")]
    public double ExposureTime { get; init; }

    [JsonPropertyName("Filter")]
    public string Filter { get; init; } = "";

    [JsonPropertyName("Gain")]
    public int Gain { get; init; }

    [JsonPropertyName("MaxObjects")]
    public int MaxObjects { get; init; }

    [JsonPropertyName("Offset")]
    public int Offset { get; init; }

    [JsonPropertyName("PlateSolveMethod")]
    public PlateSolveMethod PlateSolveMethod { get; init; }

    [JsonPropertyName("Regions")]
    public int Regions { get; init; }

    [JsonPropertyName("SearchRadius")]
    public double SearchRadius { get; init; }

    [JsonPropertyName("Sync")]
    public bool Sync { get; init; }

    [JsonPropertyName("Threshold")]
    public double Threshold { get; init; }
}
