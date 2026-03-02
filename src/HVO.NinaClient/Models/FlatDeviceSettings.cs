using System.Text.Json.Serialization;

namespace HVO.NinaClient.Models;

/// <summary>
/// FlatDeviceSettings for NINA profile - simplified version
/// </summary>
public class FlatDeviceSettings
{
    [JsonPropertyName("Id")]
    public string Id { get; set; } = string.Empty;
}
