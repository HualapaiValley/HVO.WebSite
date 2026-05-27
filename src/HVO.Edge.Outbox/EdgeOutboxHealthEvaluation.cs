using HVO.Edge.Contracts;

namespace HVO.Edge.Outbox;

public sealed record EdgeOutboxHealthEvaluation(
    GatewayHealthState HealthState,
    EdgeOutboxSyncState CurrentSyncState,
    EdgeOutboxHistoricalFailureState HistoricalFailureState,
    IReadOnlyList<GatewayHealthAlert> Alerts);
