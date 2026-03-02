using System.Text.Json.Serialization;

namespace HVO.NinaClient.Models;

/// <summary>
/// Binning mode information
/// </summary>
public record BinningMode
{
    [JsonPropertyName("Name")]
    public string? Name { get; init; }

    [JsonPropertyName("X")]
    public int X { get; init; }

    [JsonPropertyName("Y")]
    public int Y { get; init; }
}
