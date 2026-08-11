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
        Func<DevicePollState, JkBmsClient, HVO.Hardware.JkBms.Protocol.Packets.CellInfoPacket, CancellationToken, Task>? onPoll = null)
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
            onPoll ?? ((_, _, _, _) => Task.CompletedTask),
            onUnavailable ?? (_ => { }),
            () => { },
            NullLogger<JkBmsDevice>.Instance);
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
