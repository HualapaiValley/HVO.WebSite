using System.Text.Json.Serialization;

namespace HVO.NinaClient.Models;

/// <summary>
/// Response model for available sequences list
/// </summary>
public record AvailableSequencesResponse
{
    [JsonPropertyName("Response")]
    public List<SequenceListItem>? Response { get; init; }

    [JsonPropertyName("Error")]
    public string? Error { get; init; }

    [JsonPropertyName("StatusCode")]
    public int StatusCode { get; init; }

    [JsonPropertyName("Success")]
    public bool Success { get; init; }

    [JsonPropertyName("Type")]
    public string? Type { get; init; }
}

/// <summary>
/// Represents an available sequence in the list
/// </summary>
public record SequenceListItem
{
    [JsonPropertyName("Event")]
    public string? Event { get; init; }

    [JsonPropertyName("Time")]
    public string? Time { get; init; }
}
