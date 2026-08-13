using System.Collections.Concurrent;
using System.Text;
using FluentAssertions;
using HVO.Edge.Contracts.PowerSystem;
using HVO.Hardware.Eg4.Configuration;
using HVO.Hardware.Eg4.Protocol;
using HVO.Hardware.Eg4.Telemetry;

namespace HVO.Hardware.Eg4.Tests.Telemetry;

[TestClass]
public sealed class Eg46500ExTelemetrySourceTests
{
    [TestMethod]
    public async Task ReadAsync_ValidatesIdentityAndMapsChargingObservationWithDerivedProvenance()
    {
        var factory = new ScriptedFactory();
        factory.AddSession(SuccessfulSession("(000.0 00.0 120.1 60.0 1897 1790 029 437 54.40 068 100 0075 09.1 305.6 00.00 00000 00010110 00 00 02797 010"));
        var observedAt = new DateTimeOffset(2026, 8, 9, 18, 0, 0, TimeSpan.Zero);
        await using var source = new Eg46500ExTelemetrySource(factory, new FixedTimeProvider(observedAt));

        var sample = await source.ReadSampleAsync(Device(), CancellationToken.None);
        var observation = sample.BatteryObservation!;

        observation.SourceId.Should().Be("eg4-6500ex-a");
        observation.DeviceId.Should().Be("6500ex-a");
        observation.Source.Should().Be(PowerMetricSource.Eg46500Ex);
        observation.Role.Should().Be(PowerMeasurementRole.InverterBranch);
        observation.MeasurementPoint.Should().Be("inverter-battery-branch");
        observation.ObservedAtUtc.Should().Be(observedAt.UtcDateTime);
        observation.VoltageV.Should().Be(54.4);
        observation.CurrentA.Should().Be(-68);
        observation.PowerW.Should().BeApproximately(-3699.2, 0.001);
        observation.StateOfChargePercent.Should().Be(100);
        observation.Provenance.Should().Be(PowerObservationProvenance.Derived);
        observation.Confidence.Should().Contain("current=discharge-charge");
        observation.Inputs.Should().ContainSingle().Which.SourceId.Should().Be(observation.SourceId);
        factory.Commands.Should().Equal(
            Eg46500ExInquiry.ProtocolId,
            Eg46500ExInquiry.ModelName,
            Eg46500ExInquiry.GeneralModelName,
            Eg46500ExInquiry.MainFirmware,
            Eg46500ExInquiry.SecondaryFirmware,
            Eg46500ExInquiry.GeneralStatus,
            Eg46500ExInquiry.ParallelStatus,
            Eg46500ExInquiry.ExtendedStatus,
            Eg46500ExInquiry.TotalPvEnergy,
            Eg46500ExInquiry.TotalLoadEnergy);
        sample.MpptDetail!.Trackers.Should().HaveCount(2);
        sample.MpptDetail.Trackers[1].Provenance.Should().Be(PowerObservationProvenance.Derived);
        sample.MpptDetail.Trackers[1].Confidence.Should().Contain("low-resolution/coarse");
        sample.InverterDetail!.Temperatures.Select(value => value.TemperatureId).Should().Equal(
            "scc-pwm", "inverter", "battery-channel", "transformer");
        sample.InverterDetail.Statuses.Select(value => value.Key).Should().Contain(
            "main-firmware", "secondary-firmware", "charge-stage", "fan-locked", "fan-pwm-percent",
            "parallel-role", "parallel-warning-flags", "mppt-1-charge-power-w");
        sample.Energy!.Counters.Should().Contain(counter => counter.Key == "pv_energy" && counter.ValueKwh == 14473.1)
            .And.Contain(counter => counter.Key == "load_energy" && counter.ValueKwh == 8381.8);
    }

