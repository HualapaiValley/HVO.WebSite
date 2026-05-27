namespace HVO.Edge.Outbox;

/// <summary>Thresholds used by the shared edge outbox health evaluator.</summary>
/// <param name="PendingWarningCount">Warn when pending records exceed this count.</param>
/// <param name="FailedCriticalCount">Legacy threshold name for classifying historical failed rows as over-threshold; historical failures produce warning/degraded health, not critical health.</param>
public sealed record EdgeOutboxHealthOptions(
    int PendingWarningCount,
    int FailedCriticalCount)
{
    public static EdgeOutboxHealthOptions Default { get; } = new(
        PendingWarningCount: 10,
        FailedCriticalCount: 1);
}
