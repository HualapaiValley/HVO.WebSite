using System.Diagnostics;
using Bunit;
using FluentAssertions;
using HVO.Enterprise.Telemetry.Abstractions;
using HVO.Enterprise.Telemetry.HealthChecks;
using HVO.Hardware.JkBms.Bms;
using HVO.Hardware.JkBms.Configuration;
using HVO.Hardware.JkBms.Protocol.Packets;
using HVO.Hardware.JkBms.Telemetry;
using HVO.Hardware.JkBms.Tests.Fakes;
using HVO.Hardware.JkBms.Workers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HVO.Hardware.JkBms.Tests.Components;

[TestClass]
public sealed class DeviceDetailPageBunitTests : BunitContext
{
    [TestInitialize]
    public void ConfigureDisplayTimeZone()
    {
        Services.AddSingleton(new JkBmsDisplayTimeZoneResolver(Options.Create(new JkBmsOptions())));
    }

    [TestMethod]
    public void RendersSelectedBankTelemetry()
    {
        var poller = CreatePoller();
        poller.DeviceStates[0].IsSessionConnected = true;
        poller.DeviceStates[0].LastPollAt = new DateTime(2026, 6, 17, 12, 0, 0, DateTimeKind.Utc);
        poller.DeviceStates[0].LatestReading = CreateReading([3380, 3381, 3382, 3379]);
        Services.AddSingleton(poller);
        Services.AddSingleton(NullLogger<HVO.Hardware.JkBms.Components.Pages.DeviceDetail>.Instance);
        JSInterop.Mode = JSRuntimeMode.Loose;

        var component = Render<HVO.Hardware.JkBms.Components.Pages.DeviceDetail>(parameters => parameters.Add(p => p.Address, "AA:BB:CC:DD:EE:01"));

        component.Markup.Should().Contain("bank-1a");
        component.Markup.Should().Contain("Pack summary");
        component.Markup.Should().Contain("State of charge");
        component.Markup.Should().Contain("87%");
        component.Markup.Should().Contain("Out of pack");
        component.Markup.Should().Contain("/device/AA:BB:CC:DD:EE:01/admin");
        component.Markup.Should().Contain("No alarms active");
    }

    [TestMethod]
    public void RendersNotFoundWhenAddressUnknown()
    {
        Services.AddSingleton(CreatePoller());
        Services.AddSingleton(NullLogger<HVO.Hardware.JkBms.Components.Pages.DeviceDetail>.Instance);

        var component = Render<HVO.Hardware.JkBms.Components.Pages.DeviceDetail>(parameters => parameters.Add(p => p.Address, "AA:BB:CC:DD:EE:99"));

        component.Markup.Should().Contain("Device 'AA:BB:CC:DD:EE:99' not found in configured devices.");
    }

    [TestMethod]
    public void RendersCellChartContainerWhenCellVoltagesExist()
    {
        var poller = CreatePoller();
        poller.DeviceStates[0].LatestReading = CreateReading([3380, 3381, 3382, 3379]);
        Services.AddSingleton(poller);
        Services.AddSingleton(NullLogger<HVO.Hardware.JkBms.Components.Pages.DeviceDetail>.Instance);
        JSInterop.Mode = JSRuntimeMode.Loose;

        var component = Render<HVO.Hardware.JkBms.Components.Pages.DeviceDetail>(parameters => parameters.Add(p => p.Address, "AA:BB:CC:DD:EE:01"));

        component.Markup.Should().Contain("Cell voltage profile");
        component.Find("#jk-cell-chart").Should().NotBeNull();
    }

    private static CellInfoPacket CreateReading(IReadOnlyList<ushort> cellVoltages)
        => new()
        {
            CellVoltagesMv = cellVoltages,
            CellCount = (byte)cellVoltages.Count,
            MaxVoltageCellIndex = 3,
            MinVoltageCellIndex = 4,
            TotalVoltageMv = 54_120,
            CurrentMa = -4_200,
            DeltaCellVoltageMv = 8,
            BatteryTemperature1C = 25.2,
            BatteryTemperature2C = 24.8,
            PowerTubeTemperatureC = 27.1,
            StateOfChargePercent = 87,
            RemainingCapacityMah = 243_000,
            NominalCapacityMah = 280_000,
            CycleCount = 42,
            CycleCapacityMah = 1_000_000,
            StateOfHealthPercent = 99,
            RecordedAtUtc = DateTime.UtcNow.AddSeconds(-10),
        };

    private static BmsPollerWorker CreatePoller()
        => new(
            FakeBmsTransportFactory.AlwaysSucceed(),
            new FakeBluetoothAdapterCoordinator(),
            NullLoggerFactory.Instance,
            new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            new NullAlarmHandler(),
            Options.Create(new JkBmsOptions
            {
                DefaultPollIntervalSeconds = 60,
                Devices = [new BmsDeviceConfig { Address = "AA:BB:CC:DD:EE:01", Alias = "bank-1a" }],
            }),
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
