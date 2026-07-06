using System.Diagnostics.Metrics;
using HVO.Edge.Contracts;

namespace HVO.Hardware.VictronSmartShunt.Telemetry;

public sealed class SmartShuntTelemetry : IDisposable
{
    private readonly Meter _meter;

    private volatile int _outboxQueueDepth;

    public readonly Counter<long> SamplePollCount;
    public readonly Histogram<double> SamplePollDurationMs;
    public readonly Counter<long> OutboxRecordsForwarded;
    public readonly Histogram<double> OutboxForwardLatencyMs;

    public SmartShuntTelemetry()
    {
        _meter = new Meter("hvo.smartshunt", "1.0.0");

        SamplePollCount = _meter.CreateCounter<long>(
            "smartshunt.sample.poll.count", "samples",
            "Number of SmartShunt sample poll attempts, tagged by result");

        SamplePollDurationMs = _meter.CreateHistogram<double>(
            "smartshunt.sample.poll.duration_ms", "ms",
            "Duration of each SmartShunt sample poll");

        OutboxRecordsForwarded = _meter.CreateCounter<long>(
            GatewayTelemetryConventions.MetricNames.OutboxForwardSuccess, "records",
            "Number of outbox records successfully forwarded to the website");

        OutboxForwardLatencyMs = _meter.CreateHistogram<double>(
            "smartshunt.outbox.forward_latency_ms", "ms",
            "Round-trip latency of outbox HTTP forward requests");

        _meter.CreateObservableGauge(
            GatewayTelemetryConventions.MetricNames.OutboxDepth, () => _outboxQueueDepth, "records",
            "Number of pending records in the outbox");
    }

    public void SetOutboxQueueDepth(int depth) => _outboxQueueDepth = depth;

    public void Dispose() => _meter.Dispose();
}
