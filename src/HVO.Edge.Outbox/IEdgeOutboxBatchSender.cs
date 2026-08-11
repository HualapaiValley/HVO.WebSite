namespace HVO.Edge.Outbox;

public interface IEdgeOutboxBatchSender
{
    Task<IReadOnlyList<EdgeOutboxSendOutcome>> SendAsync(
        IReadOnlyList<EdgeOutboxRecord> records,
        CancellationToken cancellationToken);
}

public enum EdgeOutboxSendStatus
{
    Sent,
    TransientFailure,
    PermanentFailure,
}

public sealed record EdgeOutboxSendOutcome(
    long RecordId,
    EdgeOutboxSendStatus Status,
    string? Error = null);
