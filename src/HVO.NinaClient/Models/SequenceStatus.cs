using System.Text.Json.Serialization;

namespace HVO.NinaClient.Models;

/// <summary>
/// Sequence status information
/// </summary>
public record SequenceStatus
{
    [JsonPropertyName("Id")]
    public string? Id { get; init; }

    [JsonPropertyName("State")]
    public string? State { get; init; }

    [JsonPropertyName("Progress")]
    public double Progress { get; init; }

    [JsonPropertyName("EstimatedRemainingTime")]
    public DateTimeInfo? EstimatedRemainingTime { get; init; }

    [JsonPropertyName("ElapsedTime")]
    public DateTimeInfo? ElapsedTime { get; init; }

    [JsonPropertyName("CurrentTargetName")]
    public string? CurrentTargetName { get; init; }

    [JsonPropertyName("CurrentInstructionName")]
    public string? CurrentInstructionName { get; init; }
}
