using System.Diagnostics.Metrics;
using HVO.Edge.Contracts;

namespace HVO.Hardware.DavisVantagePro2.Telemetry;

/// <summary>
/// Custom metrics for the Davis hardware driver.
/// Registered as a singleton and injected into workers.
/// The "hvo.davis" meter name must be listed in AddOpenTelemetryExport AdditionalMeterNames.
/// </summary>
public sealed class DavisTelemetry : IDisposable
{
    private readonly Meter _meter;

    // Volatile ensures the observable gauge callback always sees the latest value
    // without needing a lock (single-writer from the outbox sweep loop).
    private volatile int _outboxQueueDepth;

    public readonly Counter<long> ConsolePollCount;
    public readonly Counter<long> ConsoleReconnectCount;
    public readonly Counter<long> OutboxRecordsForwarded;
    public readonly Histogram<double> OutboxForwardLatencyMs;
    public readonly Counter<long> OutboxForwardFailureCount;

    public DavisTelemetry()
    {
        _meter = new Meter("hvo.davis", "1.0.0");

        ConsolePollCount = _meter.CreateCounter<long>(
            "davis.console.poll.count", "polls",
            "Number of LOOP2 packets received from the Davis console");

        ConsoleReconnectCount = _meter.CreateCounter<long>(
            "davis.console.reconnect.count", "reconnects",
            "Number of Davis console reconnect attempts");

        OutboxRecordsForwarded = _meter.CreateCounter<long>(
            GatewayTelemetryConventions.MetricNames.OutboxForwardSuccess, "records",
            "Number of outbox records successfully forwarded to the website");

        OutboxForwardLatencyMs = _meter.CreateHistogram<double>(
            "davis.outbox.forward_latency_ms", "ms",
            "Round-trip latency of outbox HTTP batch forward requests");

        OutboxForwardFailureCount = _meter.CreateCounter<long>(
            GatewayTelemetryConventions.MetricNames.OutboxForwardFailure, "records",
            "Number of outbox records that failed to forward");

        _meter.CreateObservableGauge(
            GatewayTelemetryConventions.MetricNames.OutboxDepth, () => _outboxQueueDepth, "records",
            "Number of pending records in the outbox");
    }

    public void SetOutboxQueueDepth(int depth) => _outboxQueueDepth = depth;

    public void Dispose() => _meter.Dispose();
}
