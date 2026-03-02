using System.Text.Json.Serialization;

namespace HVO.NinaClient.Models;

/// <summary>
/// Coordinate information
/// </summary>
public record CoordinateInfo
{
    [JsonPropertyName("RA")]
    public double RA { get; init; }

    [JsonPropertyName("RAString")]
    public string? RAString { get; init; }

    [JsonPropertyName("RADegrees")]
    public double RADegrees { get; init; }

    [JsonPropertyName("Dec")]
    public double Dec { get; init; }

    [JsonPropertyName("DecString")]
    public string? DecString { get; init; }

    [JsonPropertyName("Epoch")]
    public string? Epoch { get; init; }

    [JsonPropertyName("DateTime")]
    public DateTimeInfo? DateTime { get; init; }
}
