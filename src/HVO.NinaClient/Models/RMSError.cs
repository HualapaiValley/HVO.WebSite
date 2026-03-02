using System.Text.Json.Serialization;

namespace HVO.NinaClient.Models;

/// <summary>
/// RMS Error information
/// </summary>
public record RMSError
{
    [JsonPropertyName("RA")]
    public PixelArcsecondInfo? RA { get; init; }

    [JsonPropertyName("Dec")]
    public PixelArcsecondInfo? Dec { get; init; }

    [JsonPropertyName("Total")]
    public PixelArcsecondInfo? Total { get; init; }

    [JsonPropertyName("PeakRA")]
    public PixelArcsecondInfo? PeakRA { get; init; }

    [JsonPropertyName("PeakDec")]
    public PixelArcsecondInfo? PeakDec { get; init; }
}
