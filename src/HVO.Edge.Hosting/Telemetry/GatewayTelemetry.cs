using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Collections.Concurrent;
using HVO.Edge.Contracts;

namespace HVO.Edge.Hosting.Telemetry;

public sealed class GatewayTelemetry : IDisposable
{
    private readonly GatewayTelemetryIdentity _identity;
    private readonly Meter _meter = new(
        GatewayTelemetryConventions.MeterName,
        GatewayTelemetryConventions.Version);
    private readonly Counter<long> _connectAttempts;
    private readonly Counter<long> _connectFailures;
    private readonly Counter<long> _reconnects;
    private readonly Histogram<double> _connectDuration;
    private readonly Counter<long> _readAttempts;
    private readonly Counter<long> _readFailures;
    private readonly Histogram<double> _readDuration;
    private readonly Counter<long> _pollAttempts;
    private readonly Counter<long> _pollFailures;
    private readonly Histogram<double> _pollDuration;
    private readonly Counter<long> _forwardSuccess;
    private readonly Counter<long> _forwardFailure;
    private readonly Histogram<double> _forwardDuration;
    private readonly Counter<long> _healthEvaluations;
    private readonly Histogram<double> _healthDuration;
    private volatile int _outboxDepth;
    private volatile int _outboxFailed;
    private int _outboxInitialized;
    private readonly ConcurrentDictionary<string, FreshnessState> _freshness = new(StringComparer.Ordinal);

    public GatewayTelemetry(GatewayTelemetryIdentity identity)
    {
        _identity = identity;
        _connectAttempts = Counter(GatewayTelemetryConventions.MetricNames.DeviceConnectAttempt, GatewayTelemetryConventions.Units.Attempt);
        _connectFailures = Counter(GatewayTelemetryConventions.MetricNames.DeviceConnectFailure, GatewayTelemetryConventions.Units.Failure);
        _reconnects = Counter(GatewayTelemetryConventions.MetricNames.DeviceReconnect, GatewayTelemetryConventions.Units.Reconnect);
        _connectDuration = Histogram(GatewayTelemetryConventions.MetricNames.DeviceConnectDuration);
        _readAttempts = Counter(GatewayTelemetryConventions.MetricNames.DeviceReadAttempt, GatewayTelemetryConventions.Units.Attempt);
        _readFailures = Counter(GatewayTelemetryConventions.MetricNames.DeviceReadFailure, GatewayTelemetryConventions.Units.Failure);
        _readDuration = Histogram(GatewayTelemetryConventions.MetricNames.DeviceReadDuration);
        _pollAttempts = Counter(GatewayTelemetryConventions.MetricNames.DevicePollAttempt, GatewayTelemetryConventions.Units.Attempt);
        _pollFailures = Counter(GatewayTelemetryConventions.MetricNames.DevicePollFailure, GatewayTelemetryConventions.Units.Failure);
        _pollDuration = Histogram(GatewayTelemetryConventions.MetricNames.DevicePollDuration);
        _forwardSuccess = Counter(GatewayTelemetryConventions.MetricNames.OutboxForwardSuccess, GatewayTelemetryConventions.Units.Record);
        _forwardFailure = Counter(GatewayTelemetryConventions.MetricNames.OutboxForwardFailure, GatewayTelemetryConventions.Units.Record);
        _forwardDuration = Histogram(GatewayTelemetryConventions.MetricNames.OutboxForwardDuration);
        _healthEvaluations = Counter(GatewayTelemetryConventions.MetricNames.HealthEvaluation, GatewayTelemetryConventions.Units.Evaluation);
        _healthDuration = Histogram(GatewayTelemetryConventions.MetricNames.HealthEvaluationDuration);

        _meter.CreateObservableGauge(GatewayTelemetryConventions.MetricNames.OutboxDepth,
            ObserveOutboxDepth, GatewayTelemetryConventions.Units.Record);
        _meter.CreateObservableGauge(GatewayTelemetryConventions.MetricNames.OutboxFailed,
            ObserveOutboxFailed, GatewayTelemetryConventions.Units.Record);
        _meter.CreateObservableGauge(GatewayTelemetryConventions.MetricNames.DeviceFreshnessSeconds,
            ObserveFreshness, GatewayTelemetryConventions.Units.Seconds);
    }