    [TestMethod]
    public async Task ReadAsync_Firmware7972UsesDirectQpigs2WithoutChangingOlderFirmwarePath()
    {
        var factory = new ScriptedFactory();
        factory.AddSession([
            .. IdentitySteps("VERFW:00079.72"),
            Step(Eg46500ExInquiry.GeneralStatus, "(000.0 00.0 120.0 60.0 0000 0000 000 360 53.20 000 075 0030 01.0 300.0 00.00 00000 00010000 00 00 00300 010"),
            Step(Eg46500ExInquiry.Pv2Status, "(03.1 327.3 01026"),
        ]);
        await using var source = new Eg46500ExTelemetrySource(factory, TimeProvider.System);

        var tracker = (await source.ReadSampleAsync(Device(), CancellationToken.None)).MpptDetail!.Trackers[1];

        tracker.VoltageV.Should().Be(327.3);
        tracker.CurrentA.Should().Be(3.1);
        tracker.PowerW.Should().Be(1026);
        tracker.Provenance.Should().Be(PowerObservationProvenance.Direct);
        tracker.Confidence.Should().Contain("79.72");
        factory.Commands.Should().ContainInOrder(Eg46500ExInquiry.GeneralStatus, Eg46500ExInquiry.Pv2Status,
            Eg46500ExInquiry.ParallelStatus, Eg46500ExInquiry.ExtendedStatus);
    }

    [TestMethod]
    public async Task ReadAsync_RejectsUnsupportedFirmwareBeforeStatusWithoutRetry()
    {
        var factory = new ScriptedFactory();
        factory.AddSession([
            Step(Eg46500ExInquiry.ProtocolId, "(PI30"),
            Step(Eg46500ExInquiry.ModelName, "(MKS2-6500"),
            Step(Eg46500ExInquiry.GeneralModelName, "(045"),
            Step(Eg46500ExInquiry.MainFirmware, "(VERFW:00080.00"),
            Step(Eg46500ExInquiry.SecondaryFirmware, "(VERFW:00061.13"),
        ]);
        await using var source = new Eg46500ExTelemetrySource(factory, TimeProvider.System);

        var failure = await FluentActions.Awaiting(async () => await source.ReadSampleAsync(Device(), CancellationToken.None))
            .Should().ThrowAsync<Eg4TransportException>();

        failure.Which.Kind.Should().Be(Eg4TransportFailureKind.Protocol);
        factory.Created.Should().Be(1);
        factory.Commands.Should().NotContain(Eg46500ExInquiry.GeneralStatus);
    }

    [TestMethod]
    public async Task ReadAsync_ReconnectsAfterTransientFailureAndRepeatsIdentity()
    {
        var factory = new ScriptedFactory();
        factory.AddSession([
            .. IdentitySteps(),
            new ScriptedStep(Eg46500ExInquiry.GeneralStatus,
                Error: new Eg4TransportException(Eg4TransportFailureKind.Crc, "bad crc")),
        ]);
        factory.AddSession(SuccessfulSession("(000.0 00.0 120.0 60.0 0000 0000 000 360 53.20 000 075 0030 00.0 000.0 00.00 00012 00010000 00 00 00000 010"));
        await using var source = new Eg46500ExTelemetrySource(factory, TimeProvider.System);

        var observation = (await source.ReadSampleAsync(Device(), CancellationToken.None)).BatteryObservation!;

        observation.CurrentA.Should().Be(12);
        observation.PowerW.Should().BeApproximately(638.4, 0.001);
        factory.Created.Should().Be(2);
        factory.Commands.Count(command => command == Eg46500ExInquiry.ProtocolId).Should().Be(2);
        factory.Disposed.Should().BeGreaterThanOrEqualTo(1);
    }

    [TestMethod]
    public async Task ReadAsync_ReusesValidatedIdentityForSubsequentPollOnSameConnection()
    {
        var factory = new ScriptedFactory();
        factory.AddSession([
            .. IdentitySteps(),
            Step(Eg46500ExInquiry.GeneralStatus, "(000.0 00.0 120.0 60.0 0000 0000 000 360 53.20 000 075 0030 00.0 000.0 00.00 00010 00010000 00 00 00000 010"),
            Step(Eg46500ExInquiry.GeneralStatus, "(000.0 00.0 120.0 60.0 0000 0000 000 360 53.20 000 075 0030 00.0 000.0 00.00 00011 00010000 00 00 00000 010"),
        ]);
        await using var source = new Eg46500ExTelemetrySource(factory, TimeProvider.System);

        var first = (await source.ReadSampleAsync(Device(), CancellationToken.None)).BatteryObservation!;
        var second = (await source.ReadSampleAsync(Device(), CancellationToken.None)).BatteryObservation!;

        first.CurrentA.Should().Be(10);
        second.CurrentA.Should().Be(11);
        factory.Created.Should().Be(1);
        factory.Commands.Count(command => command == Eg46500ExInquiry.ProtocolId).Should().Be(1);
        factory.Commands.Count(command => command == Eg46500ExInquiry.GeneralStatus).Should().Be(2);
        factory.Commands.Count(command => command == Eg46500ExInquiry.TotalPvEnergy).Should().Be(1);
        factory.Commands.Count(command => command == Eg46500ExInquiry.TotalLoadEnergy).Should().Be(1);
    }

