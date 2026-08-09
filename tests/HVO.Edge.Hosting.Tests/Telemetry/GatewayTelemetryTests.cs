using System.Diagnostics;
using System.Diagnostics.Metrics;
using FluentAssertions;
using HVO.Edge.Contracts;
using HVO.Edge.Hosting.Telemetry;

namespace HVO.Edge.Hosting.Tests.Telemetry;

[TestClass]
public sealed class GatewayTelemetryTests
{
    [TestMethod]
    public void Instruments_MatchCanonicalContractAndEmitBoundedTags()
    {
        var instruments = new Dictionary<string, Instrument>();
        var measurements = new List<(string Name, IReadOnlyDictionary<string, object?> Tags)>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == GatewayTelemetryConventions.MeterName)
                {
                    instruments[instrument.Name] = instrument;
                    meterListener.EnableMeasurementEvents(instrument);
                }
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, _, tags, _) => measurements.Add((instrument.Name, ToDictionary(tags))));
        listener.SetMeasurementEventCallback<int>((instrument, _, tags, _) => measurements.Add((instrument.Name, ToDictionary(tags))));
        listener.SetMeasurementEventCallback<double>((instrument, _, tags, _) => measurements.Add((instrument.Name, ToDictionary(tags))));
        listener.Start();

        using var telemetry = new GatewayTelemetry(new GatewayTelemetryIdentity("gateway-1", "battery", "hvo"));
        telemetry.RecordConnect(false, 0.5, "source-1", "device-1", "eg4", "timeout", reconnect: true);
        telemetry.RecordRead(true, 0.1, "source-1", "device-1", "eg4");
        telemetry.RecordPoll(false, 0.2, "source-1", "device-1", "eg4", "protocol");
        telemetry.RecordPoll(true, 0.2, "source-2", "device-2", "eg4");
        telemetry.RecordForward(2, true, 0.3, "hvo.power.reading.v1");
        telemetry.RecordHealth("degraded", 0.01);
        telemetry.SetOutboxState(4, 1);
        listener.RecordObservableInstruments();

        instruments.Keys.Should().BeEquivalentTo(GatewayTelemetryConventions.MetricNames.All);
        AssertInstrument<Counter<long>>(instruments, GatewayTelemetryConventions.MetricNames.DeviceConnectAttempt, GatewayTelemetryConventions.Units.Attempt);
        AssertInstrument<Counter<long>>(instruments, GatewayTelemetryConventions.MetricNames.DeviceConnectFailure, GatewayTelemetryConventions.Units.Failure);
        AssertInstrument<Counter<long>>(instruments, GatewayTelemetryConventions.MetricNames.DeviceReconnect, GatewayTelemetryConventions.Units.Reconnect);
        AssertInstrument<Histogram<double>>(instruments, GatewayTelemetryConventions.MetricNames.DeviceConnectDuration, GatewayTelemetryConventions.Units.Seconds);
        AssertInstrument<Counter<long>>(instruments, GatewayTelemetryConventions.MetricNames.DeviceReadAttempt, GatewayTelemetryConventions.Units.Attempt);
        AssertInstrument<Counter<long>>(instruments, GatewayTelemetryConventions.MetricNames.DeviceReadFailure, GatewayTelemetryConventions.Units.Failure);
        AssertInstrument<Histogram<double>>(instruments, GatewayTelemetryConventions.MetricNames.DeviceReadDuration, GatewayTelemetryConventions.Units.Seconds);
        AssertInstrument<Counter<long>>(instruments, GatewayTelemetryConventions.MetricNames.DevicePollAttempt, GatewayTelemetryConventions.Units.Attempt);
        AssertInstrument<Counter<long>>(instruments, GatewayTelemetryConventions.MetricNames.DevicePollFailure, GatewayTelemetryConventions.Units.Failure);
        AssertInstrument<Histogram<double>>(instruments, GatewayTelemetryConventions.MetricNames.DevicePollDuration, GatewayTelemetryConventions.Units.Seconds);
        AssertInstrument<ObservableGauge<double>>(instruments, GatewayTelemetryConventions.MetricNames.DeviceFreshnessSeconds, GatewayTelemetryConventions.Units.Seconds);
        AssertInstrument<ObservableGauge<int>>(instruments, GatewayTelemetryConventions.MetricNames.OutboxDepth, GatewayTelemetryConventions.Units.Record);
        AssertInstrument<ObservableGauge<int>>(instruments, GatewayTelemetryConventions.MetricNames.OutboxFailed, GatewayTelemetryConventions.Units.Record);
        AssertInstrument<Counter<long>>(instruments, GatewayTelemetryConventions.MetricNames.OutboxForwardSuccess, GatewayTelemetryConventions.Units.Record);
        AssertInstrument<Counter<long>>(instruments, GatewayTelemetryConventions.MetricNames.OutboxForwardFailure, GatewayTelemetryConventions.Units.Record);
        AssertInstrument<Histogram<double>>(instruments, GatewayTelemetryConventions.MetricNames.OutboxForwardDuration, GatewayTelemetryConventions.Units.Seconds);
        AssertInstrument<Counter<long>>(instruments, GatewayTelemetryConventions.MetricNames.HealthEvaluation, GatewayTelemetryConventions.Units.Evaluation);
        AssertInstrument<Histogram<double>>(instruments, GatewayTelemetryConventions.MetricNames.HealthEvaluationDuration, GatewayTelemetryConventions.Units.Seconds);
        measurements.Should().NotBeEmpty();
        measurements.Should().OnlyContain(measurement =>
            measurement.Tags.ContainsKey(GatewayTelemetryConventions.Tags.GatewayId) &&
            measurement.Tags.ContainsKey(GatewayTelemetryConventions.Tags.GatewayType));
        measurements.SelectMany(measurement => measurement.Tags.Keys)
            .Should().NotContain(key => key.Contains("exception", StringComparison.OrdinalIgnoreCase) || key.Contains("host", StringComparison.OrdinalIgnoreCase));
        measurements.Should().Contain(measurement =>
            measurement.Name == GatewayTelemetryConventions.MetricNames.DevicePollFailure
            && Equals(measurement.Tags[GatewayTelemetryConventions.Tags.Result], GatewayTelemetryConventions.Results.Failure)
            && Equals(measurement.Tags[GatewayTelemetryConventions.Tags.FailureKind], "protocol"));
    }

    [TestMethod]
    public void ResourceAttributes_ContainStableGatewayIdentity()
    {
        var attributes = GatewayTelemetryResource.Create(
            "hvo-test",
            new GatewayTelemetryIdentity("gateway-1", "battery", "hvo"),
            "Testing")
            .ToDictionary(attribute => attribute.Key, attribute => attribute.Value);

        attributes[GatewayTelemetryConventions.ResourceAttributes.GatewayId].Should().Be("gateway-1");
        attributes[GatewayTelemetryConventions.ResourceAttributes.GatewayType].Should().Be("battery");
        attributes[GatewayTelemetryConventions.ResourceAttributes.SiteId].Should().Be("hvo");
        attributes[GatewayTelemetryConventions.ResourceAttributes.DeploymentEnvironment].Should().Be("Testing");
        attributes[GatewayTelemetryConventions.ResourceAttributes.ServiceInstanceId].Should().Be("gateway-1");
    }

    [TestMethod]
    public void ObservableState_DoesNotPublishFalseZeroesBeforeFirstRuntimeSample()
    {
        var names = new List<string>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, current) =>
            {
                if (instrument.Meter.Name == GatewayTelemetryConventions.MeterName)
                    current.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<int>((instrument, _, _, _) => names.Add(instrument.Name));
        listener.SetMeasurementEventCallback<double>((instrument, _, _, _) =>
        {
            if (instrument is ObservableGauge<double>) names.Add(instrument.Name);
        });
        listener.Start();
        using var telemetry = new GatewayTelemetry(new GatewayTelemetryIdentity("gateway-1", "battery"));

        listener.RecordObservableInstruments();
        names.Should().BeEmpty();

        telemetry.SetOutboxState(0, 0);
        telemetry.RecordPoll(true, 0.1, "source-1", "device-1", "eg4");
        listener.RecordObservableInstruments();

        names.Should().BeEquivalentTo(
            GatewayTelemetryConventions.MetricNames.OutboxDepth,
            GatewayTelemetryConventions.MetricNames.OutboxFailed,
            GatewayTelemetryConventions.MetricNames.DeviceFreshnessSeconds);
    }

    [TestMethod]
    public void Activities_UseCanonicalNamesAndPreserveW3CParentChildContext()
    {
        var stopped = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == GatewayTelemetryConventions.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = stopped.Add
        };
        ActivitySource.AddActivityListener(listener);
        using var telemetry = new GatewayTelemetry(new GatewayTelemetryIdentity("gateway-1", "battery"));

        using (var parent = telemetry.StartOperation(GatewayTelemetryConventions.OperationNames.DevicePoll, sourceId: "source-1"))
        {
            parent.Should().NotBeNull();
            using var child = telemetry.StartOperation(GatewayTelemetryConventions.OperationNames.DeviceRead, sourceId: "source-1");
            child.Should().NotBeNull();
            child!.ParentSpanId.Should().Be(parent!.SpanId);
            child.TraceId.Should().Be(parent.TraceId);
            HvoActivitySource.Complete(child, false, "timeout");
            HvoActivitySource.Complete(parent, true);
        }

        stopped.Select(activity => activity.OperationName).Should().BeEquivalentTo(
            GatewayTelemetryConventions.OperationNames.DevicePoll,
            GatewayTelemetryConventions.OperationNames.DeviceRead);
        stopped.Should().OnlyContain(activity => activity.IdFormat == ActivityIdFormat.W3C);
        stopped.Single(activity => activity.OperationName == GatewayTelemetryConventions.OperationNames.DeviceRead)
            .Status.Should().Be(ActivityStatusCode.Error);
    }

    [TestMethod]
    public void Diagnostics_DeclareOnlyRegisteredCanonicalSourceAndMetrics()
    {
        var diagnostics = GatewayTelemetry.CreateDiagnostics("hvo-test", "legacy.metric");

        diagnostics.MetricNames.Should().BeEquivalentTo(
            GatewayTelemetryConventions.MetricNames.All.Append("legacy.metric"));
        diagnostics.ActivitySourceNames.Should().Equal(GatewayTelemetryConventions.ActivitySourceName);
    }

    [TestMethod]
    public void RecordForwardBatch_EmitsSuccessfulAndFailedRecordCountsOnce()
    {
        var measurements = new List<(string Name, long Value)>();
        var durations = new List<IReadOnlyDictionary<string, object?>>();
        var stopped = new List<Activity>();
        using var activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == GatewayTelemetryConventions.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = stopped.Add
        };
        ActivitySource.AddActivityListener(activityListener);
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, current) =>
            {
                if (instrument.Meter.Name == GatewayTelemetryConventions.MeterName)
                    current.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, _, _) => measurements.Add((instrument.Name, value)));
        listener.SetMeasurementEventCallback<double>((instrument, _, tags, _) =>
        {
            if (instrument.Name == GatewayTelemetryConventions.MetricNames.OutboxForwardDuration)
                durations.Add(ToDictionary(tags));
        });
        listener.Start();
        using var telemetry = new GatewayTelemetry(new GatewayTelemetryIdentity("gateway-1", "battery"));

        telemetry.RecordForwardBatch(2, 1, 0.25, "hvo.power.reading.v1", "validation");

        measurements.Should().ContainSingle(item =>
            item.Name == GatewayTelemetryConventions.MetricNames.OutboxForwardSuccess && item.Value == 2);
        measurements.Should().ContainSingle(item =>
            item.Name == GatewayTelemetryConventions.MetricNames.OutboxForwardFailure && item.Value == 1);
        durations.Should().ContainSingle().Which[GatewayTelemetryConventions.Tags.Result]
            .Should().Be(GatewayTelemetryConventions.Results.Degraded);
        stopped.Should().ContainSingle(activity =>
            activity.OperationName == GatewayTelemetryConventions.OperationNames.OutboxForward
            && activity.Status == ActivityStatusCode.Error
            && Equals(activity.GetTagItem(GatewayTelemetryConventions.Tags.Result), GatewayTelemetryConventions.Results.Degraded));
    }

    private static void AssertInstrument<TInstrument>(
        IReadOnlyDictionary<string, Instrument> instruments,
        string name,
        string unit)
        where TInstrument : Instrument
    {
        instruments[name].Should().BeOfType<TInstrument>();
        instruments[name].Unit.Should().Be(unit);
    }

    private static IReadOnlyDictionary<string, object?> ToDictionary(ReadOnlySpan<KeyValuePair<string, object?>> tags) =>
        tags.ToArray().ToDictionary(tag => tag.Key, tag => tag.Value);
}
