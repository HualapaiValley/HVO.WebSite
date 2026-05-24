using System.Reflection;
using FluentAssertions;
using HVO.Enterprise.Telemetry.Abstractions;
using HVO.Hardware.JkBms.Bms;
using HVO.Hardware.JkBms.Configuration;
using HVO.Hardware.JkBms.Protocol;
using HVO.Hardware.JkBms.Telemetry;
using HVO.Hardware.JkBms.Tests.Fakes;
using HVO.Hardware.JkBms.Workers;
using Microsoft.Extensions.Logging.Abstractions;

namespace HVO.Hardware.JkBms.Tests.Workers;

[TestClass]
public class JkBmsDeviceLifecycleTests
{
    [TestMethod]
    public async Task RunAsync_ConnectFailure_RecordsFailureStateWithoutPolling()
    {
        var config = new BmsDeviceConfig { Address = "AA:BB:CC:DD:EE:FF", Alias = "bank-1" };
        var state = new DevicePollState
        {
            Address = config.Address,
            Alias = config.Alias,
            PollIntervalSeconds = 3600,
            NextPollAt = DateTime.UtcNow,
        };

        var transport = new FakeBmsTransport(config.Address);

        var factory = new FakeBmsTransportFactory(_ => transport);
        var coordinator = new FakeBluetoothAdapterCoordinator();
        coordinator.EnqueueFailure(new TimeoutException("connect failed"));

        await using var device = CreateDevice(config, state, factory, coordinator);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));
        var runTask = device.RunAsync(cts.Token);

        await Task.Delay(200, CancellationToken.None);
        cts.Cancel();
        await runTask.AwaitCancellationAsync();

        coordinator.ConnectCallCount.Should().Be(1);
        factory.LastAdapterName.Should().Be("hci0");
        state.LastPollAt.Should().BeNull();
        state.LastError.Should().Contain("connect failed");
        state.ConsecutiveErrors.Should().BeGreaterThan(0);
    }

    [TestMethod]
    public async Task RunAsync_InitializesDeviceInfoBeforeSteadyStatePolling()
    {
        var config = new BmsDeviceConfig { Address = "AA:BB:CC:DD:EE:11", Alias = "bank-2" };
        var state = new DevicePollState
        {
            Address = config.Address,
            Alias = config.Alias,
            PollIntervalSeconds = 3600,
            NextPollAt = DateTime.UtcNow,
        };

        var transport = new FakeBmsTransport(
            config.Address,
            frameSequence:
            [
                TestFrameBuilder.BuildDeviceInfoFrame(serialNumber: "SN-XYZ"),
                TestFrameBuilder.BuildCellInfoFrame(cellCount: 8)
            ]);

        var factory = new FakeBmsTransportFactory(_ => transport);
        var coordinator = new FakeBluetoothAdapterCoordinator();
        coordinator.EnqueueSuccess();

        await using var device = CreateDevice(config, state, factory, coordinator, adapterName: "hci1");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var runTask = device.RunAsync(cts.Token);

        await Task.Delay(300, CancellationToken.None);
        cts.Cancel();
        await runTask.AwaitCancellationAsync();

        state.LatestDeviceInfo.Should().NotBeNull();
        state.LatestDeviceInfo!.ManufacturerName.Should().Be("JIKONG");
        state.LatestDeviceInfo.HardwareName.Should().Be("JK-B2A24");
        state.LastPollAt.Should().NotBeNull();
        coordinator.Requests.Should().ContainSingle(r => r.AdapterName == "hci1" && r.Address == config.Address);
    }

    [TestMethod]
    public async Task RunAsync_ConnectFailure_HonorsComputedBackoffBeforeRetry()
    {
        var config = new BmsDeviceConfig { Address = "AA:BB:CC:DD:EE:22", Alias = "bank-3" };
        var state = new DevicePollState
        {
            Address = config.Address,
            Alias = config.Alias,
            PollIntervalSeconds = 3600,
            NextPollAt = DateTime.UtcNow,
            BackoffLevel = 2,
        };

        var transport = new FakeBmsTransport(config.Address);
        var factory = new FakeBmsTransportFactory(_ => transport);
        var coordinator = new FakeBluetoothAdapterCoordinator();
        coordinator.EnqueueFailure(new TimeoutException("connect failed"));
        coordinator.EnqueueSuccess();

        await using var device = CreateDevice(config, state, factory, coordinator);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(3500));
        var runTask = device.RunAsync(cts.Token);

        await Task.Delay(3200, CancellationToken.None);
        cts.Cancel();
        await runTask.AwaitCancellationAsync();

        coordinator.ConnectCallCount.Should().Be(1,
            because: "the reconnect loop should wait for the computed backoff instead of retrying after a fixed delay");
    }

    private static JkBmsDevice CreateDevice(
        BmsDeviceConfig config,
        DevicePollState state,
        FakeBmsTransportFactory factory,
        FakeBluetoothAdapterCoordinator coordinator,
        string adapterName = "hci0")
    {
        return new JkBmsDevice(
            config,
            adapterName,
            state,
            new JkBmsOptions { ExchangeTimeoutSeconds = 1, ConnectTimeoutSeconds = 5, DefaultPollIntervalSeconds = 3600 },
            factory,
            coordinator,
            NullLoggerFactory.Instance,
            new BmsTelemetry(),
            CreateTelemetryServiceProxy(),
            (_, _, _, _) => Task.CompletedTask,
            () => { },
            NullLogger<JkBmsDevice>.Instance);
    }

    private static ITelemetryService CreateTelemetryServiceProxy()
    {
        var telemetryAssembly = typeof(JkBmsDevice).Assembly
            .GetReferencedAssemblies()
            .Select(Assembly.Load)
            .First(a => a.GetType("HVO.Enterprise.Telemetry.Abstractions.ITelemetryService") is not null);

        var serviceType = telemetryAssembly.GetType("HVO.Enterprise.Telemetry.Abstractions.ITelemetryService")!;
        var startOperationMethod = serviceType.GetMethod("StartOperation")
            ?? throw new InvalidOperationException("ITelemetryService.StartOperation not found.");
        var operationType = startOperationMethod.ReturnType;

        var createGeneric = typeof(DispatchProxy)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(m => m.Name == nameof(DispatchProxy.Create) && m.IsGenericMethodDefinition && m.GetGenericArguments().Length == 2);

        var createOperationProxy = createGeneric.MakeGenericMethod(operationType, typeof(NoOpDispatchProxy));
        var operationProxy = createOperationProxy.Invoke(null, null)!;
        ((NoOpDispatchProxy)operationProxy).ReturnSelf = operationProxy;

        var createServiceProxy = createGeneric.MakeGenericMethod(serviceType, typeof(NoOpDispatchProxy));
        var serviceProxy = createServiceProxy.Invoke(null, null)!;
        ((NoOpDispatchProxy)serviceProxy).StartOperationResult = operationProxy;
        return (ITelemetryService)serviceProxy;
    }

    private class NoOpDispatchProxy : DispatchProxy
    {
        public object? ReturnSelf { get; set; }
        public object? StartOperationResult { get; set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod is null)
                return null;

            if (targetMethod.Name == "StartOperation")
                return StartOperationResult;

            if (targetMethod.ReturnType == typeof(void))
                return null;

            if (targetMethod.ReturnType == typeof(string))
                return string.Empty;

            if (targetMethod.ReturnType.IsValueType)
                return Activator.CreateInstance(targetMethod.ReturnType);

            if (ReturnSelf is not null && targetMethod.ReturnType.IsInstanceOfType(ReturnSelf))
                return ReturnSelf;

            return null;
        }
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
