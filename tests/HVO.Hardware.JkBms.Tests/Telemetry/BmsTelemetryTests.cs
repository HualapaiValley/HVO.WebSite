using System.Diagnostics.Metrics;
using FluentAssertions;
using HVO.Edge.Contracts;
using HVO.Edge.Hosting.Telemetry;
using HVO.Hardware.JkBms.Telemetry;

namespace HVO.Hardware.JkBms.Tests.Telemetry;

[TestClass]
public sealed class BmsTelemetryTests
{
    [TestMethod]
    public void RecordPoll_EmitsOneCanonicalAndOneCompatibilityMeasurement()
    {
        var names = new List<string>();
        using var listener = Listen(names, GatewayTelemetryConventions.MeterName, "hvo.jkbms");
        using var telemetry = new BmsTelemetry();

        telemetry.RecordPoll("battery-1", true, 0.1);

        names.Count(name => name == GatewayTelemetryConventions.MetricNames.DevicePollAttempt).Should().Be(1);
        names.Count(name => name == "bms.device.poll.count").Should().Be(1);
    }

    [TestMethod]
    public void Diagnostics_ExactlyMatchPublishedCanonicalAndCompatibilityInstruments()
    {
        var published = new HashSet<string>();
        using var listener = new MeterListener { InstrumentPublished = (instrument, _) =>
        {
            if (instrument.Meter.Name is GatewayTelemetryConventions.MeterName or "hvo.jkbms") published.Add(instrument.Name);
        }};
        listener.Start();
        using var telemetry = new BmsTelemetry();

        published.Should().BeEquivalentTo(
            GatewayTelemetry.CreateDiagnostics("hvo-jkbms", [.. BmsTelemetry.CompatibilityMetricNames]).MetricNames);
    }

    [TestMethod]
    public void RecordForwardBatch_EmitsCompatibilityAndCanonicalLatencyOnce()
    {
        var durations = new List<string>();
        using var listener = new MeterListener { InstrumentPublished = (instrument, current) =>
        {
            if (instrument.Meter.Name is GatewayTelemetryConventions.MeterName or "hvo.jkbms")
                current.EnableMeasurementEvents(instrument);
        }};
        listener.SetMeasurementEventCallback<double>((instrument, _, _, _) => durations.Add(instrument.Name));
        listener.Start();
        using var telemetry = new BmsTelemetry();

        telemetry.RecordForwardBatch(1, 1, 0.1);

        durations.Count(name => name == GatewayTelemetryConventions.MetricNames.OutboxForwardDuration).Should().Be(1);
        durations.Count(name => name == BmsTelemetry.OutboxLatencyMetricName).Should().Be(1);
    }

    private static MeterListener Listen(List<string> names, params string[] meters)
    {
        var listener = new MeterListener { InstrumentPublished = (instrument, current) =>
        {
            if (meters.Contains(instrument.Meter.Name)) current.EnableMeasurementEvents(instrument);
        }};
        listener.SetMeasurementEventCallback<long>((instrument, _, _, _) => names.Add(instrument.Name));
        listener.Start();
        return listener;
    }
}