    public void RecordConnect(bool succeeded, double durationSeconds, string? sourceId = null, string? deviceId = null, string? deviceType = null, string? failureKind = null, bool reconnect = false)
    {
        var tags = OperationTags(succeeded, sourceId, deviceId, deviceType, failureKind);
        _connectAttempts.Add(1, tags);
        _connectDuration.Record(durationSeconds, tags);
        if (!succeeded) _connectFailures.Add(1, tags);
        if (reconnect) _reconnects.Add(1, tags);
        RecordCompletedOperation(GatewayTelemetryConventions.OperationNames.DeviceConnect, ActivityKind.Client,
            succeeded, durationSeconds, sourceId, deviceId, deviceType, failureKind);
    }

    public void RecordRead(bool succeeded, double durationSeconds, string? sourceId = null, string? deviceId = null, string? deviceType = null, string? failureKind = null)
    {
        var tags = OperationTags(succeeded, sourceId, deviceId, deviceType, failureKind);
        _readAttempts.Add(1, tags);
        _readDuration.Record(durationSeconds, tags);
        if (!succeeded) _readFailures.Add(1, tags);
        RecordCompletedOperation(GatewayTelemetryConventions.OperationNames.DeviceRead, ActivityKind.Client,
            succeeded, durationSeconds, sourceId, deviceId, deviceType, failureKind);
    }

    public void RecordPoll(bool succeeded, double durationSeconds, string? sourceId = null, string? deviceId = null, string? deviceType = null, string? failureKind = null)
        => RecordPoll(
            succeeded ? GatewayTelemetryConventions.Results.Success : GatewayTelemetryConventions.Results.Failure,
            durationSeconds, sourceId, deviceId, deviceType, failureKind);

    public void RecordPoll(string result, double durationSeconds, string? sourceId = null, string? deviceId = null, string? deviceType = null, string? failureKind = null)
    {
        var succeeded = result is GatewayTelemetryConventions.Results.Success or GatewayTelemetryConventions.Results.Degraded;
        var tags = BaseTags();
        tags.Add(GatewayTelemetryConventions.Tags.Result, result);
        if (!string.IsNullOrWhiteSpace(sourceId)) tags.Add(GatewayTelemetryConventions.Tags.SourceId, sourceId);
        if (!string.IsNullOrWhiteSpace(deviceId)) tags.Add(GatewayTelemetryConventions.Tags.DeviceId, deviceId);
        if (!string.IsNullOrWhiteSpace(deviceType)) tags.Add(GatewayTelemetryConventions.Tags.DeviceType, deviceType);
        if (!string.IsNullOrWhiteSpace(failureKind)) tags.Add(GatewayTelemetryConventions.Tags.FailureKind, failureKind);
        _pollAttempts.Add(1, tags);
        _pollDuration.Record(durationSeconds, tags);
        if (result is GatewayTelemetryConventions.Results.Failure or GatewayTelemetryConventions.Results.Skipped)
            _pollFailures.Add(1, tags);
        if (succeeded && !string.IsNullOrWhiteSpace(sourceId))
            _freshness[sourceId] = new FreshnessState(DateTimeOffset.UtcNow, deviceId, deviceType);
        RecordCompletedOperation(GatewayTelemetryConventions.OperationNames.DevicePoll, ActivityKind.Internal,
            succeeded, durationSeconds, sourceId, deviceId, deviceType, failureKind, result);
    }

    public void RecordForward(long count, bool succeeded, double durationSeconds, string? payloadType = null, string? failureKind = null, Activity? activity = null)
    {
        var tags = OperationTags(succeeded, failureKind: failureKind);
        if (!string.IsNullOrWhiteSpace(payloadType)) tags.Add(GatewayTelemetryConventions.Tags.PayloadType, payloadType);
        (succeeded ? _forwardSuccess : _forwardFailure).Add(count, tags);
        _forwardDuration.Record(durationSeconds, tags);
        CompleteOrRecordOperation(activity, GatewayTelemetryConventions.OperationNames.OutboxForward, ActivityKind.Client,
            succeeded, durationSeconds, failureKind: failureKind, payloadType: payloadType);
    }

