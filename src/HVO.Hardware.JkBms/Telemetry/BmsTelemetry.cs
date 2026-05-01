using System.Diagnostics.Metrics;

namespace HVO.Hardware.JkBms.Telemetry;

/// <summary>
/// Custom metrics for the JK BMS hardware driver.
/// Registered as a singleton and injected into workers.
/// The "hvo.jkbms" meter name must be listed in AddOpenTelemetryExport AdditionalMeterNames.
/// </summary>
public sealed class BmsTelemetry : IDisposable
{
    private readonly Meter _meter;

    // Volatile ensures the observable gauge callback always sees the latest value
    // without needing a lock (single-writer from the outbox sweep loop).
    private volatile int _outboxQueueDepth;

    public readonly Counter<long> DevicePollCount;
    public readonly Histogram<double> DevicePollDurationMs;
    public readonly Counter<long> OutboxRecordsForwarded;
    public readonly Histogram<double> OutboxForwardLatencyMs;

    public BmsTelemetry()
    {
        _meter = new Meter("hvo.jkbms", "1.0.0");

        DevicePollCount = _meter.CreateCounter<long>(
            "bms.device.poll.count", "polls",
            "Number of JK BMS device poll attempts, tagged by device alias and result");

        DevicePollDurationMs = _meter.CreateHistogram<double>(
            "bms.device.poll.duration_ms", "ms",
            "Duration of each JK BMS device poll exchange, tagged by device alias");

        OutboxRecordsForwarded = _meter.CreateCounter<long>(
            "bms.outbox.records_forwarded", "records",
            "Number of outbox records successfully forwarded to the website");

        OutboxForwardLatencyMs = _meter.CreateHistogram<double>(
            "bms.outbox.forward_latency_ms", "ms",
            "Round-trip latency of outbox HTTP forward requests");

        _meter.CreateObservableGauge(
            "bms.outbox.queue_depth", () => _outboxQueueDepth, "records",
            "Number of pending records in the outbox");
    }

    public void SetOutboxQueueDepth(int depth) => _outboxQueueDepth = depth;

    public void Dispose() => _meter.Dispose();
}
