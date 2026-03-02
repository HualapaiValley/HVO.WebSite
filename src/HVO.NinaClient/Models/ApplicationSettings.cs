using System.Text.Json.Serialization;

namespace HVO.NinaClient.Models;

/// <summary>
/// Application settings for NINA profile
/// </summary>
public class ApplicationSettings
{
    [JsonPropertyName("Culture")]
    public string Culture { get; set; } = string.Empty;

    [JsonPropertyName("DevicePollingInterval")]
    public int DevicePollingInterval { get; set; }

    [JsonPropertyName("PageSize")]
    public int PageSize { get; set; }

    [JsonPropertyName("LogLevel")]
    public LogLevel LogLevel { get; set; }
}