    public void RecordForwardBatch(long successfulCount, long failedCount, double durationSeconds, string? payloadType = null, string? failureKind = null, Activity? activity = null)
    {
        if (successfulCount > 0)
        {
            var successTags = OperationTags(true);
            if (!string.IsNullOrWhiteSpace(payloadType)) successTags.Add(GatewayTelemetryConventions.Tags.PayloadType, payloadType);
            _forwardSuccess.Add(successfulCount, successTags);
        }

        if (failedCount > 0)
        {
            var failureTags = OperationTags(false, failureKind: failureKind);
            if (!string.IsNullOrWhiteSpace(payloadType)) failureTags.Add(GatewayTelemetryConventions.Tags.PayloadType, payloadType);
            _forwardFailure.Add(failedCount, failureTags);
        }

        var durationTags = BaseTags();
        durationTags.Add(GatewayTelemetryConventions.Tags.Result,
            failedCount == 0 ? GatewayTelemetryConventions.Results.Success :
            successfulCount == 0 ? GatewayTelemetryConventions.Results.Failure : GatewayTelemetryConventions.Results.Degraded);
        if (!string.IsNullOrWhiteSpace(payloadType)) durationTags.Add(GatewayTelemetryConventions.Tags.PayloadType, payloadType);
        if (!string.IsNullOrWhiteSpace(failureKind)) durationTags.Add(GatewayTelemetryConventions.Tags.FailureKind, failureKind);
        _forwardDuration.Record(durationSeconds, durationTags);
        CompleteOrRecordOperation(activity, GatewayTelemetryConventions.OperationNames.OutboxForward, ActivityKind.Client,
            failedCount == 0, durationSeconds, failureKind: failureKind, payloadType: payloadType,
            result: successfulCount > 0 && failedCount > 0 ? GatewayTelemetryConventions.Results.Degraded : null);
    }

    public void RecordHealth(string healthState, double durationSeconds)
    {
        var tags = BaseTags();
        tags.Add(GatewayTelemetryConventions.Tags.HealthState, healthState);
        _healthEvaluations.Add(1, tags);
        _healthDuration.Record(durationSeconds, tags);
        var healthy = healthState.Equals("healthy", StringComparison.OrdinalIgnoreCase)
            || healthState.Equals("ok", StringComparison.OrdinalIgnoreCase)
            || healthState.Equals(GatewayTelemetryConventions.Results.Success, StringComparison.OrdinalIgnoreCase);
        RecordCompletedOperation(GatewayTelemetryConventions.OperationNames.HealthEvaluate, ActivityKind.Internal,
            healthy, durationSeconds, failureKind: healthy ? null : healthState);
    }

    public void SetOutboxState(int pending, int failed = 0)
    {
        _outboxDepth = pending;
        _outboxFailed = failed;
        Volatile.Write(ref _outboxInitialized, 1);
    }

    public Activity? StartOperation(string operationName, ActivityKind kind = ActivityKind.Internal, string? sourceId = null, string? deviceId = null, string? deviceType = null) =>
        HvoActivitySource.StartOperation(operationName, kind, _identity.GatewayId, _identity.GatewayType, sourceId, deviceId, deviceType);

    public static GatewayTelemetryDiagnostics CreateDiagnostics(string defaultServiceName, params string[] compatibilityMetricNames) => new(
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT")),
        Environment.GetEnvironmentVariable("OTEL_SERVICE_NAME") ?? defaultServiceName,
        GatewayTelemetryConventions.MetricNames.All.Concat(compatibilityMetricNames).Distinct().Order().ToArray(),
        [GatewayTelemetryConventions.ActivitySourceName]);

    public void Dispose() => _meter.Dispose();

    private Counter<long> Counter(string name, string unit) => _meter.CreateCounter<long>(name, unit);
    private Histogram<double> Histogram(string name) => _meter.CreateHistogram<double>(name, GatewayTelemetryConventions.Units.Seconds);

