using System.Diagnostics;
using System.Diagnostics.Metrics;
using HVO.Edge.Contracts;
using HVO.Edge.Hosting.Telemetry;

namespace HVO.Gateway.TplinkKasa.Telemetry;

public sealed class KasaGatewayTelemetry : IDisposable
{
    public const string DevicePollMetricName = "kasa.device.poll.count";
    public const string DevicePollDurationMetricName = "kasa.device.poll.duration_ms";
    public const string OutboxLatencyMetricName = "kasa.outbox.forward_latency_ms";
    public static IReadOnlyList<string> CompatibilityMetricNames { get; } =
        [DevicePollMetricName, DevicePollDurationMetricName, OutboxLatencyMetricName];

    private readonly Meter meter;
    private readonly GatewayTelemetry common;


    public Counter<long> DevicePollCount { get; }
    public Histogram<double> DevicePollDurationMs { get; }
    public Histogram<double> OutboxForwardLatencyMs { get; }

    public KasaGatewayTelemetry(string gatewayId = "hvo-tplink-kasa")
    {
        common = new(new(gatewayId, "smart-plug"));
        meter = new Meter("hvo.tplinkkasa", "1.0.0");

        DevicePollCount = meter.CreateCounter<long>(
            DevicePollMetricName,
            "polls",
            "Number of TP-Link/Kasa device poll attempts, tagged by device and result.");

        DevicePollDurationMs = meter.CreateHistogram<double>(
            DevicePollDurationMetricName,
            "ms",
            "Duration of each TP-Link/Kasa device poll attempt, tagged by device and result.");

        OutboxForwardLatencyMs = meter.CreateHistogram<double>(
            OutboxLatencyMetricName, "ms",
            "Round-trip latency of outbox HTTP forward requests");

    }

    public void RecordPoll(string sourceId, string deviceType, bool succeeded, double durationSeconds, string? failureKind = null)
        => RecordPoll(sourceId, deviceType,
            succeeded ? GatewayTelemetryConventions.Results.Success : GatewayTelemetryConventions.Results.Failure,
            durationSeconds, failureKind);

    public void RecordPoll(string sourceId, string deviceType, string result, double durationSeconds, string? failureKind = null)
    {
        var compatibilityResult = result == GatewayTelemetryConventions.Results.Failure ? "failed" : result;
        var tags = new TagList { { "device", sourceId }, { "kind", deviceType }, { "result", compatibilityResult } };
        DevicePollCount.Add(1, tags);
        DevicePollDurationMs.Record(durationSeconds * 1000, tags);
        common.RecordPoll(result, durationSeconds, sourceId, sourceId, deviceType, failureKind);
    }

    public void RecordPollSkipped(string sourceId, string deviceType, string failureKind)
    {
        common.RecordPoll(GatewayTelemetryConventions.Results.Skipped, 0, sourceId, sourceId, deviceType, failureKind);
    }

    public Activity? StartForwardOperation() =>
        common.StartOperation(GatewayTelemetryConventions.OperationNames.OutboxForward, ActivityKind.Client);

    public void RecordForward(long count, bool succeeded, double durationSeconds, string? failureKind = null, Activity? activity = null)
        => RecordForward(count, succeeded, durationSeconds, "hvo.power.reading.v1", failureKind, activity);

    public void RecordForward(long count, bool succeeded, double durationSeconds, string payloadType, string? failureKind, Activity? activity)
    {
        OutboxForwardLatencyMs.Record(durationSeconds * 1000);
        common.RecordForward(count, succeeded, durationSeconds, payloadType, failureKind, activity);
    }

    public void RecordForwardBatch(long successfulCount, long failedCount, double durationSeconds, Activity? activity = null)
    {
        OutboxForwardLatencyMs.Record(durationSeconds * 1000);
        common.RecordForwardBatch(successfulCount, failedCount, durationSeconds, "hvo.power.reading.v1", "validation", activity);
    }

    public void SetOutboxQueueDepth(int depth, int failed = 0)
    {
        common.SetOutboxState(depth, failed);
    }

    public void Dispose()
    {
        common.Dispose();
        meter.Dispose();
    }
}
