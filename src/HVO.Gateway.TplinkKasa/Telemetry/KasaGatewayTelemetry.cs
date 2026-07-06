using System.Diagnostics.Metrics;
using HVO.Edge.Contracts;

namespace HVO.Gateway.TplinkKasa.Telemetry;

public sealed class KasaGatewayTelemetry : IDisposable
{
    private readonly Meter meter;

    private volatile int _outboxQueueDepth;

    public Counter<long> DevicePollCount { get; }
    public Histogram<double> DevicePollDurationMs { get; }
    public Counter<long> DevicePollSkippedCount { get; }
    public Counter<long> OutboxRecordsForwarded { get; }
    public Histogram<double> OutboxForwardLatencyMs { get; }

    public KasaGatewayTelemetry()
    {
        meter = new Meter("hvo.tplinkkasa", "1.0.0");

        DevicePollCount = meter.CreateCounter<long>(
            "kasa.device.poll.count",
            "polls",
            "Number of TP-Link/Kasa device poll attempts, tagged by device and result.");

        DevicePollDurationMs = meter.CreateHistogram<double>(
            "kasa.device.poll.duration_ms",
            "ms",
            "Duration of each TP-Link/Kasa device poll attempt, tagged by device and result.");

        DevicePollSkippedCount = meter.CreateCounter<long>(
            GatewayTelemetryConventions.MetricNames.DevicePollFailure,
            "ticks",
            "Number of TP-Link/Kasa poll ticks skipped because a previous poll was still running.");

        OutboxRecordsForwarded = meter.CreateCounter<long>(
            GatewayTelemetryConventions.MetricNames.OutboxForwardSuccess, "records",
            "Number of outbox records successfully forwarded to the website");

        OutboxForwardLatencyMs = meter.CreateHistogram<double>(
            "kasa.outbox.forward_latency_ms", "ms",
            "Round-trip latency of outbox HTTP forward requests");

        meter.CreateObservableGauge(
            GatewayTelemetryConventions.MetricNames.OutboxDepth, () => _outboxQueueDepth, "records",
            "Number of pending records in the outbox");
    }

    public void SetOutboxQueueDepth(int depth) => _outboxQueueDepth = depth;

    public void Dispose() => meter.Dispose();
}
