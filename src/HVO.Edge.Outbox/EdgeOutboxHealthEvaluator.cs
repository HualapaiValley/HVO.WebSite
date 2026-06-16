using HVO.Edge.Contracts;

namespace HVO.Edge.Outbox;

public static class EdgeOutboxHealthEvaluator
{
    public static EdgeOutboxHealthEvaluation Evaluate(
        EdgeOutboxObservation observation,
        EdgeOutboxHealthOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(observation);
        options ??= EdgeOutboxHealthOptions.Default;

        if (observation.PendingCount < 0)
            throw new ArgumentOutOfRangeException(nameof(observation.PendingCount), observation.PendingCount, "Pending count cannot be negative.");

        if (observation.FailedCount < 0)
            throw new ArgumentOutOfRangeException(nameof(observation.FailedCount), observation.FailedCount, "Failed count cannot be negative.");

        if (observation.PermanentFailedCount < 0)
            throw new ArgumentOutOfRangeException(nameof(observation.PermanentFailedCount), observation.PermanentFailedCount, "Permanent failed count cannot be negative.");

        if (observation.RetryExhaustedCount < 0)
            throw new ArgumentOutOfRangeException(nameof(observation.RetryExhaustedCount), observation.RetryExhaustedCount, "Retry-exhausted count cannot be negative.");

        var syncState = GetSyncState(observation);
        var historicalFailureState = GetHistoricalFailureState(observation, options);
        var alerts = BuildAlerts(observation, options, syncState);

        return new EdgeOutboxHealthEvaluation(
            GetHealthState(alerts),
            syncState,
            historicalFailureState,
            alerts);
    }

    private static EdgeOutboxSyncState GetSyncState(EdgeOutboxObservation observation)
    {
        if (!string.IsNullOrWhiteSpace(observation.LastError))
            return EdgeOutboxSyncState.Failing;

        if (observation.PendingCount > 0)
            return EdgeOutboxSyncState.Pending;

        if (observation.PermanentFailedCount > 0 || observation.RetryExhaustedCount > 0)
            return EdgeOutboxSyncState.Degraded;

        return observation.LastSentAtUtc.HasValue
            ? EdgeOutboxSyncState.Healthy
            : EdgeOutboxSyncState.Idle;
    }

    private static EdgeOutboxHistoricalFailureState GetHistoricalFailureState(
        EdgeOutboxObservation observation,
        EdgeOutboxHealthOptions options)
    {
        if (observation.PermanentFailedCount <= 0)
            return EdgeOutboxHistoricalFailureState.None;

        return options.FailedCriticalCount > 0 && observation.PermanentFailedCount >= options.FailedCriticalCount
            ? EdgeOutboxHistoricalFailureState.OverThreshold
            : EdgeOutboxHistoricalFailureState.Present;
    }

    private static IReadOnlyList<GatewayHealthAlert> BuildAlerts(
        EdgeOutboxObservation observation,
        EdgeOutboxHealthOptions options,
        EdgeOutboxSyncState syncState)
    {
        var alerts = new List<GatewayHealthAlert>();

        if (syncState == EdgeOutboxSyncState.Failing)
        {
            alerts.Add(new GatewayHealthAlert(
                "outbox-current-sync-failing",
                GatewayAlertSeverity.Critical,
                $"Current outbox forwarding is failing: {observation.LastError}"));
        }

        if (observation.PendingCount > options.PendingWarningCount)
        {
            alerts.Add(new GatewayHealthAlert(
                "outbox-pending-backlog",
                GatewayAlertSeverity.Warning,
                $"{observation.PendingCount} outbox record(s) are pending."));
        }

        if (observation.PermanentFailedCount > 0)
        {
            alerts.Add(new GatewayHealthAlert(
                "outbox-permanent-failures",
                GatewayAlertSeverity.Warning,
                $"{observation.PermanentFailedCount} outbox record(s) have permanent failures."));
        }

        if (observation.RetryExhaustedCount > 0)
        {
            alerts.Add(new GatewayHealthAlert(
                "outbox-retry-exhausted",
                GatewayAlertSeverity.Warning,
                $"{observation.RetryExhaustedCount} outbox record(s) exhausted retries and are queued for requeue."));
        }

        return alerts;
    }

    private static GatewayHealthState GetHealthState(IReadOnlyList<GatewayHealthAlert> alerts)
    {
        if (alerts.Any(static alert => alert.Severity == GatewayAlertSeverity.Critical))
            return GatewayHealthState.Critical;

        return alerts.Any(static alert => alert.Severity == GatewayAlertSeverity.Warning)
            ? GatewayHealthState.Warning
            : GatewayHealthState.Healthy;
    }
}
