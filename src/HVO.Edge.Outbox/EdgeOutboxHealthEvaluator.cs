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

        var unclassifiedFailedCount = GetUnclassifiedFailedCount(observation);
        var syncState = GetSyncState(observation, unclassifiedFailedCount);
        var historicalFailureState = GetHistoricalFailureState(observation, options, unclassifiedFailedCount);
        var alerts = BuildAlerts(observation, options, syncState, unclassifiedFailedCount);

        return new EdgeOutboxHealthEvaluation(
            GetHealthState(alerts),
            syncState,
            historicalFailureState,
            alerts);
    }

    private static EdgeOutboxSyncState GetSyncState(EdgeOutboxObservation observation, int unclassifiedFailedCount)
    {
        if (!string.IsNullOrWhiteSpace(observation.LastError))
            return EdgeOutboxSyncState.Failing;

        if (observation.PendingCount > 0)
            return EdgeOutboxSyncState.Pending;

        if (observation.PermanentFailedCount > 0
            || observation.RetryExhaustedCount > 0
            || unclassifiedFailedCount > 0)
            return EdgeOutboxSyncState.Degraded;

        return observation.LastSentAtUtc.HasValue
            ? EdgeOutboxSyncState.Healthy
            : EdgeOutboxSyncState.Idle;
    }

    private static EdgeOutboxHistoricalFailureState GetHistoricalFailureState(
        EdgeOutboxObservation observation,
        EdgeOutboxHealthOptions options,
        int unclassifiedFailedCount)
    {
        var failedCount = observation.PermanentFailedCount + unclassifiedFailedCount;
        if (failedCount <= 0)
            return EdgeOutboxHistoricalFailureState.None;

        return options.FailedCriticalCount > 0 && failedCount >= options.FailedCriticalCount
            ? EdgeOutboxHistoricalFailureState.OverThreshold
            : EdgeOutboxHistoricalFailureState.Present;
    }

    private static IReadOnlyList<GatewayHealthAlert> BuildAlerts(
        EdgeOutboxObservation observation,
        EdgeOutboxHealthOptions options,
        EdgeOutboxSyncState syncState,
        int unclassifiedFailedCount)
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

        if (unclassifiedFailedCount > 0)
        {
            alerts.Add(new GatewayHealthAlert(
                "outbox-historical-failures",
                GatewayAlertSeverity.Warning,
                $"{unclassifiedFailedCount} outbox record(s) have unclassified failures."));
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

    private static int GetUnclassifiedFailedCount(EdgeOutboxObservation observation)
    {
        var classifiedFailedCount = observation.PermanentFailedCount + observation.RetryExhaustedCount;
        return Math.Max(0, observation.FailedCount - classifiedFailedCount);
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
