using System.Text.Json.Serialization;

namespace HVO.NinaClient.Models;

/// <summary>
/// SafetyMonitorSettings for NINA profile - simplified version
/// </summary>
public class SafetyMonitorSettings
{
    [JsonPropertyName("Id")]
    public string Id { get; set; } = string.Empty;
}