    [TestMethod]
    public async Task ReadAsync_EnergyProtocolFailureDoesNotDiscardCoreTelemetry()
    {
        var factory = new ScriptedFactory();
        factory.AddSession([
            .. IdentitySteps(),
            Step(Eg46500ExInquiry.GeneralStatus, "(000.0 00.0 120.0 60.0 0000 0000 000 360 53.20 000 075 0030 00.0 000.0 00.00 00010 00010000 00 00 00000 010"),
            new ScriptedStep(Eg46500ExInquiry.TotalPvEnergy,
                Error: new Eg4TransportException(Eg4TransportFailureKind.Protocol, "unsupported")),
        ]);
        await using var source = new Eg46500ExTelemetrySource(factory, TimeProvider.System);

        var sample = await source.ReadSampleAsync(Device(), CancellationToken.None);

        sample.IsAvailable.Should().BeTrue();
        sample.BatteryObservation!.CurrentA.Should().Be(10);
        sample.Energy.Should().BeNull();
    }

    [TestMethod]
    public async Task ReadAsync_EnergyFailureWaitsForNextRefreshWindow()
    {
        var factory = new ScriptedFactory();
        factory.AddSession([
            .. IdentitySteps(),
            Step(Eg46500ExInquiry.GeneralStatus, "(000.0 00.0 120.0 60.0 0000 0000 000 360 53.20 000 075 0030 00.0 000.0 00.00 00010 00010000 00 00 00000 010"),
            new ScriptedStep(Eg46500ExInquiry.TotalPvEnergy,
                Error: new Eg4TransportException(Eg4TransportFailureKind.Protocol, "unsupported")),
            Step(Eg46500ExInquiry.GeneralStatus, "(000.0 00.0 120.0 60.0 0000 0000 000 360 53.20 000 075 0030 00.0 000.0 00.00 00011 00010000 00 00 00000 010"),
        ]);
        var time = new FixedTimeProvider(new DateTimeOffset(2026, 8, 13, 5, 0, 0, TimeSpan.Zero));
        await using var source = new Eg46500ExTelemetrySource(factory, time);

        await source.ReadSampleAsync(Device(), CancellationToken.None);
        await source.ReadSampleAsync(Device(), CancellationToken.None);

        factory.Commands.Count(command => command == Eg46500ExInquiry.TotalPvEnergy).Should().Be(1);
        factory.Commands.Count(command => command == Eg46500ExInquiry.TotalLoadEnergy).Should().Be(0);
        factory.Commands.Count(command => command == Eg46500ExInquiry.GeneralStatus).Should().Be(2);
    }

