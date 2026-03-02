using System.Text.Json.Serialization;

namespace HVO.NinaClient.Models;

/// <summary>
/// Represents a sequence item that can be either an instruction or container
/// </summary>
public record SequenceResponse
{
    [JsonPropertyName("Conditions")]
    public List<object>? Conditions { get; init; }

    [JsonPropertyName("Items")]
    public List<object>? Items { get; init; }

    [JsonPropertyName("Triggers")]
    public List<object>? Triggers { get; init; }

    [JsonPropertyName("Status")]
    public string? Status { get; init; }

    [JsonPropertyName("Name")]
    public string? Name { get; init; }

    [JsonPropertyName("GlobalTriggers")]
    public List<object>? GlobalTriggers { get; init; }
}
