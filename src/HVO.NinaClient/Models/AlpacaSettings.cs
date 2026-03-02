using System.Text.Json.Serialization;

namespace HVO.NinaClient.Models;

/// <summary>
/// AlpacaSettings for NINA profile - simplified version
/// </summary>
public class AlpacaSettings
{
    [JsonPropertyName("Id")]
    public string Id { get; set; } = string.Empty;
}
