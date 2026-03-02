using System.Text.Json.Serialization;

namespace HVO.NinaClient.Models;

/// <summary>
/// Rotator information
/// </summary>
public record RotatorInfo : DeviceInfo
{
    [JsonPropertyName("CanReverse")]
    public bool CanReverse { get; init; }

    [JsonPropertyName("Reverse")]
    public bool Reverse { get; init; }

    [JsonPropertyName("MechanicalPosition")]
    public int MechanicalPosition { get; init; }

    [JsonPropertyName("Position")]
    public int Position { get; init; }

    [JsonPropertyName("StepSize")]
    public double StepSize { get; init; }

    [JsonPropertyName("IsMoving")]
    public bool IsMoving { get; init; }

    [JsonPropertyName("Synced")]
    public bool Synced { get; init; }

    [JsonPropertyName("SupportedActions")]
    public List<object>? SupportedActions { get; init; }
}
