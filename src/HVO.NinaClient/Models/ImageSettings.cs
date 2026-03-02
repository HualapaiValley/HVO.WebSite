using System.Text.Json.Serialization;

namespace HVO.NinaClient.Models;

/// <summary>
/// Image settings from NINA profile
/// </summary>
public record ImageSettings
{
    [JsonPropertyName("AutoStretch")]
    public bool AutoStretch { get; init; }

    [JsonPropertyName("AutoStretchFactor")]
    public double AutoStretchFactor { get; init; }

    [JsonPropertyName("BlackClipping")]
    public double BlackClipping { get; init; }

    [JsonPropertyName("DetectStars")]
    public bool DetectStars { get; init; }

    [JsonPropertyName("UnlinkedStretch")]
    public bool UnlinkedStretch { get; init; }

    [JsonPropertyName("WhiteClipping")]
    public double WhiteClipping { get; init; }
}
