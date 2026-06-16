namespace HVO.Edge.Outbox;

public enum EdgeOutboxFailureKind
{
    None,
    Permanent,
    RetryExhausted,
}
