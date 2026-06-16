using FluentAssertions;
using HVO.Edge.Contracts;
using HVO.Edge.Outbox;

namespace HVO.Edge.Outbox.Tests;

[TestClass]
public sealed class EdgeOutboxHealthEvaluatorTests
{
    [TestMethod]
    public void Evaluate_ReturnsHealthy_WhenIdleWithoutBacklogOrFailures()
    {
        var result = EdgeOutboxHealthEvaluator.Evaluate(new EdgeOutboxObservation(0, 0));

        result.HealthState.Should().Be(GatewayHealthState.Healthy);
        result.CurrentSyncState.Should().Be(EdgeOutboxSyncState.Idle);
        result.HistoricalFailureState.Should().Be(EdgeOutboxHistoricalFailureState.None);
        result.Alerts.Should().BeEmpty();
    }

    [TestMethod]
    public void Evaluate_ReturnsHealthy_WhenRecentSendSucceededWithoutBacklogOrFailures()
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
    public void Evaluate_WarnsForPendingBacklog_WhenOverThreshold()
    {
        var result = EdgeOutboxHealthEvaluator.Evaluate(
            new EdgeOutboxObservation(PendingCount: 11, FailedCount: 0),
            new EdgeOutboxHealthOptions(PendingWarningCount: 10, FailedCriticalCount: 1));

        result.HealthState.Should().Be(GatewayHealthState.Warning);
        result.CurrentSyncState.Should().Be(EdgeOutboxSyncState.Pending);
        result.Alerts.Should().ContainSingle(alert => alert.Code == "outbox-pending-backlog");
    }

    [TestMethod]
    public void Evaluate_MarksCurrentForwardingFailure_AsCritical()
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
    public void Evaluate_MarksCurrentForwardingFailure_AsCriticalWithoutPendingRows()
    {
        var result = EdgeOutboxHealthEvaluator.Evaluate(new EdgeOutboxObservation(
            PendingCount: 0,
            FailedCount: 1,
            LastError: "website validation rejected payload",
            PermanentFailedCount: 1));

        result.HealthState.Should().Be(GatewayHealthState.Critical);
        result.CurrentSyncState.Should().Be(EdgeOutboxSyncState.Failing);
        result.Alerts.Should().Contain(alert => alert.Code == "outbox-current-sync-failing" && alert.Severity == GatewayAlertSeverity.Critical);
    }

    [TestMethod]
    public void Evaluate_KeepsHistoricalFailuresWarning_WhenCurrentSyncIsNotFailing()
    {
        var result = EdgeOutboxHealthEvaluator.Evaluate(new EdgeOutboxObservation(
            PendingCount: 0,
            FailedCount: 71,
            LastSentAtUtc: DateTime.UtcNow,
            LastBatchCount: 1,
            PermanentFailedCount: 71));

        result.HealthState.Should().Be(GatewayHealthState.Warning);
        result.CurrentSyncState.Should().Be(EdgeOutboxSyncState.Degraded);
        result.HistoricalFailureState.Should().Be(EdgeOutboxHistoricalFailureState.OverThreshold);
        result.Alerts.Should().OnlyContain(alert => alert.Severity == GatewayAlertSeverity.Warning);
    }

    [TestMethod]
    public void Evaluate_AllowsFailedThreshold_ToDisableOverThresholdClassification()
    {
        var result = EdgeOutboxHealthEvaluator.Evaluate(
            new EdgeOutboxObservation(PendingCount: 0, FailedCount: 3, PermanentFailedCount: 3),
            new EdgeOutboxHealthOptions(PendingWarningCount: 10, FailedCriticalCount: 0));

        result.HealthState.Should().Be(GatewayHealthState.Warning);
        result.HistoricalFailureState.Should().Be(EdgeOutboxHistoricalFailureState.Present);
    }

    [TestMethod]
    public void Evaluate_ReturnsDegraded_WhenPermanentFailuresPresent()
    {
        var result = EdgeOutboxHealthEvaluator.Evaluate(new EdgeOutboxObservation(
            PendingCount: 0,
            FailedCount: 3,
            PermanentFailedCount: 3));

        result.HealthState.Should().Be(GatewayHealthState.Warning);
        result.CurrentSyncState.Should().Be(EdgeOutboxSyncState.Degraded);
        result.Alerts.Should().ContainSingle(alert => alert.Code == "outbox-permanent-failures");
    }

    [TestMethod]
    public void Evaluate_ReturnsDegraded_WhenRetryExhausted_NoLastError()
    {
        var result = EdgeOutboxHealthEvaluator.Evaluate(new EdgeOutboxObservation(
            PendingCount: 0,
            FailedCount: 5,
            RetryExhaustedCount: 5));

        result.HealthState.Should().Be(GatewayHealthState.Warning);
        result.CurrentSyncState.Should().Be(EdgeOutboxSyncState.Degraded);
        result.Alerts.Should().ContainSingle(alert => alert.Code == "outbox-retry-exhausted");
    }

    [TestMethod]
    public void Evaluate_RejectsNegativePendingCount()
    {
        var act = () => EdgeOutboxHealthEvaluator.Evaluate(new EdgeOutboxObservation(-1, 0));

        act.Should().Throw<ArgumentOutOfRangeException>()
            .Which.ParamName.Should().Be(nameof(EdgeOutboxObservation.PendingCount));
    }

    [TestMethod]
    public void Evaluate_RejectsNegativeFailedCount()
    {
        var act = () => EdgeOutboxHealthEvaluator.Evaluate(new EdgeOutboxObservation(0, -1));

        act.Should().Throw<ArgumentOutOfRangeException>()
            .Which.ParamName.Should().Be(nameof(EdgeOutboxObservation.FailedCount));
    }
}
