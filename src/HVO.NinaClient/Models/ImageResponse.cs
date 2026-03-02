using System.Text.Json.Serialization;

namespace HVO.NinaClient.Models;

/// <summary>
/// Represents an image response that can contain base64 encoded image data
/// </summary>
public record ImageResponse
{
    [JsonPropertyName("Image")]
    public string? Image { get; init; }

    [JsonPropertyName("PlateSolveResult")]
    public PlatesolveResult? PlateSolveResult { get; init; }
}
