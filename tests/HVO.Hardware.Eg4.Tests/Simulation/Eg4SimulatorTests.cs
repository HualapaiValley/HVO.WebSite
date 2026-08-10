using FluentAssertions;
using HVO.Edge.Contracts.PowerSystem;
using HVO.Hardware.Eg4.Configuration;
using HVO.Hardware.Eg4.Protocol;
using HVO.Hardware.Eg4.Simulation;
using Microsoft.Extensions.Time.Testing;

namespace HVO.Hardware.Eg4.Tests.Simulation;

[TestClass]
public sealed class Eg4SimulatorTests
{
    [TestMethod]
    public async Task SeedIfUnscripted_PreservesExplicitScript()
    {
        var simulator = new Eg4FleetSimulator(new FakeTimeProvider());
        var device = Device("seeded", Eg4DeviceType.Inverter6500Ex, 1);
        await simulator.SetScriptAsync(device.SourceId, [new Eg4SimulationStep(new Eg4SimulatedTelemetry(PowerW: 100))]);

        await simulator.SeedIfUnscriptedAsync(device.SourceId, new Eg4SimulatedTelemetry(PowerW: 200));

        (await simulator.ReadAsync(device, CancellationToken.None)).BatteryObservation!.PowerW.Should().Be(100);
    }

    [TestMethod]
    public async Task RichSimulation_ReturnsPvAndInverterDetail()
    {
        var simulator = new Eg4FleetSimulator(new FakeTimeProvider());
        var device = Device("rich", Eg4DeviceType.Inverter6500Ex, 1);
        await simulator.SetScriptAsync(device.SourceId,
            [new Eg4SimulationStep(new Eg4SimulatedTelemetry(52.4, 8, 419.2, 80, IncludeRichDetail: true))]);

        var sample = await simulator.ReadAsync(device, CancellationToken.None);

        sample.MpptDetail!.Trackers.Should().HaveCount(2);
        sample.InverterDetail!.Ac!.OutputVoltageV.Should().Be(120.1);
        sample.InverterDetail.Load!.LoadPowerW.Should().Be(850);
    }

