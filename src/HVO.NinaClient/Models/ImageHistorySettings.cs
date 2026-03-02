using System.Text.Json.Serialization;

namespace HVO.NinaClient.Models;

/// <summary>
/// ImageHistorySettings for NINA profile - simplified version
/// </summary>
public class ImageHistorySettings
{
    [JsonPropertyName("Id")]
    public string Id { get; set; } = string.Empty;
}
