using System.Text.Json.Serialization;

namespace HVO.NinaClient.Models;

public record GlobalTriggersResponse
{
    [JsonPropertyName("GlobalTriggers")]
    public List<object>? GlobalTriggers { get; init; }
}
