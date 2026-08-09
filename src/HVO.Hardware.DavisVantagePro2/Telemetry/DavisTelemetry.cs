using System.Diagnostics;
using System.Diagnostics.Metrics;
using HVO.Edge.Contracts;
using HVO.Edge.Hosting.Telemetry;

namespace HVO.Hardware.DavisVantagePro2.Telemetry;

/// <summary>
/// Custom metrics for the Davis hardware driver.
/// Registered as a singleton and injected into workers.
/// The "hvo.davis" meter name must be listed in AddOpenTelemetryExport AdditionalMeterNames.
/// </summary>
public sealed class DavisTelemetry : IDisposable
{
    public const string ConsolePollMetricName = "davis.console.poll.count";
    public const string ConsoleReconnectMetricName = "davis.console.reconnect.count";
    public const string OutboxLatencyMetricName = "davis.outbox.forward_latency_ms";
    public static IReadOnlyList<string> CompatibilityMetricNames { get; } =
        [ConsolePollMetricName, ConsoleReconnectMetricName, OutboxLatencyMetricName];

    private readonly Meter _meter;
    private readonly GatewayTelemetry _common = new(new("davis", "weather-station"));

    // Volatile ensures the observable gauge callback always sees the latest value
    // without needing a lock (single-writer from the outbox sweep loop).

    public readonly Counter<long> ConsolePollCount;
    public readonly Counter<long> ConsoleReconnectCount;
    public readonly Histogram<double> OutboxForwardLatencyMs;

    public DavisTelemetry()
    {
        _meter = new Meter("hvo.davis", "1.0.0");

        ConsolePollCount = _meter.CreateCounter<long>(
            ConsolePollMetricName, "polls",
            "Number of LOOP2 packets received from the Davis console");

        ConsoleReconnectCount = _meter.CreateCounter<long>(
            ConsoleReconnectMetricName, "reconnects",
            "Number of Davis console reconnect attempts");

        OutboxForwardLatencyMs = _meter.CreateHistogram<double>(
            OutboxLatencyMetricName, "ms",
            "Round-trip latency of outbox HTTP batch forward requests");

    }

    public void RecordPoll(bool succeeded, double durationSeconds, string? failureKind = null)
    {
        _common.RecordPoll(succeeded, durationSeconds, "hvo-davis-01", deviceType: "davis-vantage-pro2", failureKind: failureKind);
    }

    public void RecordReconnect(double durationSeconds, bool succeeded, string? failureKind = null)
    {
        ConsoleReconnectCount.Add(1);
        _common.RecordConnect(succeeded, durationSeconds, "hvo-davis-01", deviceType: "davis-vantage-pro2", failureKind: failureKind, reconnect: true);
    }

    public Activity? StartForwardOperation() =>
        _common.StartOperation(GatewayTelemetryConventions.OperationNames.OutboxForward, ActivityKind.Client);

    public void RecordForward(long count, bool succeeded, double durationSeconds, string? failureKind = null, Activity? activity = null)
    {
        OutboxForwardLatencyMs.Record(durationSeconds * 1000);
        _common.RecordForward(count, succeeded, durationSeconds, "hvo.weather.raw.v1", failureKind, activity);
    }

    public void RecordForwardBatch(long successfulCount, long failedCount, double durationSeconds, Activity? activity = null)
    {
        OutboxForwardLatencyMs.Record(durationSeconds * 1000);
        _common.RecordForwardBatch(successfulCount, failedCount, durationSeconds, "hvo.weather.raw.v1", "validation", activity);
    }

    public void RecordLegacyPacket() => ConsolePollCount.Add(1);

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
