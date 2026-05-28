namespace HVO.Edge.Outbox;

public sealed record EdgeOutboxSnapshot(
    int PendingCount,
    int FailedCount,
    DateTime? LastSentAtUtc,
    int LastBatchCount,
    string? LastError,
    EdgeOutboxSyncState CurrentSyncState,
    EdgeOutboxHistoricalFailureState HistoricalFailureState);
