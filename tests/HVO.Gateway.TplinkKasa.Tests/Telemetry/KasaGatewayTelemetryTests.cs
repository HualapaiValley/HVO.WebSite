using System.Diagnostics.Metrics;
using FluentAssertions;
using HVO.Edge.Contracts;
using HVO.Edge.Hosting.Telemetry;
using HVO.Gateway.TplinkKasa.Telemetry;

namespace HVO.Gateway.TplinkKasa.Tests.Telemetry;

[TestClass]
public sealed class KasaGatewayTelemetryTests
{
    [TestMethod]
    public void RecordPoll_EmitsOneCanonicalAndOneCompatibilityMeasurement()
    {
        var names = new List<string>();
        using var listener = Listen(names, GatewayTelemetryConventions.MeterName, "hvo.tplinkkasa");
        using var telemetry = new KasaGatewayTelemetry();

        telemetry.RecordPoll("plug-1", "plug", true, 0.1);

        names.Count(name => name == GatewayTelemetryConventions.MetricNames.DevicePollAttempt).Should().Be(1);
        names.Count(name => name == "kasa.device.poll.count").Should().Be(1);
    }

    [TestMethod]
    public void Diagnostics_ExactlyMatchPublishedCanonicalAndCompatibilityInstruments()
    {
        var published = new HashSet<string>();
        using var listener = new MeterListener { InstrumentPublished = (instrument, _) =>
        {
            if (instrument.Meter.Name is GatewayTelemetryConventions.MeterName or "hvo.tplinkkasa") published.Add(instrument.Name);
        }};
        listener.Start();
        using var telemetry = new KasaGatewayTelemetry();

        published.Should().BeEquivalentTo(
            GatewayTelemetry.CreateDiagnostics("hvo-tplinkkasa", [.. KasaGatewayTelemetry.CompatibilityMetricNames]).MetricNames);
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
