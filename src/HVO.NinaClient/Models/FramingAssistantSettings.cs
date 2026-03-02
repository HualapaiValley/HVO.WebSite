using System.Text.Json.Serialization;

namespace HVO.NinaClient.Models;

/// <summary>
/// FramingAssistantSettings for NINA profile - simplified version
/// </summary>
public class FramingAssistantSettings
{
    [JsonPropertyName("Id")]
    public string Id { get; set; } = string.Empty;
}
