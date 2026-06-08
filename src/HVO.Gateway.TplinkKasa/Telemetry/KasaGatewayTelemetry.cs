using System.Diagnostics.Metrics;

namespace HVO.Gateway.TplinkKasa.Telemetry;

public sealed class KasaGatewayTelemetry : IDisposable
{
    private readonly Meter meter = new("HVO.Gateway.TplinkKasa");

    public Counter<long> DevicePollCount { get; }

    public Histogram<double> DevicePollDurationMs { get; }

    public Counter<long> DevicePollSkippedCount { get; }

    public KasaGatewayTelemetry()
    {
        DevicePollCount = meter.CreateCounter<long>(
            "kasa.device.poll.count",
            "polls",
            "Number of TP-Link/Kasa device poll attempts, tagged by device and result.");

        DevicePollDurationMs = meter.CreateHistogram<double>(
            "kasa.device.poll.duration_ms",
            "ms",
            "Duration of each TP-Link/Kasa device poll attempt, tagged by device and result.");

        DevicePollSkippedCount = meter.CreateCounter<long>(
            "kasa.device.poll.skipped.count",
            "ticks",
            "Number of TP-Link/Kasa poll ticks skipped because a previous poll was still running.");
    }

    public void Dispose() => meter.Dispose();
}