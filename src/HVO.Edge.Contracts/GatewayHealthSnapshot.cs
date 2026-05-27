namespace HVO.Edge.Contracts;

public sealed record GatewayHealthSnapshot(
    GatewayHealthState State,
    DateTime EvaluatedAtUtc,
    IReadOnlyList<GatewayHealthAlert> Alerts,
    GatewaySampleState SourceFreshness,
    string? OutboxState = null,
    string? ApiSyncState = null);