    [TestMethod]
    public async Task ReadAsync_HonorsCancellationAndRejectsOtherDeviceTypes()
    {
        var factory = new ScriptedFactory();
        await using var source = new Eg46500ExTelemetrySource(factory, TimeProvider.System);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await FluentActions.Awaiting(async () => await source.ReadSampleAsync(Device(), cancellation.Token))
            .Should().ThrowAsync<OperationCanceledException>();
        var controller = Device(Eg4DeviceType.ChargeControllerMppt10048Hv);
        await FluentActions.Awaiting(async () => await source.ReadSampleAsync(controller, CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>();
        factory.Created.Should().Be(0);
    }

    [TestMethod]
    public async Task ReadAsync_SerializesSamePortAndQueuedCancellationConsumesNoInquiry()
    {
        var factory = new ScriptedFactory();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        factory.AddSession([
            .. IdentitySteps(),
            new ScriptedStep(Eg46500ExInquiry.GeneralStatus,
                FrameResponse("(000.0 00.0 120.0 60.0 0000 0000 000 360 53.20 000 075 0030 00.0 000.0 00.00 00010 00010000 00 00 00000 010"),
                Started: started,
                Release: release),
            Step(Eg46500ExInquiry.GeneralStatus, "(000.0 00.0 120.0 60.0 0000 0000 000 360 53.20 000 075 0030 00.0 000.0 00.00 00011 00010000 00 00 00000 010"),
        ]);
        await using var source = new Eg46500ExTelemetrySource(factory, TimeProvider.System);
        var first = source.ReadSampleAsync(Device(), CancellationToken.None).AsTask();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(1));
        using var queuedCancellation = new CancellationTokenSource();
        var queued = source.ReadSampleAsync(Device(), queuedCancellation.Token).AsTask();

        await queuedCancellation.CancelAsync();
        await FluentActions.Awaiting(() => queued).Should().ThrowAsync<OperationCanceledException>();
        factory.Commands.Count(command => command == Eg46500ExInquiry.GeneralStatus).Should().Be(1);
        release.SetResult();
        (await first).BatteryObservation!.CurrentA.Should().Be(10);
        (await source.ReadSampleAsync(Device(), CancellationToken.None)).BatteryObservation!.CurrentA.Should().Be(11);
    }

    [TestMethod]
    public async Task ReadAsync_AllowsSeparatePortsToOverlapAndDisposeWaitsForActiveReads()
    {
        var factory = new ScriptedFactory();
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        factory.AddSession([
            .. IdentitySteps(),
            new ScriptedStep(Eg46500ExInquiry.GeneralStatus,
                FrameResponse("(000.0 00.0 120.0 60.0 0000 0000 000 360 53.20 000 075 0030 00.0 000.0 00.00 00010 00010000 00 00 00000 010"),
                Started: firstStarted,
                Release: release),
        ]);
        factory.AddSession([
            .. IdentitySteps(),
            new ScriptedStep(Eg46500ExInquiry.GeneralStatus,
                FrameResponse("(000.0 00.0 120.0 60.0 0000 0000 000 360 53.20 000 075 0030 00.0 000.0 00.00 00020 00010000 00 00 00000 010"),
                Started: secondStarted,
                Release: release),
        ]);
        var source = new Eg46500ExTelemetrySource(factory, TimeProvider.System);
        var first = source.ReadSampleAsync(Device(), CancellationToken.None).AsTask();
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        var secondDevice = Device();
        secondDevice.Port = "/dev/hvo/eg4-6500ex-b";
        secondDevice.SourceId = "eg4-6500ex-b";
        secondDevice.DeviceId = "6500ex-b";
        var second = source.ReadSampleAsync(secondDevice, CancellationToken.None).AsTask();
        await secondStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        var dispose = source.DisposeAsync().AsTask();
        dispose.IsCompleted.Should().BeFalse();
        release.SetResult();
        (await first).BatteryObservation!.CurrentA.Should().Be(10);
        (await second).BatteryObservation!.CurrentA.Should().Be(20);
        await dispose;
        await FluentActions.Awaiting(async () => await source.ReadSampleAsync(Device(), CancellationToken.None))
            .Should().ThrowAsync<ObjectDisposedException>();
    }

    private static Eg4DeviceOptions Device(Eg4DeviceType type = Eg4DeviceType.Inverter6500Ex) => new()
    {
        Type = type,
        SourceId = "eg4-6500ex-a",
        DeviceId = "6500ex-a",
        Alias = "6500EX A",
        Port = "/dev/hvo/eg4-6500ex-a",
        UnitId = 0,
    };

    private static ScriptedStep[] SuccessfulSession(string status) => [.. IdentitySteps(), Step(Eg46500ExInquiry.GeneralStatus, status)];

    private static ScriptedStep[] IdentitySteps(string mainFirmware = "VERFW:00079.71") =>
    [
        Step(Eg46500ExInquiry.ProtocolId, "(PI30"),
        Step(Eg46500ExInquiry.ModelName, "(MKS2-6500"),
        Step(Eg46500ExInquiry.GeneralModelName, "(045"),
        Step(Eg46500ExInquiry.MainFirmware, $"({mainFirmware}"),
        Step(Eg46500ExInquiry.SecondaryFirmware, "(VERFW:00061.13"),
    ];

    private static ScriptedStep Step(Eg46500ExInquiry inquiry, string payload) => new(inquiry, FrameResponse(payload));

    private static byte[] FrameResponse(string payload)
    {
        var bytes = Encoding.ASCII.GetBytes(payload);
        ushort crc = 0;
        foreach (var value in bytes)
        {
            crc ^= (ushort)(value << 8);
            for (var bit = 0; bit < 8; bit++)
                crc = (ushort)((crc & 0x8000) != 0 ? (crc << 1) ^ 0x1021 : crc << 1);
        }
        static byte Adjust(byte value) => value is 0x28 or 0x0D or 0x0A or 0x00 ? (byte)(value + 1) : value;
        return [.. bytes, Adjust((byte)(crc >> 8)), Adjust((byte)crc), 0x0D];
    }

    private sealed record ScriptedStep(
        Eg46500ExInquiry Inquiry,
        byte[]? Response = null,
        Exception? Error = null,
        TaskCompletionSource? Started = null,
        TaskCompletionSource? Release = null);

    private sealed class ScriptedFactory : IEg46500ExInquiryTransportFactory
    {
        private readonly ConcurrentQueue<ConcurrentQueue<ScriptedStep>> _sessions = new();
        private readonly ConcurrentQueue<Eg46500ExInquiry> _commands = new();
        private int _created;
        private int _disposed;
        public int Created => _created;
        public int Disposed => _disposed;
        public IReadOnlyList<Eg46500ExInquiry> Commands => [.. _commands];
        public void AddSession(IEnumerable<ScriptedStep> steps) => _sessions.Enqueue(new ConcurrentQueue<ScriptedStep>(steps));
        public IEg46500ExInquiryTransport Create(string port)
        {
            Interlocked.Increment(ref _created);
            if (!_sessions.TryDequeue(out var session)) throw new InvalidOperationException("No scripted transport session remains.");
            return new Transport(this, session);
        }

        private sealed class Transport(ScriptedFactory owner, ConcurrentQueue<ScriptedStep> steps) : IEg46500ExInquiryTransport
        {
            public async ValueTask<byte[]> ExchangeAsync(Eg46500ExInquiry inquiry, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                owner._commands.Enqueue(inquiry);
                if ((!steps.TryPeek(out var next) || next.Inquiry != inquiry) && inquiry == Eg46500ExInquiry.ParallelStatus)
                    return FrameResponse("(0 00000000000000 B 00 000.0 00.00 120.0 60.00 0000 0000 000 53.2 000 075 000.0 000 00000 00000 000 00000000 0 0 000 000 000 00 000 000.0 00");
                if ((!steps.TryPeek(out next) || next.Inquiry != inquiry) && inquiry == Eg46500ExInquiry.ExtendedStatus)
                    return FrameResponse("(00001 22533 01 00 00 030 031 032 033 02 00 000 0035 0552 0000 00.00 11");
                if ((!steps.TryPeek(out next) || next.Inquiry != inquiry) && inquiry == Eg46500ExInquiry.TotalPvEnergy)
                    return FrameResponse("(14473100");
                if ((!steps.TryPeek(out next) || next.Inquiry != inquiry) && inquiry == Eg46500ExInquiry.TotalLoadEnergy)
                    return FrameResponse("(08381800");
                if (!steps.TryDequeue(out var step)) throw new InvalidOperationException("No scripted inquiry remains.");
                if (step.Inquiry != inquiry) throw new InvalidOperationException($"Expected {step.Inquiry}, received {inquiry}.");
                step.Started?.TrySetResult();
                if (step.Release is not null) await step.Release.Task.WaitAsync(cancellationToken);
                if (step.Error is not null) throw step.Error;
                return step.Response!;
            }
            public ValueTask DisposeAsync()
            {
                Interlocked.Increment(ref owner._disposed);
                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}
