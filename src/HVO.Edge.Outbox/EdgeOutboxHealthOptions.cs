namespace HVO.Edge.Outbox;

public sealed record EdgeOutboxHealthOptions(
    int PendingWarningCount,
    int FailedCriticalCount)
{
    public static EdgeOutboxHealthOptions Default { get; } = new(
        PendingWarningCount: 10,
        FailedCriticalCount: 1);
}
