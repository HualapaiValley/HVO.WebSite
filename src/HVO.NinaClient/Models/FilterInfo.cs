using System.Text.Json.Serialization;

namespace HVO.NinaClient.Models;

/// <summary>
/// Filter information
/// </summary>
public record FilterInfo
{
    [JsonPropertyName("Name")]
    public string? Name { get; init; }

    [JsonPropertyName("Id")]
    public int Id { get; init; }

    [JsonPropertyName("FocusOffset")]
    public int FocusOffset { get; init; }

    [JsonPropertyName("Position")]
    public int Position { get; init; }
}
