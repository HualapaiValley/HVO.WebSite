using System.Text.Json.Serialization;

namespace HVO.NinaClient.Models;

/// <summary>
/// Guider information
/// </summary>
public record GuiderInfo : DeviceInfo
{
    [JsonPropertyName("CanClearCalibration")]
    public bool CanClearCalibration { get; init; }

    [JsonPropertyName("CanSetShiftRate")]
    public bool CanSetShiftRate { get; init; }

    [JsonPropertyName("CanGetLockPosition")]
    public bool CanGetLockPosition { get; init; }

    [JsonPropertyName("SupportedActions")]
    public List<object>? SupportedActions { get; init; }

    [JsonPropertyName("RMSError")]
    public RMSError? RMSError { get; init; }

    [JsonPropertyName("PixelScale")]
    public double PixelScale { get; init; }

    [JsonPropertyName("LastGuideStep")]
    public GuideStep? LastGuideStep { get; init; }

    [JsonPropertyName("State")]
    public string? State { get; init; }
}
