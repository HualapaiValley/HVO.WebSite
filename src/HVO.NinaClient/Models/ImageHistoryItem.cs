using System.Text.Json.Serialization;

namespace HVO.NinaClient.Models;


/// <summary>
/// Represents an item in the image history
/// </summary>
public record ImageHistoryItem
{
    [JsonPropertyName("ExposureTime")]
    public double ExposureTime { get; init; }

    [JsonPropertyName("ImageType")]
    public string ImageType { get; init; } = string.Empty;

    [JsonPropertyName("Filter")]
    public string Filter { get; init; } = string.Empty;

    [JsonPropertyName("RmsText")]
    public string RmsText { get; init; } = string.Empty;

    [JsonPropertyName("Temperature")]
    public string Temperature { get; init; } = string.Empty;

    [JsonPropertyName("CameraName")]
    public string CameraName { get; init; } = string.Empty;

    [JsonPropertyName("Gain")]
    public int Gain { get; init; }

    [JsonPropertyName("Offset")]
    public int Offset { get; init; }

    [JsonPropertyName("Date")]
    public string Date { get; init; } = string.Empty;

    [JsonPropertyName("TelescopeName")]
    public string TelescopeName { get; init; } = string.Empty;

    [JsonPropertyName("FocalLength")]
    public double FocalLength { get; init; }

    [JsonPropertyName("StDev")]
    public double StDev { get; init; }

    [JsonPropertyName("Mean")]
    public double Mean { get; init; }

    [JsonPropertyName("Median")]
    public double Median { get; init; }

    [JsonPropertyName("Stars")]
    public int Stars { get; init; }

    [JsonPropertyName("HFR")]
    public double HFR { get; init; }

    [JsonPropertyName("IsBayered")]
    public bool IsBayered { get; init; }
}
