using FluentAssertions;
using HVO.Edge.Hosting.Telemetry;
using HVO.Hardware.JkBms.Configuration;
using HVO.Hardware.JkBms.Protocol;
using HVO.Hardware.JkBms.Tests.Fakes;
using HVO.Hardware.JkBms.Workers;
using Microsoft.Extensions.Logging.Abstractions;

namespace HVO.Hardware.JkBms.Tests.Workers;

[TestClass]
public sealed class JkBmsDeviceLifecycleTests
{
    [TestMethod]
    public async Task SettingsPasswordChange_WritesOnceAndVerifiesWithOneDeviceInfoRead()
    {
        var config = Device("03", "bank-c");
        var state = State(config);
        FakeBmsTransport? transport = null;
        var factory = new FakeBmsTransportFactory(address => transport = new FakeBmsTransport(
            address,
            frameSequence:
            [
                TestFrameBuilder.BuildDeviceInfoFrame(setupPasscode: "123456"),
                TestFrameBuilder.BuildCellInfoFrame32S(alarmBitmask: 0x00080000),
                TestFrameBuilder.BuildDeviceInfoFrame(setupPasscode: "654321"),
            ]));
        var coordinator = new FakeBluetoothAdapterCoordinator();
        await using var device = CreateDevice(config, state, factory, coordinator, settingsPassword: () => "654321");
        using var cancellation = new CancellationTokenSource();
        var run = device.RunAsync(cancellation.Token);
        await WaitUntilAsync(() => state.LastPollAt.HasValue);

        device.TryQueueSettingsPasswordChange().Should().BeTrue();
        await WaitUntilAsync(() => state.SettingsPasswordChangeStatus == "succeeded_verified");
        device.TryQueueSettingsPasswordChange().Should().BeFalse();
        cancellation.Cancel();
        await run.AwaitCancellationAsync();

        transport!.LastAcknowledgedCommand.Should().Equal(JkBmsProtocol.BuildSetSettingsPasswordCommand("654321"));
        transport.ExchangeCallCount.Should().Be(4);
    }

    [TestMethod]
    public async Task SessionInitialization_RecognizesAlreadyAppliedSettingsPassword()
    {
        var config = Device("04", "bank-d");
        var state = State(config);
        var factory = new FakeBmsTransportFactory(address => new FakeBmsTransport(
            address,
            frameSequence:
            [
                TestFrameBuilder.BuildDeviceInfoFrame(setupPasscode: "654321"),
                TestFrameBuilder.BuildCellInfoFrame32S(),
            ]));
        await using var device = CreateDevice(
            config,
            state,
            factory,
            new FakeBluetoothAdapterCoordinator(),
            settingsPassword: () => "654321");
        using var cancellation = new CancellationTokenSource();
        var run = device.RunAsync(cancellation.Token);

        await WaitUntilAsync(() => state.LastPollAt.HasValue);
        state.SettingsPasswordChangeStatus.Should().Be("succeeded_verified");
        device.TryQueueSettingsPasswordChange().Should().BeFalse();
        cancellation.Cancel();
        await run.AwaitCancellationAsync();
    }

    [TestMethod]
    public async Task RunAsync_ConnectFailure_IsIsolatedAndPublishesUnavailable()
    {
        var config = Device("FF", "bank-1");
        var state = State(config);
        var coordinator = new FakeBluetoothAdapterCoordinator();
        coordinator.EnqueueFailure(new TimeoutException("connect failed"));
        var unavailable = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var device = CreateDevice(config, state, FakeBmsTransportFactory.AlwaysSucceed(), coordinator, _ => unavailable.TrySetResult());
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(1));

        var run = device.RunAsync(cancellation.Token);
        await unavailable.Task.WaitAsync(TimeSpan.FromSeconds(1));
        cancellation.Cancel();
        await run.AwaitCancellationAsync();

        state.LastError.Should().Contain("connect failed");
        state.SessionRequestFailureCount.Should().Be(1);
        state.SessionEstablishedCount.Should().Be(0);
    }

    [TestMethod]
    public async Task RunAsync_PreservesPersistentSessionAndInitializesMetadataBeforePolling()
    {
        var config = Device("11", "bank-2");
        var state = State(config);
        var transport = new FakeBmsTransport(config.Address, frameSequence:
        [
            TestFrameBuilder.BuildDeviceInfoFrame(serialNumber: "SN-XYZ"),
            TestFrameBuilder.BuildCellInfoFrame(cellCount: 8),
        ]);
        var coordinator = new FakeBluetoothAdapterCoordinator();
        coordinator.EnqueueSuccess();
        var polled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var device = CreateDevice(
            config,
            state,
            new FakeBmsTransportFactory(_ => transport),
            coordinator,
            onPoll: (_, _, _, _) => { polled.TrySetResult(); return Task.CompletedTask; });
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(1));

        var run = device.RunAsync(cancellation.Token);
        await polled.Task.WaitAsync(TimeSpan.FromSeconds(1));
        cancellation.Cancel();
        await run.AwaitCancellationAsync();

        coordinator.ConnectCallCount.Should().Be(1);
        state.LatestDeviceInfo!.ManufacturerName.Should().Be("JIKONG");
        state.SessionEstablishedCount.Should().Be(1);
        state.LastPollAt.Should().NotBeNull();
    }

    private static BmsDeviceConfig Device(string suffix, string id) => new()
    {
        Address = $"AA:BB:CC:DD:EE:{suffix}",
        DeviceId = id,
        Alias = id,
    };

    private static DevicePollState State(BmsDeviceConfig config) => new()
    {
        Address = config.Address,
        DeviceId = config.DeviceId,
        Alias = config.Alias,
        AdapterName = "hci0",
        PollIntervalSeconds = 3600,
        NextPollAt = DateTime.UtcNow,
    };

    private static JkBmsDevice CreateDevice(
        BmsDeviceConfig config,
        DevicePollState state,
        FakeBmsTransportFactory factory,
        FakeBluetoothAdapterCoordinator coordinator,
        Action<DateTime>? onUnavailable = null,
        Func<DevicePollState, JkBmsClient, HVO.Hardware.JkBms.Protocol.Packets.CellInfoPacket, CancellationToken, Task>? onPoll = null,
        Func<string?>? settingsPassword = null)
    {
        return new JkBmsDevice(
            config,
            "hci0",
            state,
            new JkBmsOptions { ExchangeTimeoutSeconds = 1, ConnectTimeoutSeconds = 5 },
            factory,
            coordinator,
            NullLoggerFactory.Instance,
            new GatewayTelemetry(new("jkbms-test", "jk-bms-direct")),
            TimeProvider.System,
            settingsPassword ?? (() => null),
            onPoll ?? ((_, _, _, _) => Task.CompletedTask),
            onUnavailable ?? (_ => { }),
            () => { },
            NullLogger<JkBmsDevice>.Instance);
    }


    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (!condition())
            await Task.Delay(10, timeout.Token);
    }
}

internal static class TaskTestExtensions
{
    public static async Task AwaitCancellationAsync(this Task task)
    {
        try
        {
            await task.WaitAsync(TimeSpan.FromSeconds(2));
        }
        catch (OperationCanceledException)
        {
        }
    }
}
