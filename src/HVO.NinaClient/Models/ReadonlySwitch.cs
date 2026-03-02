using System.Text.Json.Serialization;

namespace HVO.NinaClient.Models;

/// <summary>
/// Readonly switch information
/// </summary>
public record ReadonlySwitch
{
    [JsonPropertyName("Id")]
    public int Id { get; init; }

    [JsonPropertyName("Name")]
    public string? Name { get; init; }

    [JsonPropertyName("Description")]
    public string? Description { get; init; }

    [JsonPropertyName("Value")]
    public int Value { get; init; }
}
