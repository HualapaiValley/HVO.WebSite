using System.Text.Json.Serialization;

namespace HVO.NinaClient.Models;

/// <summary>
/// Flat device information
/// </summary>
public record FlatDeviceInfo : DeviceInfo
{
    [JsonPropertyName("CoverState")]
    public string? CoverState { get; init; }

    [JsonPropertyName("LocalizedCoverState")]
    public string? LocalizedCoverState { get; init; }

    [JsonPropertyName("LocalizedLightOnState")]
    public string? LocalizedLightOnState { get; init; }

    [JsonPropertyName("LightOn")]
    public bool LightOn { get; init; }

    [JsonPropertyName("Brightness")]
    public int Brightness { get; init; }

    [JsonPropertyName("SupportsOpenClose")]
    public bool SupportsOpenClose { get; init; }

    [JsonPropertyName("MinBrightness")]
    public int MinBrightness { get; init; }

    [JsonPropertyName("MaxBrightness")]
    public int MaxBrightness { get; init; }

    [JsonPropertyName("SupportsOnOff")]
    public bool SupportsOnOff { get; init; }

    [JsonPropertyName("SupportedActions")]
    public List<object>? SupportedActions { get; init; }
}
