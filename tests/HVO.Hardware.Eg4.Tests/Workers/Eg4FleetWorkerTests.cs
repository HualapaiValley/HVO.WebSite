using FluentAssertions;
using HVO.Edge.Contracts.PowerSystem;
using HVO.Edge.Hosting.Telemetry;
using HVO.Hardware.Eg4.Configuration;
using HVO.Hardware.Eg4.Dashboard;
using HVO.Hardware.Eg4.Outbox;
using HVO.Hardware.Eg4.Protocol;
using HVO.Hardware.Eg4.Telemetry;
using HVO.Hardware.Eg4.Workers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HVO.Hardware.Eg4.Tests.Workers;

[TestClass]
public sealed class Eg4FleetWorkerTests
{
    [TestMethod]
    public async Task PollOnce_PublishesAndEnqueuesBatteryOnlyPayload()
    {
        var device = Device();
        var source = new FakeSource(Observation(device, -20));
        var writer = new FakeWriter();
        using var telemetry = new GatewayTelemetry(new("eg4-test", "battery-gateway"));
        using var services = Services(writer);
        var dashboard = Dashboard(device);
        var worker = new Eg4FleetWorker(
            source,
            services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new Eg4Options { Devices = [device] }),
            dashboard,
            telemetry,
            TimeProvider.System,
            NullLogger<Eg4FleetWorker>.Instance);

        (await worker.PollOnceAsync(device, CancellationToken.None)).Should().BeTrue();

        writer.Payloads.Should().ContainSingle();
        writer.Payloads[0].SourceSystem.Should().Be("eg4-6500ex");
        writer.Payloads[0].BatteryCurrentA.Should().Be(-20);
        writer.Payloads[0].PvPowerW.Should().BeNull();
        dashboard.GetSnapshot().Devices.Should().ContainSingle().Which.State.Should().Be(Eg4DashboardDeviceState.Online);
    }

    [TestMethod]
    public async Task PollOnce_IsolatesDeviceFailureAndDoesNotMisreportOutboxFailureAsHardwareFailure()
    {
        var device = Device();
        var failingSource = new FakeSource(new Eg4TransportException(Eg4TransportFailureKind.Disconnected, "raw /dev/hidraw0 detail"));
        using var telemetry = new GatewayTelemetry(new("eg4-test", "battery-gateway"));
        using var services = Services(new FakeWriter());
        var failedDashboard = Dashboard(device);
        var failedWorker = new Eg4FleetWorker(
            failingSource, services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new Eg4Options { Devices = [device] }), failedDashboard,
            telemetry, TimeProvider.System, NullLogger<Eg4FleetWorker>.Instance);

        (await failedWorker.PollOnceAsync(device, CancellationToken.None)).Should().BeFalse();
        var failed = failedDashboard.GetSnapshot().Devices.Single();
        failed.State.Should().Be(Eg4DashboardDeviceState.Offline);
        failed.LastError.Should().Be("Transport Disconnected").And.NotContain("/dev/");

        var recoveringWriter = new FakeWriter(failuresBeforeSuccess: 1);
        using var enqueueServices = Services(recoveringWriter);
        var onlineDashboard = Dashboard(device);
        var onlineWorker = new Eg4FleetWorker(
            new FakeSource(Observation(device, 10)), enqueueServices.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new Eg4Options { Devices = [device] }), onlineDashboard,
            telemetry, TimeProvider.System, NullLogger<Eg4FleetWorker>.Instance);
        (await onlineWorker.PollOnceAsync(device, CancellationToken.None)).Should().BeTrue();
        recoveringWriter.Attempts.Should().Be(2);
        onlineDashboard.GetSnapshot().Devices.Single().State.Should().Be(Eg4DashboardDeviceState.Online);
    }

    private static ServiceProvider Services(FakeWriter writer)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IEg4PowerOutboxWriter>(writer);
        return services.BuildServiceProvider();
    }

    private static Eg4GatewayDashboardState Dashboard(Eg4DeviceOptions device) => new(
        Options.Create(new Eg4Options { Devices = [device] }),
        TimeProvider.System,
        new UnavailableEg4OutboxDashboardProvider());

    private static Eg4DeviceOptions Device() => new()
    {
        Type = Eg4DeviceType.Inverter6500Ex,
        SourceId = "eg4-a",
        DeviceId = "a",
        Alias = "Inverter A",
        Port = "/dev/hvo/eg4-a",
        UnitId = 0,
    };

    private static PowerBatteryObservation Observation(Eg4DeviceOptions device, double current) => new(
        device.SourceId,
        device.DeviceId,
        PowerMetricSource.Eg46500Ex,
        PowerMeasurementRole.InverterBranch,
        "inverter-battery-branch",
        DateTime.UtcNow,
        54,
        current,
        54 * current,
        80,
        PowerObservationProvenance.Derived);

    private sealed class FakeSource : IEg4TelemetrySource
    {
        private readonly PowerBatteryObservation? _observation;
        private readonly Exception? _error;
        public FakeSource(PowerBatteryObservation observation) => _observation = observation;
        public FakeSource(Exception error) => _error = error;
        public bool Supports(Eg4DeviceType deviceType) => deviceType == Eg4DeviceType.Inverter6500Ex;
        public ValueTask<PowerBatteryObservation> ReadAsync(Eg4DeviceOptions device, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_error is not null) throw _error;
            return ValueTask.FromResult(_observation!);
        }
    }

    private sealed class FakeWriter(Exception? error = null, int failuresBeforeSuccess = 0) : IEg4PowerOutboxWriter
    {
        private int _remainingFailures = failuresBeforeSuccess;
        public List<PowerReadingPayload> Payloads { get; } = [];
        public int Attempts { get; private set; }
        public Task<bool> EnqueueAsync(PowerReadingPayload payload, CancellationToken cancellationToken)
        {
            Attempts++;
            if (error is not null) throw error;
            if (_remainingFailures-- > 0) throw new IOException("transient disk failure");
            Payloads.Add(payload);
            return Task.FromResult(true);
        }
    }
}
