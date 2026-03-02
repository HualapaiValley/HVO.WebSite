using System.Text.Json.Serialization;

namespace HVO.NinaClient.Models;

/// <summary>
/// Flat wizard filter-specific settings
/// </summary>
public class FlatWizardFilterSettings
{
    [JsonPropertyName("FlatWizardMode")]
    public FlatWizardMode FlatWizardMode { get; set; }

    [JsonPropertyName("HistogramMeanTarget")]
    public double HistogramMeanTarget { get; set; }

    [JsonPropertyName("HistogramTolerance")]
    public double HistogramTolerance { get; set; }

    [JsonPropertyName("MaxFlatExposureTime")]
    public int MaxFlatExposureTime { get; set; }

    [JsonPropertyName("MinFlatExposureTime")]
    public double MinFlatExposureTime { get; set; }

    [JsonPropertyName("MaxAbsoluteFlatDeviceBrightness")]
    public int MaxAbsoluteFlatDeviceBrightness { get; set; }

    [JsonPropertyName("MinAbsoluteFlatDeviceBrightness")]
    public int MinAbsoluteFlatDeviceBrightness { get; set; }

    [JsonPropertyName("Gain")]
    public int Gain { get; set; }

    [JsonPropertyName("Offset")]
    public int Offset { get; set; }

    [JsonPropertyName("Binning")]
    public BinningMode? Binning { get; set; }
}
