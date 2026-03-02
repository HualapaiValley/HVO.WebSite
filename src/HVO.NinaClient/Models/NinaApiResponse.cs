using System.Text.Json.Serialization;
using HVO.Core.Results;

namespace HVO.NinaClient.Models;

/// <summary>
/// Base response model for all NINA API responses
/// </summary>
/// <typeparam name="T">The type of the response data</typeparam>
public record NinaApiResponse<T>
{
    [JsonPropertyName("Response")]
    public T? Response { get; init; }

    [JsonPropertyName("Error")]
    public string? Error { get; init; }

    [JsonPropertyName("StatusCode")]
    public int StatusCode { get; init; }

    [JsonPropertyName("Success")]
    public bool Success { get; init; }

    [JsonPropertyName("Type")]
    public string? Type { get; init; }
}
