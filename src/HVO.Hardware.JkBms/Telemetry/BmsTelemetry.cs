using System.Diagnostics;
using System.Diagnostics.Metrics;
using HVO.Edge.Contracts;
using HVO.Edge.Hosting.Telemetry;

namespace HVO.Hardware.JkBms.Telemetry;

/// <summary>
/// Custom metrics for the JK BMS hardware driver.
/// Registered as a singleton and injected into workers.
/// The "hvo.jkbms" meter name must be listed in AddOpenTelemetryExport AdditionalMeterNames.
/// </summary>
public sealed class BmsTelemetry : IDisposable
{
    public const string DevicePollMetricName = "bms.device.poll.count";
    public const string DevicePollDurationMetricName = "bms.device.poll.duration_ms";
    public const string OutboxLatencyMetricName = "bms.outbox.forward_latency_ms";
    public static IReadOnlyList<string> CompatibilityMetricNames { get; } =
        [DevicePollMetricName, DevicePollDurationMetricName, OutboxLatencyMetricName];

    private readonly Meter _meter;
    private readonly GatewayTelemetry _common = new(new("jkbms", "battery-bms"));

    // Volatile ensures the observable gauge callback always sees the latest value
    // without needing a lock (single-writer from the outbox sweep loop).

    public readonly Counter<long> DevicePollCount;
    public readonly Histogram<double> DevicePollDurationMs;
    public readonly Histogram<double> OutboxForwardLatencyMs;

    public BmsTelemetry()
    {
        _meter = new Meter("hvo.jkbms", "1.0.0");

        DevicePollCount = _meter.CreateCounter<long>(
            DevicePollMetricName, "polls",
            "Number of JK BMS device poll attempts, tagged by device alias and result");

        DevicePollDurationMs = _meter.CreateHistogram<double>(
            DevicePollDurationMetricName, "ms",
            "Duration of each JK BMS device poll exchange, tagged by device alias");

        OutboxForwardLatencyMs = _meter.CreateHistogram<double>(
            OutboxLatencyMetricName, "ms",
            "Round-trip latency of outbox HTTP forward requests");

    }

    public void RecordPoll(string deviceId, bool succeeded, double durationSeconds, string? failureKind = null)
    {
        var result = succeeded ? "success" : "error";
        DevicePollCount.Add(1, new KeyValuePair<string, object?>("device", deviceId), new KeyValuePair<string, object?>("result", result));
        DevicePollDurationMs.Record(durationSeconds * 1000, new KeyValuePair<string, object?>("device", deviceId));
        _common.RecordPoll(succeeded, durationSeconds, deviceId, deviceId, "jk-bms", failureKind);
    }

    public Activity? StartForwardOperation() =>
        _common.StartOperation(GatewayTelemetryConventions.OperationNames.OutboxForward, ActivityKind.Client);

    public void RecordForward(long count, bool succeeded, double durationSeconds, string? failureKind = null, Activity? activity = null)
    {
        OutboxForwardLatencyMs.Record(durationSeconds * 1000);
        _common.RecordForward(count, succeeded, durationSeconds, "hvo.bms.reading.v1", failureKind, activity);
    }

    public void RecordForwardBatch(long successfulCount, long failedCount, double durationSeconds, Activity? activity = null)
    {
        OutboxForwardLatencyMs.Record(durationSeconds * 1000);
        _common.RecordForwardBatch(successfulCount, failedCount, durationSeconds, "hvo.bms.reading.v1", "forwarder_failure", activity);
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
