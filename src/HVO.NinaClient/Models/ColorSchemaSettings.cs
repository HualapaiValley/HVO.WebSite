using System.Text.Json.Serialization;

namespace HVO.NinaClient.Models;

/// <summary>
/// Color schema settings for NINA profile
/// </summary>
public class ColorSchemaSettings
{
    [JsonPropertyName("AltColorSchema")]
    public string AltColorSchema { get; set; } = string.Empty;

    [JsonPropertyName("ColorSchema")]
    public string ColorSchema { get; set; } = string.Empty;
}
