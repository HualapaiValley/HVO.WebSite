using System.Text.Json.Serialization;

namespace HVO.NinaClient.Models;

/// <summary>
/// Framing assistant information from NINA Advanced API
/// </summary>
public record FramingAssistantInfo
{
    [JsonPropertyName("Source")]
    public string? Source { get; init; }

    [JsonPropertyName("RightAscension")]
    public double RightAscension { get; init; }

    [JsonPropertyName("Declination")]
    public double Declination { get; init; }

    [JsonPropertyName("Rotation")]
    public double Rotation { get; init; }

    [JsonPropertyName("FoVW")]
    public double FoVW { get; init; }

    [JsonPropertyName("FoVH")]
    public double FoVH { get; init; }

    [JsonPropertyName("CameraPixelX")]
    public int CameraPixelX { get; init; }

    [JsonPropertyName("CameraPixelY")]
    public int CameraPixelY { get; init; }

    [JsonPropertyName("CameraPixelSize")]
    public double CameraPixelSize { get; init; }

    [JsonPropertyName("TelescopeFocalLength")]
    public double TelescopeFocalLength { get; init; }

    [JsonPropertyName("DSO")]
    public string? DSO { get; init; }
}
