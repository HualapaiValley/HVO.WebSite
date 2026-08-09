using System.Diagnostics.Metrics;
using FluentAssertions;
using HVO.Edge.Contracts;
using HVO.Edge.Hosting.Telemetry;
using HVO.Gateway.SolarAssistant.Telemetry;

namespace HVO.Gateway.SolarAssistant.Tests.Telemetry;

[TestClass]
public sealed class SolarAssistantTelemetryTests
{
    [TestMethod]
    public void RecordPoll_EmitsOneCanonicalAndOneCompatibilityMeasurement()
    {
        var names = new List<string>();
        using var listener = Listen(names, GatewayTelemetryConventions.MeterName, "hvo.solarassistant");
        using var telemetry = new SolarAssistantTelemetry();

        telemetry.RecordPoll(true, 0.1);

        names.Count(name => name == GatewayTelemetryConventions.MetricNames.DevicePollAttempt).Should().Be(1);
        names.Count(name => name == "solarassistant.snapshot.poll.count").Should().Be(1);
    }

    [TestMethod]
    public void Diagnostics_ExactlyMatchPublishedCanonicalAndCompatibilityInstruments()
    {
        var published = new HashSet<string>();
        using var listener = new MeterListener { InstrumentPublished = (instrument, _) =>
        {
            if (instrument.Meter.Name is GatewayTelemetryConventions.MeterName or "hvo.solarassistant") published.Add(instrument.Name);
        }};
        listener.Start();
        using var telemetry = new SolarAssistantTelemetry();

        published.Should().BeEquivalentTo(
            GatewayTelemetry.CreateDiagnostics("hvo-solarassistant", [.. SolarAssistantTelemetry.CompatibilityMetricNames]).MetricNames);
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
