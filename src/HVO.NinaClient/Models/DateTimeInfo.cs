using System.Text.Json.Serialization;

namespace HVO.NinaClient.Models;

/// <summary>
/// Date time information
/// </summary>
public record DateTimeInfo
{
    [JsonPropertyName("Now")]
    public string? Now { get; init; }

    [JsonPropertyName("UtcNow")]
    public string? UtcNow { get; init; }
}
