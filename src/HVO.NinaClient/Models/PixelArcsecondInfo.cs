using System.Text.Json.Serialization;

namespace HVO.NinaClient.Models;

/// <summary>
/// Pixel and arcsecond information
/// </summary>
public record PixelArcsecondInfo
{
    [JsonPropertyName("Pixel")]
    public int Pixel { get; init; }

    [JsonPropertyName("Arcseconds")]
    public int Arcseconds { get; init; }
}
