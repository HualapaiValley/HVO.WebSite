namespace HVO.Edge.Contracts;

public sealed class GatewayStatusPayload
{
    public string SourceId { get; init; } = string.Empty;

    public string SourceSystem { get; init; } = string.Empty;

    public string? DeviceId { get; init; }

    public DateTime RecordedAtUtc { get; init; }

    public GatewayIdentity Identity { get; init; } = new(
        GatewayId: string.Empty,
        DisplayName: string.Empty,
        Domain: GatewayDomain.Unknown,
        SourceId: string.Empty);

    public GatewayHealthSnapshot Health { get; init; } = new(
        State: GatewayHealthState.Unknown,
        EvaluatedAtUtc: default,
        Alerts: [],
        SourceFreshness: GatewaySampleState.Unknown);

    public GatewayRuntimeSignal Rest { get; init; } = new(GatewaySampleState.Unknown);

    public GatewayRuntimeSignal? Mqtt { get; init; }

    public GatewayOutboxStatus Outbox { get; init; } = new(PendingCount: 0, FailedCount: 0);

    public int? RestMetricCount { get; init; }

    public int? MqttEntityCount { get; init; }

    public int? MqttStateTopicCount { get; init; }

    public int? MqttCommandTopicCount { get; init; }
}

public sealed record GatewayRuntimeSignal(
    GatewaySampleState State,
    DateTime? LastObservedAtUtc = null,
    string? LastError = null,
    string? Detail = null);

public sealed record GatewayOutboxStatus(
    int PendingCount,
    int FailedCount,
    DateTime? LastSentAtUtc = null,
    int LastBatchCount = 0,
    string? LastError = null);
