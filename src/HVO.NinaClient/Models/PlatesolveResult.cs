using System.Text.Json.Serialization;

namespace HVO.NinaClient.Models;

/// <summary>
/// Platesolve result
/// </summary>
public record PlatesolveResult
{
    [JsonPropertyName("Coordinates")]
    public PlatesolveCoordinates? Coordinates { get; init; }

    [JsonPropertyName("PositionAngle")]
    public double PositionAngle { get; init; }

    [JsonPropertyName("PixelScale")]
    public double PixelScale { get; init; }

    [JsonPropertyName("Radius")]
    public double Radius { get; init; }

    [JsonPropertyName("Flipped")]
    public bool Flipped { get; init; }

    [JsonPropertyName("Success")]
    public bool Success { get; init; }

    [JsonPropertyName("RaErrorString")]
    public string? RaErrorString { get; init; }

    [JsonPropertyName("RaPixError")]
    public double RaPixError { get; init; }

    [JsonPropertyName("DecPixError")]
    public double DecPixError { get; init; }

    [JsonPropertyName("DecErrorString")]
    public string? DecErrorString { get; init; }
}
