using System.Text.Json.Serialization;

namespace HVO.NinaClient.Models;

/// <summary>
/// Camera capture response - for complex responses with image data and platesolve results
/// Based on NINA API specification for waitForResult=true responses
/// </summary>
public record CaptureResponse
{
    [JsonPropertyName("Image")]
    public string? Image { get; init; }

    [JsonPropertyName("PlateSolveResult")]
    public PlatesolveResult? PlateSolveResult { get; init; }
}