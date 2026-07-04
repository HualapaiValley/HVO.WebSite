using System.Text.Json.Serialization;

namespace HVO.Edge.Outbox;

public sealed class OutboxSettingsUpdate
{
    [JsonPropertyName("batchSize")]
    public int? BatchSize { get; init; }

    [JsonPropertyName("sweepIntervalSeconds")]
    public int? SweepIntervalSeconds { get; init; }

    [JsonPropertyName("reset")]
    public bool? Reset { get; init; }
}

public sealed class OutboxSettingsResponse
{
    [JsonPropertyName("batchSize")]
    public int BatchSize { get; }

    [JsonPropertyName("sweepIntervalSeconds")]
    public int SweepIntervalSeconds { get; }

    [JsonPropertyName("isOverride")]
    public bool IsOverride { get; }

    public OutboxSettingsResponse(int batchSize, int sweepIntervalSeconds, bool isOverride)
    {
        BatchSize = batchSize;
        SweepIntervalSeconds = sweepIntervalSeconds;
        IsOverride = isOverride;
    }
}
