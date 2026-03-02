using System.Text.Json.Serialization;

namespace HVO.NinaClient.Models;

/// <summary>
/// Safety monitor information
/// </summary>
public record SafetyMonitorInfo : DeviceInfo
{
    [JsonPropertyName("IsSafe")]
    public bool IsSafe { get; init; }

    [JsonPropertyName("SupportedActions")]
    public List<object>? SupportedActions { get; init; }
}
