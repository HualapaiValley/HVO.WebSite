using System.Text.Json.Serialization;

namespace HVO.NinaClient.Models;

/// <summary>
/// Sequence settings from NINA profile
/// </summary>
public record SequenceSettings
{
    [JsonPropertyName("CollapseSequencerTemplatesByDefault")]
    public bool CollapseSequencerTemplatesByDefault { get; init; }

    [JsonPropertyName("DefaultSequenceFolder")]
    public string DefaultSequenceFolder { get; init; } = "";

    [JsonPropertyName("DonutDetection")]
    public bool DonutDetection { get; init; }

    [JsonPropertyName("EstimatedDownloadTime")]
    public double EstimatedDownloadTime { get; init; }

    [JsonPropertyName("GuiderScaleBacklash")]
    public double GuiderScaleBacklash { get; init; }

    [JsonPropertyName("GuiderScaleX")]
    public double GuiderScaleX { get; init; }

    [JsonPropertyName("GuiderScaleY")]
    public double GuiderScaleY { get; init; }

    [JsonPropertyName("OpenLastUsedSequence")]
    public bool OpenLastUsedSequence { get; init; }

    [JsonPropertyName("SequenceCompleteCommand")]
    public string SequenceCompleteCommand { get; init; } = "";

    [JsonPropertyName("SequenceCompleteCommandArgs")]
    public string SequenceCompleteCommandArgs { get; init; } = "";

    [JsonPropertyName("TemplateFolder")]
    public string TemplateFolder { get; init; } = "";
}
