using System.Text.Json.Serialization;

namespace HVO.NinaClient.Models;

/// <summary>
/// Response model for livestack image data
/// </summary>
public class LivestackImageResponse
{
    /// <summary>
    /// Base64 encoded image data
    /// </summary>
    [JsonPropertyName("Response")]
    public string Image { get; set; } = string.Empty;

    /// <summary>
    /// Error message if any
    /// </summary>
    [JsonPropertyName("Error")]
    public string Error { get; set; } = string.Empty;

    /// <summary>
    /// HTTP status code
    /// </summary>
    [JsonPropertyName("StatusCode")]
    public int StatusCode { get; set; }

    /// <summary>
    /// Whether the operation was successful
    /// </summary>
    [JsonPropertyName("Success")]
    public bool Success { get; set; }

    /// <summary>
    /// Response type
    /// </summary>
    [JsonPropertyName("Type")]
    public string Type { get; set; } = string.Empty;
}
