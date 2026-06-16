using System.Diagnostics;
using FluentAssertions;
using HVO.Enterprise.Telemetry.Abstractions;
using HVO.Enterprise.Telemetry.HealthChecks;
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
            AdapterName = "hci1",
            PollIntervalSeconds = 3600,
            NextPollAt = DateTime.UtcNow,
        };

        var transport = new FakeBmsTransport(config.Address);

        var factory = new FakeBmsTransportFactory(_ => transport);
        var coordinator = new FakeBluetoothAdapterCoordinator();
        coordinator.EnqueueFailure(new TimeoutException("connect failed"));

        var stateChanged = StateChangeProbe.Until(() => state.LastError?.Contains("connect failed") == true);
        await using var device = CreateDevice(config, state, factory, coordinator, onStateChanged: stateChanged.Notify);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));
        var runTask = device.RunAsync(cts.Token);

        await stateChanged.WaitAsync();
        cts.Cancel();
        await runTask.AwaitCancellationAsync();

        coordinator.ConnectCallCount.Should().Be(1);
        factory.LastAdapterName.Should().Be("hci0");
        state.LastPollAt.Should().BeNull();
        state.LastError.Should().Contain("connect failed");
        state.ConsecutiveErrors.Should().BeGreaterThan(0);
        state.SessionRequestFailureCount.Should().BeGreaterThan(0);
        state.SessionEstablishedCount.Should().Be(0);
    }

    [TestMethod]
    public async Task RunAsync_InitializesDeviceInfoBeforeSteadyStatePolling()
    {
        var config = new BmsDeviceConfig { Address = "AA:BB:CC:DD:EE:11", Alias = "bank-2" };
        var state = new DevicePollState
        {
            Address = config.Address,
            Alias = config.Alias,
            AdapterName = "hci1",
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

        var stateChanged = StateChangeProbe.Until(() => state.LastPollAt.HasValue);
        await using var device = CreateDevice(config, state, factory, coordinator, adapterName: "hci1", onStateChanged: stateChanged.Notify);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var runTask = device.RunAsync(cts.Token);

        await stateChanged.WaitAsync();
        cts.Cancel();
        await runTask.AwaitCancellationAsync();

        state.LatestDeviceInfo.Should().NotBeNull();
        state.LatestDeviceInfo!.ManufacturerName.Should().Be("JIKONG");
        state.LatestDeviceInfo.HardwareName.Should().Be("JK-B2A24");
        state.LastPollAt.Should().NotBeNull();
        state.SessionEstablishedCount.Should().Be(1);
        state.AdapterName.Should().Be("hci1");
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

        var stateChanged = StateChangeProbe.Until(() => state.LastError?.Contains("connect failed") == true);
        await using var device = CreateDevice(config, state, factory, coordinator, onStateChanged: stateChanged.Notify);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var runTask = device.RunAsync(cts.Token);

        await stateChanged.WaitAsync();
        (state.NextPollAt - DateTime.UtcNow).Should().BeGreaterThan(TimeSpan.FromSeconds(5),
            because: "BackoffLevel 2 should advance to a computed 8 second retry delay after the failure");

        await Task.Delay(TimeSpan.FromMilliseconds(750), CancellationToken.None);
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
        string adapterName = "hci0",
        Action? onStateChanged = null)
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
            new NoOpTelemetryService(),
            (_, _, _, _) => Task.CompletedTask,
            onStateChanged ?? (() => { }),
            NullLogger<JkBmsDevice>.Instance);
    }

    private sealed class StateChangeProbe
    {
        private readonly Func<bool> _isComplete;
        private readonly TaskCompletionSource _completed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private StateChangeProbe(Func<bool> isComplete) => _isComplete = isComplete;

        public static StateChangeProbe Until(Func<bool> isComplete) => new(isComplete);

        public void Notify()
        {
            if (_isComplete())
                _completed.TrySetResult();
        }

        public Task WaitAsync() => _isComplete()
            ? Task.CompletedTask
            : _completed.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }

    private sealed class NoOpTelemetryService : ITelemetryService
    {
        public bool IsEnabled => false;
        public ITelemetryStatistics Statistics { get; } = new NoOpTelemetryStatistics();

        public IOperationScope StartOperation(string operationName) => new NoOpOperationScope(operationName);
        public void TrackException(Exception exception) { }
        public void TrackEvent(string eventName) { }
        public void RecordMetric(string metricName, double value) { }
        public void Start() { }
        public void Shutdown() { }
    }

    private sealed class NoOpOperationScope(string name) : IOperationScope
    {
        public string Name { get; } = name;
        public string CorrelationId { get; } = string.Empty;
        public Activity? Activity => null;
        public TimeSpan Elapsed => TimeSpan.Zero;

        public IOperationScope WithTag(string key, object? value) => this;
        public IOperationScope WithTags(IEnumerable<KeyValuePair<string, object?>> tags) => this;
        public IOperationScope WithProperty(string key, Func<object?> valueFactory) => this;
        public IOperationScope Fail(Exception exception) => this;
        public IOperationScope Succeed() => this;
        public IOperationScope WithResult(object? result) => this;
        public IOperationScope CreateChild(string name) => new NoOpOperationScope(name);
        public void RecordException(Exception exception) { }
        public void Dispose() { }
    }

    private sealed class NoOpTelemetryStatistics : ITelemetryStatistics
    {
        public DateTimeOffset StartTime { get; } = DateTimeOffset.UtcNow;
        public long ActivitiesCreated => 0;
        public long ActivitiesCompleted => 0;
        public long ActiveActivities => 0;
        public long ExceptionsTracked => 0;
        public long EventsRecorded => 0;
        public long MetricsRecorded => 0;
        public int QueueDepth => 0;
        public int MaxQueueDepth => 0;
        public long ItemsEnqueued => 0;
        public long ItemsProcessed => 0;
        public long ItemsDropped => 0;
        public long ProcessingErrors => 0;
        public double AverageProcessingTimeMs => 0;
        public long CorrelationIdsGenerated => 0;
        public double CurrentErrorRate => 0;
        public double CurrentThroughput => 0;
        public IReadOnlyDictionary<string, ActivitySourceStatistics> PerSourceStatistics { get; } =
            new Dictionary<string, ActivitySourceStatistics>();

        public TelemetryStatisticsSnapshot GetSnapshot() => new()
        {
            Timestamp = DateTimeOffset.UtcNow,
            StartTime = StartTime,
        };

        public void Reset() { }
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
