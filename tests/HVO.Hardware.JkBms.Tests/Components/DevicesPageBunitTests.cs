using System.Diagnostics;
using Bunit;
using FluentAssertions;
using HVO.Enterprise.Telemetry.Abstractions;
using HVO.Enterprise.Telemetry.HealthChecks;
using HVO.Hardware.JkBms.Bms;
using HVO.Hardware.JkBms.Configuration;
using HVO.Hardware.JkBms.Telemetry;
using HVO.Hardware.JkBms.Tests.Fakes;
using HVO.Hardware.JkBms.Workers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HVO.Hardware.JkBms.Tests.Components;

[TestClass]
public sealed class DevicesPageBunitTests : BunitContext
{
    [TestMethod]
    public void RendersConfiguredDeviceRows()
    {
        var options = new JkBmsOptions
        {
            DefaultPollIntervalSeconds = 60,
            Devices =
            [
                new BmsDeviceConfig { Address = "AA:BB:CC:DD:EE:01", Alias = "bank-1a", PollIntervalSeconds = 30 },
                new BmsDeviceConfig { Address = "AA:BB:CC:DD:EE:02", Alias = "bank-1b", PollIntervalSeconds = 45, Enabled = false },
            ],
        };
        Services.AddSingleton(CreatePoller(options));
        Services.AddSingleton(Options.Create(options));

        var component = Render<HVO.Hardware.JkBms.Components.Pages.Devices>();

        component.Markup.Should().Contain("Configured JK BMS banks");
        component.Markup.Should().Contain("bank-1a");
        component.Markup.Should().Contain("AA:BB:CC:DD:EE:01");
        component.Markup.Should().Contain("bank-1b");
        component.Markup.Should().Contain("No");
    }

    [TestMethod]
    public void UsesDefaultPollIntervalWhenDeviceIntervalMissing()
    {
        var options = new JkBmsOptions
        {
            DefaultPollIntervalSeconds = 75,
            Devices = [new BmsDeviceConfig { Address = "AA:BB:CC:DD:EE:03", Alias = "bank-1c" }],
        };
        Services.AddSingleton(CreatePoller(options));
        Services.AddSingleton(Options.Create(options));

        var component = Render<HVO.Hardware.JkBms.Components.Pages.Devices>();

        component.Markup.Should().Contain("Default poll");
        component.Markup.Should().Contain("75 s");
    }

    [TestMethod]
    public void RendersPollerErrors()
    {
        var options = new JkBmsOptions
        {
            DefaultPollIntervalSeconds = 60,
            Devices = [new BmsDeviceConfig { Address = "AA:BB:CC:DD:EE:04", Alias = "bank-1d" }],
        };
        var poller = CreatePoller(options);
        poller.DeviceStates[0].ConsecutiveErrors = 3;
        poller.DeviceStates[0].LastError = "BLE timeout";
        Services.AddSingleton(poller);
        Services.AddSingleton(Options.Create(options));

        var component = Render<HVO.Hardware.JkBms.Components.Pages.Devices>();

        component.Markup.Should().Contain("Consecutive Errors");
        component.Markup.Should().Contain("3");
        component.Markup.Should().Contain("BLE timeout");
    }

    private static BmsPollerWorker CreatePoller(JkBmsOptions options)
        => new(
            FakeBmsTransportFactory.AlwaysSucceed(),
            new FakeBluetoothAdapterCoordinator(),
            NullLoggerFactory.Instance,
            new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            new NullAlarmHandler(),
            Options.Create(options),
            new BmsTelemetry(),
            new NoOpTelemetryService(),
            NullLogger<BmsPollerWorker>.Instance);

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
        public IReadOnlyDictionary<string, ActivitySourceStatistics> PerSourceStatistics { get; } = new Dictionary<string, ActivitySourceStatistics>();
        public TelemetryStatisticsSnapshot GetSnapshot() => new() { Timestamp = DateTimeOffset.UtcNow, StartTime = StartTime };
        public void Reset() { }
    }
}