    private TagList BaseTags()
    {
        var tags = new TagList
        {
            { GatewayTelemetryConventions.Tags.GatewayId, _identity.GatewayId },
            { GatewayTelemetryConventions.Tags.GatewayType, _identity.GatewayType }
        };
        return tags;
    }

    private TagList OperationTags(bool succeeded, string? sourceId = null, string? deviceId = null, string? deviceType = null, string? failureKind = null)
    {
        var tags = BaseTags();
        tags.Add(GatewayTelemetryConventions.Tags.Result, succeeded ? GatewayTelemetryConventions.Results.Success : GatewayTelemetryConventions.Results.Failure);
        if (!string.IsNullOrWhiteSpace(sourceId)) tags.Add(GatewayTelemetryConventions.Tags.SourceId, sourceId);
        if (!string.IsNullOrWhiteSpace(deviceId)) tags.Add(GatewayTelemetryConventions.Tags.DeviceId, deviceId);
        if (!string.IsNullOrWhiteSpace(deviceType)) tags.Add(GatewayTelemetryConventions.Tags.DeviceType, deviceType);
        if (!string.IsNullOrWhiteSpace(failureKind)) tags.Add(GatewayTelemetryConventions.Tags.FailureKind, failureKind);
        return tags;
    }

    private IEnumerable<Measurement<double>> ObserveFreshness()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var entry in _freshness)
        {
            var tags = BaseTags();
            tags.Add(GatewayTelemetryConventions.Tags.SourceId, entry.Key);
            if (!string.IsNullOrWhiteSpace(entry.Value.DeviceId)) tags.Add(GatewayTelemetryConventions.Tags.DeviceId, entry.Value.DeviceId);
            if (!string.IsNullOrWhiteSpace(entry.Value.DeviceType)) tags.Add(GatewayTelemetryConventions.Tags.DeviceType, entry.Value.DeviceType);
            yield return new Measurement<double>(Math.Max(0, (now - entry.Value.RecordedAt).TotalSeconds), tags);
        }
    }

    private IEnumerable<Measurement<int>> ObserveOutboxDepth()
    {
        if (Volatile.Read(ref _outboxInitialized) != 0)
            yield return new Measurement<int>(_outboxDepth, BaseTags());
    }

    private IEnumerable<Measurement<int>> ObserveOutboxFailed()
    {
        if (Volatile.Read(ref _outboxInitialized) != 0)
            yield return new Measurement<int>(_outboxFailed, BaseTags());
    }

    private void RecordCompletedOperation(
        string operationName,
        ActivityKind kind,
        bool succeeded,
        double durationSeconds,
        string? sourceId = null,
        string? deviceId = null,
        string? deviceType = null,
        string? failureKind = null,
        string? result = null)
    {
        var duration = TimeSpan.FromSeconds(Math.Max(0, durationSeconds));
        using var activity = HvoActivitySource.StartCompletedOperation(
            operationName, kind, duration, _identity.GatewayId, _identity.GatewayType, sourceId, deviceId, deviceType);
        HvoActivitySource.Complete(activity, succeeded, failureKind);
        if (!string.IsNullOrWhiteSpace(result))
            activity?.SetTag(GatewayTelemetryConventions.Tags.Result, result);
    }

    private void CompleteOrRecordOperation(
        Activity? activity,
        string operationName,
        ActivityKind kind,
        bool succeeded,
        double durationSeconds,
        string? failureKind = null,
        string? payloadType = null,
        string? result = null)
    {
        if (activity is null)
        {
            RecordCompletedOperation(operationName, kind, succeeded, durationSeconds, failureKind: failureKind, result: result);
            return;
        }

        if (!string.IsNullOrWhiteSpace(payloadType))
            activity.SetTag(GatewayTelemetryConventions.Tags.PayloadType, payloadType);
        HvoActivitySource.Complete(activity, succeeded, failureKind);
        if (!string.IsNullOrWhiteSpace(result))
            activity.SetTag(GatewayTelemetryConventions.Tags.Result, result);
    }

    private sealed record FreshnessState(DateTimeOffset RecordedAt, string? DeviceId, string? DeviceType);
}
