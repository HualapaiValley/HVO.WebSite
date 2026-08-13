using FluentAssertions;
using HVO.Edge.Contracts;
using HVO.Edge.HomeAssistant.Mqtt;
using HVO.Edge.Hosting.Telemetry;
using HVO.Edge.Outbox;
using HVO.Hardware.VictronSmartShunt.Configuration;
using HVO.Hardware.VictronSmartShunt.HomeAssistant;
using HVO.Hardware.VictronSmartShunt.Outbox;
using HVO.Hardware.VictronSmartShunt.SmartShunt;
using HVO.Hardware.VictronSmartShunt.Workers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HVO.Hardware.VictronSmartShunt.Tests.Workers;

[TestClass]
public sealed class SmartShuntWorkerTests
{
    [TestMethod]
    public async Task HealthyAcquisition_OutboxFailureRetainsAvailableStateAndRetriesSameObservation()
    {
        var fixture = Fixture(connected: true, writerFailures: 1);
        await fixture.Worker.PollOnceAsync(CancellationToken.None);
        await fixture.Worker.PollOnceAsync(CancellationToken.None);
        fixture.HomeAssistant.Availability.Should().Equal(true, true);
        fixture.Writer.Attempts.Should().Be(2);
        fixture.Writer.RecordedAt.Should().OnlyContain(timestamp => timestamp == fixture.Sample.RecordedAtUtc);
    }

    [TestMethod]
    public async Task MissingStaleOrDisconnectedAcquisitionReturnsUnavailableBoundary()
    {
        var disconnected = Fixture(connected: false);
        await disconnected.Worker.RunIterationAsync(CancellationToken.None);
        disconnected.HomeAssistant.Availability.Should().Equal(false);
        var stale = Fixture(connected: true, stale: true);
        await stale.Worker.RunIterationAsync(CancellationToken.None);
        stale.HomeAssistant.Availability.Should().Equal(false);
    }

    [TestMethod]
    public async Task RepeatedStaleIterationsPublishOneUnavailableTransitionAndAllowRecovery()
    {
        var fixture = Fixture(connected: true, stale: true);

        await fixture.Worker.RunIterationAsync(CancellationToken.None);
        await fixture.Worker.RunIterationAsync(CancellationToken.None);
        fixture.Session.CurrentSample = new SmartShuntLiveSample
        {
            RecordedAtUtc = fixture.Now.AddSeconds(1),
            VoltageV = fixture.Sample.VoltageV,
            CurrentA = fixture.Sample.CurrentA,
            PowerW = fixture.Sample.PowerW,
            StateOfChargePercent = fixture.Sample.StateOfChargePercent,
        };
        await fixture.Worker.RunIterationAsync(CancellationToken.None);

        fixture.HomeAssistant.Availability.Should().Equal(false, true);
    }

    [TestMethod]
    public async Task CancellationFromOutboxIsPropagated()
    {
        var fixture = Fixture(connected: true, cancelWriter: true);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var action = () => fixture.Worker.PollOnceAsync(cts.Token);
        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    private static TestFixture Fixture(bool connected, int writerFailures = 0, bool stale = false, bool cancelWriter = false)
    {
        var now = new DateTime(2026, 8, 12, 12, 0, 0, DateTimeKind.Utc);
        var sample = new SmartShuntLiveSample { RecordedAtUtc = stale ? now.AddMinutes(-5) : now, VoltageV = 52, CurrentA = -5, PowerW = -260, StateOfChargePercent = 80 };
        var session = new Session { IsConnected = connected, CurrentSample = sample };
        var writer = new Writer(writerFailures, cancelWriter);
        var services = new ServiceCollection().AddScoped<ISmartShuntOutboxWriter>(_ => writer).BuildServiceProvider();
        var ha = new HomeAssistant();
        var telemetry = new GatewayTelemetry(new("smartshunt", "direct", "hvo"));
        var worker = new SmartShuntWorker(services.GetRequiredService<IServiceScopeFactory>(), session, ha,
            Options.Create(new SmartShuntOptions { SourceId = "source", DeviceId = "device", SnapshotIntervalSeconds = 1, SampleStaleAfterSeconds = 60 }),
            telemetry, new FixedTimeProvider(now), NullLogger<SmartShuntWorker>.Instance);
        return new(worker, writer, ha, sample, session, now, services, telemetry);
    }

    private sealed record TestFixture(SmartShuntWorker Worker, Writer Writer, HomeAssistant HomeAssistant, SmartShuntLiveSample Sample, Session Session, DateTime Now, ServiceProvider Services, GatewayTelemetry Telemetry);
    private sealed class Session : ISmartShuntSessionState { public SmartShuntLiveSample? CurrentSample { get; set; } public bool IsConnected { get; set; } public string? LastError => null; }
    private sealed class Writer(int failures, bool cancel) : ISmartShuntOutboxWriter
    {
        public int Attempts { get; private set; } public List<DateTime> RecordedAt { get; } = [];
        public Task<bool> EnqueueAsync(HVO.Edge.Contracts.PowerSystem.SmartShuntObservationPayload bundle, CancellationToken cancellationToken)
        {
            Attempts++; RecordedAt.Add(bundle.Summary.RecordedAtUtc);
            if (cancel) throw new OperationCanceledException(cancellationToken);
            if (Attempts <= failures) throw new InvalidOperationException("sqlite unavailable");
            return Task.FromResult(true);
        }
    }
    private sealed class HomeAssistant : ISmartShuntHomeAssistantProjection
    {
        public List<bool> Availability { get; } = [];
        public bool Publish(SmartShuntLiveSample sample) { Availability.Add(true); return true; }
        public bool PublishUnavailable(DateTime observedAtUtc) { Availability.Add(false); return true; }
    }
    private sealed class FixedTimeProvider(DateTime now) : TimeProvider { public override DateTimeOffset GetUtcNow() => new(now); }
}
