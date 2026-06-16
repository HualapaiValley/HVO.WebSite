namespace HVO.Edge.Outbox;

public sealed record EdgeOutboxObservation(
    int PendingCount,
    int FailedCount,
    DateTime? LastSentAtUtc = null,
    int LastBatchCount = 0,
    string? LastError = null,
    int PermanentFailedCount = 0,
    int RetryExhaustedCount = 0);
