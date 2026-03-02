using System.Text.Json.Serialization;

namespace HVO.NinaClient.Models;

/// <summary>
/// Rotator settings from NINA profile
/// </summary>
public record RotatorSettings
{
    [JsonPropertyName("Id")]
    public string Id { get; init; } = "";

    [JsonPropertyName("Name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("RangeStartMechanicalPosition")]
    public double RangeStartMechanicalPosition { get; init; }

    [JsonPropertyName("RangeType")]
    public string RangeType { get; init; } = "";

    [JsonPropertyName("Reverse")]
    public bool Reverse { get; init; }

    [JsonPropertyName("StepSize")]
    public double StepSize { get; init; }
}
