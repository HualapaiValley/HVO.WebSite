using System.Text.Json.Serialization;

namespace HVO.NinaClient.Models;

/// <summary>
/// Individual filter configuration within a filter wheel
/// </summary>
public class FilterWheelFilter
{
    [JsonPropertyName("Name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("FocusOffset")]
    public int FocusOffset { get; set; }

    [JsonPropertyName("Position")]
    public int Position { get; set; }

    [JsonPropertyName("AutoFocusExposureTime")]
    public int AutoFocusExposureTime { get; set; }

    [JsonPropertyName("AutoFocusFilter")]
    public bool AutoFocusFilter { get; set; }

    [JsonPropertyName("FlatWizardFilterSettings")]
    public FlatWizardFilterSettings? FlatWizardFilterSettings { get; set; }

    [JsonPropertyName("AutoFocusBinning")]
    public BinningMode? AutoFocusBinning { get; set; }

    [JsonPropertyName("AutoFocusGain")]
    public int AutoFocusGain { get; set; }

    [JsonPropertyName("AutoFocusOffset")]
    public int AutoFocusOffset { get; set; }
}
