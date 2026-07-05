using System.Diagnostics.Metrics;
using HVO.Edge.Contracts;

namespace HVO.Gateway.SolarAssistant.Telemetry;

public sealed class SolarAssistantTelemetry : IDisposable
{
    private readonly Meter _meter;

    private volatile int _outboxQueueDepth;

    public readonly Counter<long> SnapshotPollCount;
    public readonly Histogram<double> SnapshotPollDurationMs;
    public readonly Counter<long> OutboxRecordsForwarded;
    public readonly Histogram<double> OutboxForwardLatencyMs;

    public SolarAssistantTelemetry()
    {
        _meter = new Meter("hvo.solarassistant", "1.0.0");

        SnapshotPollCount = _meter.CreateCounter<long>(
            "solarassistant.snapshot.poll.count", "polls",
            "Number of SolarAssistant REST snapshot poll attempts, tagged by result");

        SnapshotPollDurationMs = _meter.CreateHistogram<double>(
            "solarassistant.snapshot.poll.duration_ms", "ms",
            "Duration of each SolarAssistant REST snapshot poll");

        OutboxRecordsForwarded = _meter.CreateCounter<long>(
            GatewayTelemetryConventions.MetricNames.OutboxForwardSuccess, "records",
            "Number of outbox records successfully forwarded to the website");

        OutboxForwardLatencyMs = _meter.CreateHistogram<double>(
            "solarassistant.outbox.forward_latency_ms", "ms",
            "Round-trip latency of outbox HTTP forward requests");

        _meter.CreateObservableGauge(
            GatewayTelemetryConventions.MetricNames.OutboxDepth, () => _outboxQueueDepth, "records",
            "Number of pending records in the outbox");
    }

    public void SetOutboxQueueDepth(int depth) => _outboxQueueDepth = depth;

    public void Dispose() => _meter.Dispose();
}
