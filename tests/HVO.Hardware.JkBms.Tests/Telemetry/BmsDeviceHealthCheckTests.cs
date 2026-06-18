using System.Diagnostics;
using FluentAssertions;
using HVO.Enterprise.Telemetry.Abstractions;
using HVO.Enterprise.Telemetry.HealthChecks;
using HVO.Hardware.JkBms.Bms;
using HVO.Hardware.JkBms.Configuration;
using HVO.Hardware.JkBms.Telemetry;
using HVO.Hardware.JkBms.Tests.Fakes;
using HVO.Hardware.JkBms.Workers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HVO.Hardware.JkBms.Tests.Telemetry;

[TestClass]
public sealed class BmsDeviceHealthCheckTests
{
    [TestMethod]
    public async Task CheckHealthAsync_ReturnsHealthyWhenAllDevicesConnected()
    {
        using var fixture = CreateFixture(deviceCount: 2);
        foreach (var state in fixture.Worker.DeviceStates)
        {
            state.IsSessionConnected = true;
        }
        var healthCheck = new BmsDeviceHealthCheck(fixture.Worker);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Be("All 2 BMS device(s) connected.");
    }

    [TestMethod]
    public async Task CheckHealthAsync_ReturnsDegradedWhenSomeDevicesDisconnected()
    {
        using var fixture = CreateFixture(deviceCount: 2);
        fixture.Worker.DeviceStates[0].IsSessionConnected = true;
        fixture.Worker.DeviceStates[1].IsSessionConnected = false;
        var healthCheck = new BmsDeviceHealthCheck(fixture.Worker);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        result.Status.Should().Be(HealthStatus.Degraded);
        result.Description.Should().Be("1 of 2 BMS device(s) connected.");
    }

    [TestMethod]
    public async Task CheckHealthAsync_ReturnsUnhealthyWhenNoneConnected()
    {
        using var fixture = CreateFixture(deviceCount: 2);
        foreach (var state in fixture.Worker.DeviceStates)
        {
            state.IsSessionConnected = false;
        }
        var healthCheck = new BmsDeviceHealthCheck(fixture.Worker);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Be("0 of 2 BMS device(s) connected.");
    }

    private static WorkerFixture CreateFixture(int deviceCount)
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var options = new JkBmsOptions
        {
            DefaultPollIntervalSeconds = 60,
            Devices = Enumerable.Range(1, deviceCount)
                .Select(i => new BmsDeviceConfig
                {
                    Enabled = true,
                    Address = $"AA:BB:CC:DD:EE:{i:00}",
                    Alias = $"bank-{i}"
                })
                .ToList()
        };

        var worker = new BmsPollerWorker(
            FakeBmsTransportFactory.AlwaysSucceed(),
            new FakeBluetoothAdapterCoordinator(),
            NullLoggerFactory.Instance,
            services.GetRequiredService<IServiceScopeFactory>(),
            new NullAlarmHandler(),
            Options.Create(options),
            new BmsTelemetry(),
            new NoOpTelemetryService(),
            NullLogger<BmsPollerWorker>.Instance);

        return new WorkerFixture(worker, services);
    }

    private sealed class WorkerFixture(BmsPollerWorker worker, ServiceProvider services) : IDisposable
    {
        public BmsPollerWorker Worker { get; } = worker;

        public void Dispose()
        {
            Worker.Dispose();
            services.Dispose();
        }
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
