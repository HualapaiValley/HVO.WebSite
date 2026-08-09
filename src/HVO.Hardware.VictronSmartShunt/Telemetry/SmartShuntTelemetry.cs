using System.Diagnostics;
using System.Diagnostics.Metrics;
using HVO.Edge.Contracts;
using HVO.Edge.Hosting.Telemetry;

namespace HVO.Hardware.VictronSmartShunt.Telemetry;

public sealed class SmartShuntTelemetry : IDisposable
{
    public const string SamplePollMetricName = "smartshunt.sample.poll.count";
    public const string SamplePollDurationMetricName = "smartshunt.sample.poll.duration_ms";
    public const string OutboxLatencyMetricName = "smartshunt.outbox.forward_latency_ms";
    public static IReadOnlyList<string> CompatibilityMetricNames { get; } =
        [SamplePollMetricName, SamplePollDurationMetricName, OutboxLatencyMetricName];

    private readonly Meter _meter;
    private readonly GatewayTelemetry _common;
    private readonly string _sourceId;
    private readonly string _deviceId;


    public readonly Counter<long> SamplePollCount;
    public readonly Histogram<double> SamplePollDurationMs;
    public readonly Histogram<double> OutboxForwardLatencyMs;

    public SmartShuntTelemetry(string sourceId = "smartshunt-main", string deviceId = "smartshunt-lifepo4")
    {
        _sourceId = sourceId;
        _deviceId = deviceId;
        _common = new(new("smartshunt", "battery-monitor"));
        _meter = new Meter("hvo.smartshunt", "1.0.0");

        SamplePollCount = _meter.CreateCounter<long>(
            SamplePollMetricName, "samples",
            "Number of SmartShunt sample poll attempts, tagged by result");

        SamplePollDurationMs = _meter.CreateHistogram<double>(
            SamplePollDurationMetricName, "ms",
            "Duration of each SmartShunt sample poll");

        OutboxForwardLatencyMs = _meter.CreateHistogram<double>(
            OutboxLatencyMetricName, "ms",
            "Round-trip latency of outbox HTTP forward requests");

    }

    public void RecordPoll(bool succeeded, double durationSeconds, string? failureKind = null)
    {
        SamplePollCount.Add(1, new KeyValuePair<string, object?>("result", succeeded ? "success" : "failed"));
        SamplePollDurationMs.Record(durationSeconds * 1000);
        _common.RecordPoll(succeeded, durationSeconds, _sourceId, _deviceId, "victron-smartshunt", failureKind);
    }

    public Activity? StartForwardOperation() =>
        _common.StartOperation(GatewayTelemetryConventions.OperationNames.OutboxForward, ActivityKind.Client);

    public void RecordForward(long count, bool succeeded, double durationSeconds, string? failureKind = null, Activity? activity = null)
    {
        OutboxForwardLatencyMs.Record(durationSeconds * 1000);
        _common.RecordForward(count, succeeded, durationSeconds, "hvo.power.reading.v1", failureKind, activity);
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
