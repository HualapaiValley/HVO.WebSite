using System.Diagnostics;
using System.Diagnostics.Metrics;
using HVO.Edge.Contracts;
using HVO.Edge.Hosting.Telemetry;

namespace HVO.Gateway.SolarAssistant.Telemetry;

public sealed class SolarAssistantTelemetry : IDisposable
{
    public const string SnapshotPollMetricName = "solarassistant.snapshot.poll.count";
    public const string SnapshotPollDurationMetricName = "solarassistant.snapshot.poll.duration_ms";
    public const string OutboxLatencyMetricName = "solarassistant.outbox.forward_latency_ms";
    public static IReadOnlyList<string> CompatibilityMetricNames { get; } =
        [SnapshotPollMetricName, SnapshotPollDurationMetricName, OutboxLatencyMetricName];

    private readonly Meter _meter;
    private readonly GatewayTelemetry _common = new(new("solarassistant", "inverter-aggregator"));


    public readonly Counter<long> SnapshotPollCount;
    public readonly Histogram<double> SnapshotPollDurationMs;
    public readonly Histogram<double> OutboxForwardLatencyMs;

    public SolarAssistantTelemetry()
    {
        _meter = new Meter("hvo.solarassistant", "1.0.0");

        SnapshotPollCount = _meter.CreateCounter<long>(
            SnapshotPollMetricName, "polls",
            "Number of SolarAssistant REST snapshot poll attempts, tagged by result");

        SnapshotPollDurationMs = _meter.CreateHistogram<double>(
            SnapshotPollDurationMetricName, "ms",
            "Duration of each SolarAssistant REST snapshot poll");

        OutboxForwardLatencyMs = _meter.CreateHistogram<double>(
            OutboxLatencyMetricName, "ms",
            "Round-trip latency of outbox HTTP forward requests");

    }

    public void RecordPoll(bool succeeded, double durationSeconds, string? failureKind = null)
    {
        SnapshotPollCount.Add(1, new KeyValuePair<string, object?>("result", succeeded ? "success" : "failed"));
        SnapshotPollDurationMs.Record(durationSeconds * 1000);
        _common.RecordPoll(succeeded, durationSeconds, "solarassistant-total", "total", "solarassistant", failureKind);
    }

    public Activity? StartForwardOperation() =>
        _common.StartOperation(GatewayTelemetryConventions.OperationNames.OutboxForward, ActivityKind.Client);

    public void RecordForward(long count, bool succeeded, double durationSeconds, string? failureKind = null, Activity? activity = null)
        => RecordForward(count, succeeded, durationSeconds, "hvo.power.reading.v1", failureKind, activity);

    public void RecordForward(long count, bool succeeded, double durationSeconds, string payloadType, string? failureKind, Activity? activity)
    {
        OutboxForwardLatencyMs.Record(durationSeconds * 1000);
        _common.RecordForward(count, succeeded, durationSeconds, payloadType, failureKind, activity);
    }

    public void RecordForwardBatch(long successfulCount, long failedCount, double durationSeconds, Activity? activity = null)
    {
        OutboxForwardLatencyMs.Record(durationSeconds * 1000);
        _common.RecordForwardBatch(successfulCount, failedCount, durationSeconds, "hvo.power.reading.v1", "validation", activity);
    }

    public void SetOutboxQueueDepth(int depth, int failed = 0)
    {
        _common.SetOutboxState(depth, failed);
    }

    public void Dispose()
    {
        _common.Dispose();
        _meter.Dispose();
    }
}
