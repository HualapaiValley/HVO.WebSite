using System.Text.Json.Serialization;

namespace HVO.NinaClient.Models;

/// <summary>
/// Switch device configuration
/// </summary>
public record SwitchDevice
{
    [JsonPropertyName("Id")]
    public int Id { get; init; }

    [JsonPropertyName("Name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("Value")]
    public double Value { get; init; }

    [JsonPropertyName("Description")]
    public string Description { get; init; } = "";

    [JsonPropertyName("CanWrite")]
    public bool CanWrite { get; init; }

    [JsonPropertyName("Minimum")]
    public double Minimum { get; init; }

    [JsonPropertyName("Maximum")]
    public double Maximum { get; init; }

    [JsonPropertyName("StepSize")]
    public double StepSize { get; init; }
}
