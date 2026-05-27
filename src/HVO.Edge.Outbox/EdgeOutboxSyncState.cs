namespace HVO.Edge.Outbox;

public enum EdgeOutboxSyncState
{
    Idle,
    Pending,
    Sending,
    Healthy,
    Degraded,
    Failing
}
