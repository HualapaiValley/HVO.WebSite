using System.Text.Json.Serialization;

namespace HVO.NinaClient.Models;

/// <summary>
/// Writable switch information
/// </summary>
public record WritableSwitch
{
    [JsonPropertyName("Maximum")]
    public int Maximum { get; init; }

    [JsonPropertyName("Minimum")]
    public int Minimum { get; init; }

    [JsonPropertyName("StepSize")]
    public double StepSize { get; init; }

    [JsonPropertyName("TargetValue")]
    public int TargetValue { get; init; }

    [JsonPropertyName("Id")]
    public int Id { get; init; }

    [JsonPropertyName("Name")]
    public string? Name { get; init; }

    [JsonPropertyName("Description")]
    public string? Description { get; init; }

    [JsonPropertyName("Value")]
    public int Value { get; init; }
}
