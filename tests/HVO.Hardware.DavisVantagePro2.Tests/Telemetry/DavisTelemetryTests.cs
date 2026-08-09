using System.Diagnostics;
using System.Diagnostics.Metrics;
using FluentAssertions;
using HVO.Edge.Contracts;
using HVO.Edge.Hosting.Telemetry;
using HVO.Hardware.DavisVantagePro2.Telemetry;

namespace HVO.Hardware.DavisVantagePro2.Tests.Telemetry;

[TestClass]
public sealed class DavisTelemetryTests
{
    [TestMethod]
    public void RecordPoll_EmitsOneCanonicalMeasurementAndPreservesPacketAlias()
    {
        var names = new List<string>();
        using var listener = CreateListener(names, GatewayTelemetryConventions.MeterName, "hvo.davis");
        using var telemetry = new DavisTelemetry();

        telemetry.RecordPoll(true, 0.1);
        telemetry.RecordLegacyPacket();

        names.Count(name => name == GatewayTelemetryConventions.MetricNames.DevicePollAttempt).Should().Be(1);
        names.Count(name => name == "davis.console.poll.count").Should().Be(1);
    }

    [TestMethod]
    public void ForwardOperation_PropagatesCanonicalTraceContextAndStopsOnce()
    {
        var stopped = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == GatewayTelemetryConventions.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = stopped.Add
        };
        ActivitySource.AddActivityListener(listener);
        using var telemetry = new DavisTelemetry();
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://example.test/weather");

        using (var activity = telemetry.StartForwardOperation())
        {
            activity.Should().NotBeNull();
            request.AddTraceContext();
            request.Headers.GetValues(CloudEventsConstants.TraceParentHeader).Should().ContainSingle().Which.Should().Be(activity!.Id);
            telemetry.RecordForward(1, true, 0.1, activity: activity);
        }

        stopped.Should().ContainSingle(activity =>
            activity.OperationName == GatewayTelemetryConventions.OperationNames.OutboxForward
            && activity.Status == ActivityStatusCode.Ok);
    }

    [TestMethod]
    public void Diagnostics_ExactlyMatchPublishedCanonicalAndCompatibilityInstruments()
    {
        var published = new HashSet<string>();
        using var listener = PublishedInstrumentListener(published, GatewayTelemetryConventions.MeterName, "hvo.davis");
        using var telemetry = new DavisTelemetry();

        published.Should().BeEquivalentTo(
            GatewayTelemetry.CreateDiagnostics("hvo-davis", [.. DavisTelemetry.CompatibilityMetricNames]).MetricNames);
    }

    private static MeterListener CreateListener(List<string> names, params string[] meters)
    {
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, current) =>
            {
                if (meters.Contains(instrument.Meter.Name)) current.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, _, _, _) => names.Add(instrument.Name));
        listener.Start();
        return listener;
    }

    private static MeterListener PublishedInstrumentListener(HashSet<string> names, params string[] meters)
    {
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, _) =>
            {
                if (meters.Contains(instrument.Meter.Name)) names.Add(instrument.Name);
            }
        };
        listener.Start();
        return listener;
    }
}
