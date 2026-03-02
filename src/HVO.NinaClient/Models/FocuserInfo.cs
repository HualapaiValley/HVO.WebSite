using System.Text.Json.Serialization;

namespace HVO.NinaClient.Models;

/// <summary>
/// Focuser information
/// </summary>
public record FocuserInfo : DeviceInfo
{
    [JsonPropertyName("Position")]
    public int Position { get; init; }

    [JsonPropertyName("StepSize")]
    public int StepSize { get; init; }

    [JsonPropertyName("Temperature")]
    public double Temperature { get; init; }

    [JsonPropertyName("IsMoving")]
    public bool IsMoving { get; init; }

    [JsonPropertyName("IsSettling")]
    public bool IsSettling { get; init; }

    [JsonPropertyName("TempComp")]
    public bool TempComp { get; init; }

    [JsonPropertyName("TempCompAvailable")]
    public bool TempCompAvailable { get; init; }

    [JsonPropertyName("SupportedActions")]
    public List<object>? SupportedActions { get; init; }
}
