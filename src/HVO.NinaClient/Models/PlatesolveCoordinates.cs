using System.Text.Json.Serialization;

namespace HVO.NinaClient.Models;

/// <summary>
/// Platesolve coordinates
/// </summary>
public record PlatesolveCoordinates
{
    [JsonPropertyName("RA")]
    public double RA { get; init; }

    [JsonPropertyName("RADegrees")]
    public double RADegrees { get; init; }

    [JsonPropertyName("Dec")]
    public double Dec { get; init; }

    [JsonPropertyName("DECDegrees")]
    public double DECDegrees { get; init; }

    [JsonPropertyName("Epoch")]
    public int Epoch { get; init; }
}
