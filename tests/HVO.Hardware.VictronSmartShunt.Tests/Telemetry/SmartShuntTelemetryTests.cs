using System.Diagnostics.Metrics;
using FluentAssertions;
using HVO.Edge.Contracts;
using HVO.Edge.Hosting.Telemetry;
using HVO.Hardware.VictronSmartShunt.Telemetry;

namespace HVO.Hardware.VictronSmartShunt.Tests.Telemetry;

[TestClass]
public sealed class SmartShuntTelemetryTests
{
    [TestMethod]
    public void RecordPoll_EmitsOneCanonicalAndOneCompatibilityMeasurement()
    {
        var names = new List<string>();
        using var listener = Listen(names, GatewayTelemetryConventions.MeterName, "hvo.smartshunt");
        using var telemetry = new SmartShuntTelemetry();

        telemetry.RecordPoll(true, 0.1);

        names.Count(name => name == GatewayTelemetryConventions.MetricNames.DevicePollAttempt).Should().Be(1);
        names.Count(name => name == "smartshunt.sample.poll.count").Should().Be(1);
    }

    [TestMethod]
    public void Diagnostics_ExactlyMatchPublishedCanonicalAndCompatibilityInstruments()
    {
        var published = new HashSet<string>();
        using var listener = new MeterListener { InstrumentPublished = (instrument, _) =>
        {
            if (instrument.Meter.Name is GatewayTelemetryConventions.MeterName or "hvo.smartshunt") published.Add(instrument.Name);
        }};
        listener.Start();
        using var telemetry = new SmartShuntTelemetry();

        published.Should().BeEquivalentTo(
            GatewayTelemetry.CreateDiagnostics("hvo-smartshunt", [.. SmartShuntTelemetry.CompatibilityMetricNames]).MetricNames);
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
