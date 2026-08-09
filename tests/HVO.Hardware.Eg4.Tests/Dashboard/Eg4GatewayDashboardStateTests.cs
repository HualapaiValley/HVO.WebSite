using FluentAssertions;
using HVO.Hardware.Eg4.Configuration;
using HVO.Hardware.Eg4.Dashboard;
using HVO.Hardware.Eg4.Protocol;
using HVO.Hardware.Eg4.Simulation;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace HVO.Hardware.Eg4.Tests.Dashboard;

[TestClass]
public sealed class Eg4GatewayDashboardStateTests
{
    [TestMethod]
    public async Task Refresh_ClassifiesIndependentMixedFleetAndStaleness()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 8, 9, 15, 0, 0, TimeSpan.Zero));
        var simulator = new Eg4FleetSimulator(time);
        var inverter = Device("inverter", Eg4DeviceType.Inverter6500Ex);
        var controllerA = Device("controller-a", Eg4DeviceType.ChargeControllerMppt10048Hv);
        var controllerB = Device("controller-b", Eg4DeviceType.ChargeControllerMppt10048Hv);
        await simulator.SetScriptAsync(inverter.SourceId, [new Eg4SimulationStep(new Eg4SimulatedTelemetry(52.5, 16, 840, 83))]);
        await simulator.SetScriptAsync(controllerA.SourceId, [new Eg4SimulationStep(Failure: Eg4TransportFailureKind.Crc)]);
        var options = Options.Create(new Eg4Options { SimulationEnabled = true, DefaultPollIntervalSeconds = 10, Devices = [controllerB, inverter, controllerA] });
        var state = new Eg4GatewayDashboardState(options, time, new UnavailableEg4OutboxDashboardProvider());
        var worker = new Eg4SimulationDashboardWorker(simulator, options, state, time, NullLogger<Eg4SimulationDashboardWorker>.Instance);

        await worker.PublishOnceAsync(CancellationToken.None);

        var first = state.GetSnapshot();
        first.Devices.Select(device => device.SourceId).Should().BeInAscendingOrder(StringComparer.OrdinalIgnoreCase);
        first.ConfiguredCount.Should().Be(3);
        first.OnlineCount.Should().Be(1);
        first.DegradedCount.Should().Be(1);
        first.OfflineCount.Should().Be(1);
        first.Devices.Single(device => device.SourceId == controllerA.SourceId).LastError.Should().Be("Transport Crc");
        first.Devices.Single(device => device.SourceId == inverter.SourceId).Role.Should().Be(HVO.Edge.Contracts.PowerSystem.PowerMeasurementRole.InverterBranch);
        state.Publish(inverter, await simulator.ReadAsync(inverter, CancellationToken.None), "MKS2-6500", "79.02 / 61.00");
        state.GetSnapshot().Devices.Single(device => device.SourceId == inverter.SourceId)
            .Should().Match<Eg4DashboardDevice>(device => device.Model == "MKS2-6500" && device.Firmware == "79.02 / 61.00");

        time.Advance(TimeSpan.FromSeconds(21));
        var stale = state.GetSnapshot();
        stale.DegradedCount.Should().Be(2);
        stale.Devices.Where(device => device.ObservedAtUtc.HasValue).Should().OnlyContain(device => device.IsStale);
    }

    [TestMethod]
    public async Task Publisher_RedactsUnexpectedErrorsAndWorkerHonorsCancellation()
    {
        var time = new FakeTimeProvider();
        var device = Device("secret", Eg4DeviceType.Inverter6500Ex);
        var options = Options.Create(new Eg4Options { Devices = [device] });
        var state = new Eg4GatewayDashboardState(options, time, new UnavailableEg4OutboxDashboardProvider());
        state.PublishFailure(device, new IOException("Failed to open /dev/ttyUSB9 with raw frame 0103."));

        state.GetSnapshot().Devices.Single().LastError.Should().Be("Read failed").And.NotContain("/dev/ttyUSB9");

        var simulator = new Eg4FleetSimulator(time);
        var worker = new Eg4SimulationDashboardWorker(simulator, options, state, time, NullLogger<Eg4SimulationDashboardWorker>.Instance);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await FluentActions.Awaiting(() => worker.PublishOnceAsync(cancellation.Token))
            .Should().ThrowAsync<OperationCanceledException>();
    }

    [TestMethod]
    public void OutboxControls_RemainUnavailableWithoutCollectorProvider()
    {
        var state = new Eg4GatewayDashboardState(
            Options.Create(new Eg4Options()), TimeProvider.System, new UnavailableEg4OutboxDashboardProvider());

        state.GetSnapshot().Outbox.IsRuntimeAvailable.Should().BeFalse();
        FluentActions.Invoking(() => state.UpdateOutboxSettings(new Eg4OutboxSettingsUpdate(BatchSize: 100)))
            .Should().Throw<InvalidOperationException>().WithMessage("*unavailable*");
    }

    [TestMethod]
    public async Task HealthCheck_ReflectsMisconfiguredOfflineAndHealthyFleet()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 8, 9, 15, 0, 0, TimeSpan.Zero));
        var emptyState = new Eg4GatewayDashboardState(
            Options.Create(new Eg4Options()), time, new UnavailableEg4OutboxDashboardProvider());
        (await new Eg4DashboardHealthCheck(emptyState).CheckHealthAsync(new HealthCheckContext())).Status.Should().Be(HealthStatus.Degraded);

        var device = Device("health", Eg4DeviceType.Inverter6500Ex);
        var state = new Eg4GatewayDashboardState(
            Options.Create(new Eg4Options { Devices = [device] }), time, new UnavailableEg4OutboxDashboardProvider());
        var health = new Eg4DashboardHealthCheck(state);
        (await health.CheckHealthAsync(new HealthCheckContext())).Status.Should().Be(HealthStatus.Unhealthy);

        var simulator = new Eg4FleetSimulator(time);
        await simulator.SetScriptAsync(device.SourceId, [new Eg4SimulationStep(new Eg4SimulatedTelemetry(52, 5, 260, 80))]);
        await new Eg4SimulationDashboardWorker(simulator, Options.Create(new Eg4Options { Devices = [device] }), state, time,
            NullLogger<Eg4SimulationDashboardWorker>.Instance).PublishOnceAsync(CancellationToken.None);
        (await health.CheckHealthAsync(new HealthCheckContext())).Status.Should().Be(HealthStatus.Healthy);
    }

    [TestMethod]
    public void Publisher_RejectsMismatchedSameTypeObservationIdentityAndRole()
    {
        var configured = Device("configured", Eg4DeviceType.Inverter6500Ex);
        var other = Device("other", Eg4DeviceType.Inverter6500Ex);
        var state = new Eg4GatewayDashboardState(
            Options.Create(new Eg4Options { Devices = [configured, other] }), TimeProvider.System,
            new UnavailableEg4OutboxDashboardProvider());
        var timestamp = DateTime.UtcNow;
        var wrongSource = new HVO.Edge.Contracts.PowerSystem.PowerBatteryObservation(
            other.SourceId, configured.DeviceId, HVO.Edge.Contracts.PowerSystem.PowerMetricSource.Eg46500Ex,
            HVO.Edge.Contracts.PowerSystem.PowerMeasurementRole.InverterBranch, configured.Alias, timestamp, VoltageV: 52);
        var wrongRole = new HVO.Edge.Contracts.PowerSystem.PowerBatteryObservation(
            configured.SourceId, configured.DeviceId, HVO.Edge.Contracts.PowerSystem.PowerMetricSource.Eg46500Ex,
            HVO.Edge.Contracts.PowerSystem.PowerMeasurementRole.ChargeControllerBranch, configured.Alias, timestamp, VoltageV: 52);

        FluentActions.Invoking(() => state.Publish(configured, wrongSource)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => state.Publish(configured, wrongRole)).Should().Throw<ArgumentException>();
        state.GetSnapshot().Devices.Should().OnlyContain(device => device.ObservedAtUtc == null);
    }

    [TestMethod]
    public async Task SimulationWorker_HonorsIndependentDevicePollIntervals()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 8, 9, 15, 0, 0, TimeSpan.Zero));
        var fast = Device("fast", Eg4DeviceType.Inverter6500Ex);
        fast.PollIntervalSeconds = 10;
        var slow = Device("slow", Eg4DeviceType.Inverter6500Ex);
        slow.PollIntervalSeconds = 30;
        var options = Options.Create(new Eg4Options { DefaultPollIntervalSeconds = 60, Devices = [fast, slow] });
        var simulator = new Eg4FleetSimulator(time);
        await simulator.SetScriptAsync(fast.SourceId,
            [new Eg4SimulationStep(new Eg4SimulatedTelemetry(PowerW: 1)), new Eg4SimulationStep(new Eg4SimulatedTelemetry(PowerW: 2))]);
        await simulator.SetScriptAsync(slow.SourceId,
            [new Eg4SimulationStep(new Eg4SimulatedTelemetry(PowerW: 10)), new Eg4SimulationStep(new Eg4SimulatedTelemetry(PowerW: 20))]);
        var state = new Eg4GatewayDashboardState(options, time, new UnavailableEg4OutboxDashboardProvider());
        var worker = new Eg4SimulationDashboardWorker(simulator, options, state, time, NullLogger<Eg4SimulationDashboardWorker>.Instance);
        var initial = WaitForStateAsync(state, () => state.GetSnapshot().Devices.All(device => device.ObservedAtUtc.HasValue));
        await worker.StartAsync(CancellationToken.None);
        await initial;

        var fastUpdate = WaitForStateAsync(state, () => state.GetSnapshot().Devices.Single(device => device.SourceId == fast.SourceId).PowerW == 2);
        time.Advance(TimeSpan.FromSeconds(10));
        await fastUpdate;
        state.GetSnapshot().Devices.Single(device => device.SourceId == slow.SourceId).PowerW.Should().Be(10);

        var slowUpdate = WaitForStateAsync(state, () => state.GetSnapshot().Devices.Single(device => device.SourceId == slow.SourceId).PowerW == 20);
        time.Advance(TimeSpan.FromSeconds(20));
        await slowUpdate;
        await worker.StopAsync(CancellationToken.None);
    }

    private static Eg4DeviceOptions Device(string id, Eg4DeviceType type) => new()
    {
        Type = type,
        SourceId = $"eg4-{id}",
        DeviceId = id,
        Alias = id,
        Port = $"/dev/serial/by-id/{id}",
        UnitId = 1,
    };

    private static async Task WaitForStateAsync(IEg4GatewayDashboardState state, Func<bool> condition)
    {
        if (condition()) return;
        var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void HandleChanged()
        {
            if (!condition()) return;
            state.Changed -= HandleChanged;
            changed.TrySetResult();
        }
        state.Changed += HandleChanged;
        await changed.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

}
