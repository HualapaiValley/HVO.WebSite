using System.Diagnostics;
using Bunit;
using FluentAssertions;
using HVO.Edge.Outbox;
using HVO.Enterprise.Telemetry.Abstractions;
using HVO.Enterprise.Telemetry.HealthChecks;
using HVO.Hardware.JkBms.Bms;
using HVO.Hardware.JkBms.Configuration;
using HVO.Hardware.JkBms.Outbox;
using HVO.Hardware.JkBms.Outbox.Forwarders;
using HVO.Hardware.JkBms.Protocol.Packets;
using HVO.Hardware.JkBms.Telemetry;
using HVO.Hardware.JkBms.Tests.Fakes;
using HVO.Hardware.JkBms.Workers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HVO.Hardware.JkBms.Tests.Components;

[TestClass]
public sealed class JkBmsStatusPageBunitTests : BunitContext
{
    [TestMethod]
    public void RendersGatewaySummaryAndBankStates()
    {
        var poller = CreatePoller([
            new BmsDeviceConfig { Address = "AA:BB:CC:DD:EE:01", Alias = "bank-1a", PollIntervalSeconds = 30 },
            new BmsDeviceConfig { Address = "AA:BB:CC:DD:EE:02", Alias = "bank-1b", PollIntervalSeconds = 45 },
        ]);
        poller.DeviceStates[0].IsSessionConnected = true;
        poller.DeviceStates[0].LastPollAt = new DateTime(2026, 6, 17, 12, 0, 0, DateTimeKind.Utc);
        poller.DeviceStates[0].LatestReading = CreateReading(92, 54_200, -12_000, 6, 24.5);
        poller.DeviceStates[0].LatestSettings = new SettingsPacket { NominalCapacityMah = 280_000 };
        poller.DeviceStates[1].LastPollAt = new DateTime(2026, 6, 17, 11, 59, 0, DateTimeKind.Utc);
        poller.DeviceStates[1].LatestReading = CreateReading(81, 53_900, 8_000, 12, 29.1);
        poller.DeviceStates[1].LatestSettings = new SettingsPacket { NominalCapacityMah = 280_000 };
        Services.AddSingleton(poller);
        Services.AddSingleton(CreateForwarder());
        Services.AddSingleton(NullLogger<HVO.Hardware.JkBms.Components.Pages.Status>.Instance);

        var component = Render<HVO.Hardware.JkBms.Components.Pages.Status>();

        component.Markup.Should().Contain("JK BMS fleet overview");
        component.Markup.Should().Contain("Connected banks");
        component.Markup.Should().Contain("1 / 2");
        component.Markup.Should().Contain("bank-1a");
        component.Markup.Should().Contain("bank-1b");
        component.Markup.Should().Contain("Connected");
    }

    [TestMethod]
    public void RendersEmptyDeviceState()
    {
        Services.AddSingleton(CreatePoller([]));
        Services.AddSingleton(CreateForwarder());
        Services.AddSingleton(NullLogger<HVO.Hardware.JkBms.Components.Pages.Status>.Instance);

        var component = Render<HVO.Hardware.JkBms.Components.Pages.Status>();

        component.Markup.Should().Contain("No devices configured.");
        component.Markup.Should().Contain("Charge bars appear after the fleet reports its first readings.");
        component.Markup.Should().Contain("0 / 0");
    }

    private static CellInfoPacket CreateReading(ushort soc, uint voltageMv, int currentMa, ushort deltaMv, double temperatureC)
        => new()
        {
            CellVoltagesMv = [3380, 3382, 3378, 3381],
            CellCount = 4,
            TotalVoltageMv = voltageMv,
            CurrentMa = currentMa,
            DeltaCellVoltageMv = deltaMv,
            BatteryTemperature1C = temperatureC,
            StateOfChargePercent = soc,
            RemainingCapacityMah = 250_000,
            NominalCapacityMah = 280_000,
            StateOfHealthPercent = 99,
            RecordedAtUtc = new DateTime(2026, 6, 17, 12, 0, 0, DateTimeKind.Utc),
        };

    private static BmsPollerWorker CreatePoller(IReadOnlyList<BmsDeviceConfig> devices)
        => new(
            FakeBmsTransportFactory.AlwaysSucceed(),
            new FakeBluetoothAdapterCoordinator(),
            NullLoggerFactory.Instance,
            new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            new NullAlarmHandler(),
            Options.Create(new JkBmsOptions { DefaultPollIntervalSeconds = 60, Devices = devices.ToList() }),
            new BmsTelemetry(),
            new NoOpTelemetryService(),
            NullLogger<BmsPollerWorker>.Instance);

    private static ForwarderCoordinator CreateForwarder()
        => new(
            new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            Array.Empty<IReadingForwarder>(),
            Options.Create(new OutboxOptions()),
            new BmsTelemetry(),
            new NoOpTelemetryService(),
            NullLogger<ForwarderCoordinator>.Instance);

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
