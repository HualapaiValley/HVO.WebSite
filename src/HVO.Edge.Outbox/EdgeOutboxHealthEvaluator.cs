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
            throw new ArgumentOutOfRangeException(nameof(observation), observation.PendingCount, "Pending count cannot be negative.");

        if (observation.FailedCount < 0)
            throw new ArgumentOutOfRangeException(nameof(observation), observation.FailedCount, "Failed count cannot be negative.");

        var syncState = GetSyncState(observation);
        var historicalFailureState = GetHistoricalFailureState(observation, options);
        var alerts = BuildAlerts(observation, options, syncState, historicalFailureState);

        return new EdgeOutboxHealthEvaluation(
            GetHealthState(alerts),
            syncState,
            historicalFailureState,
            alerts);
    }

    private static EdgeOutboxSyncState GetSyncState(EdgeOutboxObservation observation)
    {
        if (observation.PendingCount > 0 && !string.IsNullOrWhiteSpace(observation.LastError))
            return EdgeOutboxSyncState.Failing;

        if (observation.PendingCount > 0)
            return EdgeOutboxSyncState.Pending;

        if (observation.FailedCount > 0)
            return EdgeOutboxSyncState.Degraded;

        return observation.LastSentAtUtc.HasValue
            ? EdgeOutboxSyncState.Healthy
            : EdgeOutboxSyncState.Idle;
    }

    private static EdgeOutboxHistoricalFailureState GetHistoricalFailureState(
        EdgeOutboxObservation observation,
        EdgeOutboxHealthOptions options)
    {
        if (observation.FailedCount <= 0)
            return EdgeOutboxHistoricalFailureState.None;

        return options.FailedCriticalCount > 0 && observation.FailedCount >= options.FailedCriticalCount
            ? EdgeOutboxHistoricalFailureState.OverThreshold
            : EdgeOutboxHistoricalFailureState.Present;
    }

    private static IReadOnlyList<GatewayHealthAlert> BuildAlerts(
        EdgeOutboxObservation observation,
        EdgeOutboxHealthOptions options,
        EdgeOutboxSyncState syncState,
        EdgeOutboxHistoricalFailureState historicalFailureState)
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

        if (historicalFailureState != EdgeOutboxHistoricalFailureState.None)
        {
            alerts.Add(new GatewayHealthAlert(
                historicalFailureState == EdgeOutboxHistoricalFailureState.OverThreshold
                    ? "outbox-historical-failures-over-threshold"
                    : "outbox-historical-failures",
                GatewayAlertSeverity.Warning,
                $"{observation.FailedCount} historical outbox record(s) failed."));
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
