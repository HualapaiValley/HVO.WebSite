using System.Text.Json.Serialization;

namespace HVO.NinaClient.Models;

/// <summary>
/// Switch settings from NINA profile
/// </summary>
public record SwitchSettings
{
    [JsonPropertyName("Id")]
    public string Id { get; init; } = "";

    [JsonPropertyName("Name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("Switches")]
    public SwitchDevice[] Switches { get; init; } = Array.Empty<SwitchDevice>();
}
