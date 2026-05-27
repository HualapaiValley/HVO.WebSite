using FluentAssertions;
using HVO.Edge.Contracts;
using HVO.Edge.Outbox;

namespace HVO.Edge.Outbox.Tests;

[TestClass]
public sealed class EdgeOutboxHealthEvaluatorTests
{
    [TestMethod]
    public void Evaluate_returns_healthy_when_idle_without_backlog_or_failures()
    {
        var result = EdgeOutboxHealthEvaluator.Evaluate(new EdgeOutboxObservation(0, 0));

        result.HealthState.Should().Be(GatewayHealthState.Healthy);
        result.CurrentSyncState.Should().Be(EdgeOutboxSyncState.Idle);
        result.HistoricalFailureState.Should().Be(EdgeOutboxHistoricalFailureState.None);
        result.Alerts.Should().BeEmpty();
    }

    [TestMethod]
    public void Evaluate_returns_healthy_when_recent_send_succeeded_without_backlog_or_failures()
    {
        var result = EdgeOutboxHealthEvaluator.Evaluate(new EdgeOutboxObservation(
            PendingCount: 0,
            FailedCount: 0,
            LastSentAtUtc: DateTime.UtcNow,
            LastBatchCount: 3));

        result.HealthState.Should().Be(GatewayHealthState.Healthy);
        result.CurrentSyncState.Should().Be(EdgeOutboxSyncState.Healthy);
        result.Alerts.Should().BeEmpty();
    }

    [TestMethod]
    public void Evaluate_warns_for_pending_backlog_over_threshold()
    {
        var result = EdgeOutboxHealthEvaluator.Evaluate(
            new EdgeOutboxObservation(PendingCount: 11, FailedCount: 0),
            new EdgeOutboxHealthOptions(PendingWarningCount: 10, FailedCriticalCount: 1));

        result.HealthState.Should().Be(GatewayHealthState.Warning);
        result.CurrentSyncState.Should().Be(EdgeOutboxSyncState.Pending);
        result.Alerts.Should().ContainSingle(alert => alert.Code == "outbox-pending-backlog");
    }

    [TestMethod]
    public void Evaluate_marks_current_forwarding_failure_as_critical()
    {
        var result = EdgeOutboxHealthEvaluator.Evaluate(new EdgeOutboxObservation(
            PendingCount: 1,
            FailedCount: 0,
            LastError: "HTTP 401"));

        result.HealthState.Should().Be(GatewayHealthState.Critical);
        result.CurrentSyncState.Should().Be(EdgeOutboxSyncState.Failing);
        result.Alerts.Should().ContainSingle(alert => alert.Code == "outbox-current-sync-failing" && alert.Severity == GatewayAlertSeverity.Critical);
    }

    [TestMethod]
    public void Evaluate_keeps_historical_failures_warning_when_current_sync_is_not_failing()
    {
        var result = EdgeOutboxHealthEvaluator.Evaluate(new EdgeOutboxObservation(
            PendingCount: 0,
            FailedCount: 71,
            LastSentAtUtc: DateTime.UtcNow,
            LastBatchCount: 1));

        result.HealthState.Should().Be(GatewayHealthState.Warning);
        result.CurrentSyncState.Should().Be(EdgeOutboxSyncState.Degraded);
        result.HistoricalFailureState.Should().Be(EdgeOutboxHistoricalFailureState.OverThreshold);
        result.Alerts.Should().OnlyContain(alert => alert.Severity == GatewayAlertSeverity.Warning);
    }

    [TestMethod]
    public void Evaluate_allows_failed_threshold_to_disable_over_threshold_classification()
    {
        var result = EdgeOutboxHealthEvaluator.Evaluate(
            new EdgeOutboxObservation(PendingCount: 0, FailedCount: 3),
            new EdgeOutboxHealthOptions(PendingWarningCount: 10, FailedCriticalCount: 0));

        result.HealthState.Should().Be(GatewayHealthState.Warning);
        result.HistoricalFailureState.Should().Be(EdgeOutboxHistoricalFailureState.Present);
    }

    [TestMethod]
    public void Evaluate_rejects_negative_counts()
    {
        var act = () => EdgeOutboxHealthEvaluator.Evaluate(new EdgeOutboxObservation(-1, 0));

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
