using System.Text.Json.Serialization;

namespace HVO.NinaClient.Models;

/// <summary>
/// Guider settings from NINA profile
/// </summary>
public record GuiderSettings
{
    [JsonPropertyName("AutoRetryStartGuiding")]
    public bool AutoRetryStartGuiding { get; init; }

    [JsonPropertyName("AutoRetryStartGuidingTimeoutSeconds")]
    public int AutoRetryStartGuidingTimeoutSeconds { get; init; }

    [JsonPropertyName("DitherPixels")]
    public double DitherPixels { get; init; }

    [JsonPropertyName("DitherRAOnly")]
    public bool DitherRAOnly { get; init; }

    [JsonPropertyName("GuiderName")]
    public string GuiderName { get; init; } = "";

    [JsonPropertyName("MaxY")]
    public double MaxY { get; init; }

    [JsonPropertyName("MinY")]
    public double MinY { get; init; }

    [JsonPropertyName("PHD2Path")]
    public string PHD2Path { get; init; } = "";

    [JsonPropertyName("PHD2ProfileId")]
    public int PHD2ProfileId { get; init; }

    [JsonPropertyName("PHD2ServerUrl")]
    public string PHD2ServerUrl { get; init; } = "";

    [JsonPropertyName("SettlePixels")]
    public double SettlePixels { get; init; }

    [JsonPropertyName("SettleTime")]
    public double SettleTime { get; init; }

    [JsonPropertyName("SettleTimeout")]
    public double SettleTimeout { get; init; }
}