    [TestMethod]
    public async Task Fleet_SupportsSameTypeMixedTypesTransitionsAndIndependentFailure()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 8, 9, 12, 0, 0, TimeSpan.Zero));
        var simulator = new Eg4FleetSimulator(time);
        var inverterA = Device("inverter-a", Eg4DeviceType.Inverter6500Ex, 1);
        var inverterB = Device("inverter-b", Eg4DeviceType.Inverter6500Ex, 2);
        var mppt = Device("mppt-a", Eg4DeviceType.ChargeControllerMppt10048Hv, 3);
        await simulator.SetScriptAsync(inverterA.SourceId, [new Eg4SimulationStep(Failure: Eg4TransportFailureKind.Crc)]);
        await simulator.SetScriptAsync(inverterB.SourceId, [new Eg4SimulationStep(new Eg4SimulatedTelemetry(53, 10, 530, 80))]);
        await simulator.SetScriptAsync(mppt.SourceId, [new Eg4SimulationStep(new Eg4SimulatedTelemetry(53, -8, -424, 80))]);

        var results = await simulator.PollFleetAsync([inverterA, inverterB, mppt], CancellationToken.None);

        results.Should().HaveCount(3);
        results.Single(result => result.Device.SourceId == inverterA.SourceId).Error.Should().BeOfType<Eg4TransportException>();
        results.Single(result => result.Device.SourceId == inverterB.SourceId).Observation!.Role.Should().Be(PowerMeasurementRole.InverterBranch);
        results.Single(result => result.Device.SourceId == mppt.SourceId).Observation!.Role.Should().Be(PowerMeasurementRole.ChargeControllerBranch);
        results.Where(result => result.Observation is not null).Should().OnlyContain(result => result.Observation!.ObservedAtUtc.Kind == DateTimeKind.Utc);
    }

    [TestMethod]
    public async Task ScriptedTransport_UsesVirtualDelayCapturesRequestsAndModelsFaultReconnect()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 8, 9, 12, 0, 0, TimeSpan.Zero));
        var factory = new ScriptedEg4RegisterTransportFactory(time);
        var request = new Eg4ReadRegistersRequest(1, Eg4RegisterTable.Holding, 100, 2);
        factory.Enqueue("port", 1,
            new ScriptedRegisterStep(request, Failure: Eg4TransportFailureKind.Disconnected),
            new ScriptedRegisterStep(request, [10, 20], TimeSpan.FromMinutes(1)));
        await using var transport = factory.Create("port");

        await FluentActions.Awaiting(async () => await transport.ReadRegistersAsync(request, CancellationToken.None))
            .Should().ThrowAsync<Eg4TransportException>();
        var delayed = transport.ReadRegistersAsync(request, CancellationToken.None).AsTask();
        delayed.IsCompleted.Should().BeFalse();
        time.Advance(TimeSpan.FromMinutes(1));

        (await delayed).Registers.Should().Equal(10, 20);
        factory.CapturedRequests.Should().HaveCount(2);
        factory.CapturedRequests.Select(capture => capture.Sequence).Should().Equal(1, 2);
    }

    [TestMethod]
    public async Task VirtualDelayHonorsCancellationWithoutWallClockSleep()
    {
        var time = new FakeTimeProvider();
        var simulator = new Eg4FleetSimulator(time);
        var device = Device("cancel", Eg4DeviceType.Inverter6500Ex, 1);
        await simulator.SetScriptAsync(device.SourceId, [new Eg4SimulationStep(new Eg4SimulatedTelemetry(), TimeSpan.FromHours(1))]);
        using var cts = new CancellationTokenSource();
        var read = simulator.ReadAsync(device, cts.Token).AsTask();

        await cts.CancelAsync();

        await FluentActions.Awaiting(() => read).Should().ThrowAsync<OperationCanceledException>();
    }

    [TestMethod]
    public async Task ScriptedTransport_ModelsCrcMalformedAndTimeoutFailures()
    {
        var time = new FakeTimeProvider();
        var factory = new ScriptedEg4RegisterTransportFactory(time);
        var request = new Eg4ReadRegistersRequest(1, Eg4RegisterTable.Input, 10, 1);
        factory.Enqueue("port", 1,
            new ScriptedRegisterStep(request, Failure: Eg4TransportFailureKind.Crc),
            new ScriptedRegisterStep(request, Failure: Eg4TransportFailureKind.MalformedFrame),
            new ScriptedRegisterStep(request, Timeout: true));
        await using var transport = factory.Create("port");

        var crc = await FluentActions.Awaiting(async () => await transport.ReadRegistersAsync(request, CancellationToken.None))
            .Should().ThrowAsync<Eg4TransportException>();
        var malformed = await FluentActions.Awaiting(async () => await transport.ReadRegistersAsync(request, CancellationToken.None))
            .Should().ThrowAsync<Eg4TransportException>();
        await FluentActions.Awaiting(async () => await transport.ReadRegistersAsync(request, CancellationToken.None))
            .Should().ThrowAsync<TimeoutException>();

        crc.Which.Kind.Should().Be(Eg4TransportFailureKind.Crc);
        malformed.Which.Kind.Should().Be(Eg4TransportFailureKind.MalformedFrame);
    }

    [TestMethod]
    public async Task ScriptReplacementWaitsForActiveReadAndCanceledTransportReadConsumesNothing()
    {
        var time = new FakeTimeProvider();
        var simulator = new Eg4FleetSimulator(time);
        var device = Device("ordered", Eg4DeviceType.Inverter6500Ex, 1);
        await simulator.SetScriptAsync(device.SourceId,
            [new Eg4SimulationStep(new Eg4SimulatedTelemetry(PowerW: 1), TimeSpan.FromHours(1))]);
        var read = simulator.ReadAsync(device, CancellationToken.None).AsTask();
        var replacement = simulator.SetScriptAsync(device.SourceId,
            [new Eg4SimulationStep(new Eg4SimulatedTelemetry(PowerW: 2))]).AsTask();
        replacement.IsCompleted.Should().BeFalse();
        time.Advance(TimeSpan.FromHours(1));
        (await read).BatteryObservation!.PowerW.Should().Be(1);
        await replacement;
        (await simulator.ReadAsync(device, CancellationToken.None)).BatteryObservation!.PowerW.Should().Be(2);

        var factory = new ScriptedEg4RegisterTransportFactory(time);
        var request = new Eg4ReadRegistersRequest(1, Eg4RegisterTable.Input, 1, 1);
        factory.Enqueue("port", 1, new ScriptedRegisterStep(request, [42]));
        await using var transport = factory.Create("port");
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        await FluentActions.Awaiting(async () => await transport.ReadRegistersAsync(request, cts.Token))
            .Should().ThrowAsync<OperationCanceledException>();
        factory.CapturedRequests.Should().BeEmpty();
        (await transport.ReadRegistersAsync(request, CancellationToken.None)).Registers.Should().Equal(42);
    }

    private static Eg4DeviceOptions Device(string id, Eg4DeviceType type, byte unitId) => new()
    {
        Type = type,
        SourceId = $"eg4-{id}",
        DeviceId = id,
        Alias = id,
        Port = "/dev/serial/by-id/test",
        UnitId = unitId,
    };
}
