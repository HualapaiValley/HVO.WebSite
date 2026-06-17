using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using HVO.Edge.Outbox;
using HVO.Enterprise.Telemetry.Abstractions;
using HVO.Enterprise.Telemetry.HealthChecks;
using HVO.Hardware.JkBms.Bms;
using HVO.Hardware.JkBms.Configuration;
using HVO.Hardware.JkBms.Outbox;
using HVO.Hardware.JkBms.Protocol;
using HVO.Hardware.JkBms.Protocol.Packets;
using HVO.Hardware.JkBms.Protocol.Transport;
using HVO.Hardware.JkBms.Telemetry;
using HVO.Hardware.JkBms.Tests.Fakes;
using HVO.Hardware.JkBms.Workers;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HVO.Hardware.JkBms.Tests.Workers;

[TestClass]
public sealed class BmsPollerWorkerTests
{
    [TestMethod]
    public void Constructor_FiltersDisabledAndInvalidDevices()
    {
        using var fixture = WorkerFixture.Create(new JkBmsOptions
        {
            HciAdapter = "hci0",
            DefaultPollIntervalSeconds = 120,
            Devices =
            [
                new BmsDeviceConfig { Address = "AA:BB:CC:DD:EE:01", Alias = "bank-1", Enabled = true, HciAdapter = "hci1", PollIntervalSeconds = 30 },
                new BmsDeviceConfig { Address = "AA:BB:CC:DD:EE:02", Alias = "bank-2", Enabled = false },
                new BmsDeviceConfig { Address = "", Alias = "missing-address", Enabled = true },
                new BmsDeviceConfig { Address = "AA:BB:CC:DD:EE:03", Alias = "", Enabled = true },
                new BmsDeviceConfig { Address = "AA:BB:CC:DD:EE:04", Alias = "bank-4", Enabled = true },
            ],
        });

        fixture.Worker.DeviceStates.Should().HaveCount(2);
        fixture.Worker.DeviceStates.Select(d => d.Address).Should().Equal("AA:BB:CC:DD:EE:01", "AA:BB:CC:DD:EE:04");
        fixture.Worker.DeviceStates[0].AdapterName.Should().Be("hci1");
        fixture.Worker.DeviceStates[0].PollIntervalSeconds.Should().Be(30);
        fixture.Worker.DeviceStates[1].AdapterName.Should().Be("hci0");
        fixture.Worker.DeviceStates[1].PollIntervalSeconds.Should().Be(120);
    }

    [TestMethod]
    public async Task SuccessfulPoll_MapsCellInfoPacketToReading()
    {
        using var fixture = WorkerFixture.Create();
        var state = DeviceState();
        var packet = CellPacket(totalVoltageMv: 52_000, currentMa: -1_500, socPercent: 82, alarmBitmask: 0);

        await fixture.InvokeEnqueueOutboxAsync(state, packet);

        var row = fixture.Db.OutboxRecords.Single();
        var record = JsonSerializer.Deserialize<BmsIngressRecord>(row.PayloadJson)!;
        record.Reading.Should().NotBeNull();
        record.Reading!.DeviceAddress.Should().Be(state.Address);
        record.Reading.DeviceAlias.Should().Be(state.Alias);
        record.Reading.TotalVoltageMv.Should().Be(52_000);
        record.Reading.CurrentMa.Should().Be(-1_500);
        record.Reading.StateOfChargePercent.Should().Be(82);
        record.Reading.CellCount.Should().Be(4);
        record.Reading.CellVoltagesMv.Should().Equal(3301, 3302, 3303, 3304);
    }

    [TestMethod]
    public async Task ConfigSnapshot_EnqueuedOnlyWhenHashChanges()
    {
        using var fixture = WorkerFixture.Create();
        var state = DeviceState();
        state.LatestSettings = SettingsPacket.Parse(JkBmsProtocol.GetData(TestFrameBuilder.BuildSettingsFrame(cellCount: 16, nominalCapacityMah: 100_000)));

        await fixture.InvokeEnqueueOutboxAsync(state, CellPacket(recordedAtUtc: Utc(2026, 6, 17, 12, 0, 0)));
        await fixture.InvokeEnqueueOutboxAsync(state, CellPacket(recordedAtUtc: Utc(2026, 6, 17, 12, 1, 0)));
        state.LatestSettings = SettingsPacket.Parse(JkBmsProtocol.GetData(TestFrameBuilder.BuildSettingsFrame(cellCount: 15, nominalCapacityMah: 100_000)));
        await fixture.InvokeEnqueueOutboxAsync(state, CellPacket(recordedAtUtc: Utc(2026, 6, 17, 12, 2, 0)));

        fixture.Db.OutboxRecords.Count(r => r.PayloadType == BmsOutboxPayloadTypes.Reading).Should().Be(3);
        fixture.Db.OutboxRecords.Count(r => r.PayloadType == BmsOutboxPayloadTypes.Config).Should().Be(2);
        state.LastSentConfigHash.Should().NotBeNullOrWhiteSpace();
    }

    [TestMethod]
    public async Task DeviceInfoSnapshot_EnqueuedOnlyWhenHashChanges()
    {
        using var fixture = WorkerFixture.Create();
        var state = DeviceState();
        state.LatestDeviceInfo = DeviceInfoPacket.Parse(JkBmsProtocol.GetData(TestFrameBuilder.BuildDeviceInfoFrame(serialNumber: "SN-1")));

        await fixture.InvokeEnqueueOutboxAsync(state, CellPacket(recordedAtUtc: Utc(2026, 6, 17, 12, 0, 0)));
        await fixture.InvokeEnqueueOutboxAsync(state, CellPacket(recordedAtUtc: Utc(2026, 6, 17, 12, 1, 0)));
        state.LatestDeviceInfo = DeviceInfoPacket.Parse(JkBmsProtocol.GetData(TestFrameBuilder.BuildDeviceInfoFrame(serialNumber: "SN-2")));
        await fixture.InvokeEnqueueOutboxAsync(state, CellPacket(recordedAtUtc: Utc(2026, 6, 17, 12, 2, 0)));

        fixture.Db.OutboxRecords.Count(r => r.PayloadType == BmsOutboxPayloadTypes.Reading).Should().Be(3);
        fixture.Db.OutboxRecords.Count(r => r.PayloadType == BmsOutboxPayloadTypes.DeviceInfo).Should().Be(2);
        state.LastSentDeviceInfoHash.Should().NotBeNullOrWhiteSpace();
    }

    [TestMethod]
    public async Task AlarmHandler_CalledOnlyWhenPacketHasAlarms()
    {
        using var fixture = WorkerFixture.Create();
        var state = DeviceState();
        await using var client = new JkBmsClient(new FakeBmsTransport(state.Address), NullLogger<JkBmsClient>.Instance);

        await fixture.InvokeSuccessfulPollAsync(state, client, CellPacket(recordedAtUtc: Utc(2026, 6, 17, 12, 0, 0), alarmBitmask: 0));
        await fixture.InvokeSuccessfulPollAsync(state, client, CellPacket(recordedAtUtc: Utc(2026, 6, 17, 12, 1, 0), alarmBitmask: 0x0001));

        fixture.AlarmHandler.Readings.Should().ContainSingle();
        fixture.AlarmHandler.Readings[0].AlarmBitmask.Should().Be(0x0001);
    }

    private static DevicePollState DeviceState() => new()
    {
        Address = "AA:BB:CC:DD:EE:01",
        Alias = "bank-1",
        AdapterName = "hci0",
        PollIntervalSeconds = 60,
        NextPollAt = DateTime.UtcNow,
    };

    private static CellInfoPacket CellPacket(
        DateTime? recordedAtUtc = null,
        uint totalVoltageMv = 51_000,
        int currentMa = 2_500,
        byte socPercent = 80,
        uint alarmBitmask = 0)
    {
        var packet = CellInfoPacket.Parse(JkBmsProtocol.GetData(TestFrameBuilder.BuildCellInfoFrame(
            cellCount: 4,
            cellVoltagesMv: [3301, 3302, 3303, 3304],
            totalVoltageMv: totalVoltageMv,
            currentMa: currentMa,
            socPercent: socPercent,
            alarmBitmask: alarmBitmask)));
        typeof(CellInfoPacket).GetProperty(nameof(CellInfoPacket.RecordedAtUtc))!
            .SetValue(packet, recordedAtUtc ?? Utc(2026, 6, 17, 12, 0, 0));
        return packet;
    }

    private static DateTime Utc(int year, int month, int day, int hour, int minute, int second) =>
        new(year, month, day, hour, minute, second, DateTimeKind.Utc);

    private sealed class WorkerFixture : IDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly ServiceProvider _provider;

        private WorkerFixture(SqliteConnection connection, ServiceProvider provider, CapturingAlarmHandler alarmHandler)
        {
            _connection = connection;
            _provider = provider;
            AlarmHandler = alarmHandler;
            Db = provider.GetRequiredService<OutboxDbContext>();
            Worker = provider.GetRequiredService<BmsPollerWorker>();
        }

        public OutboxDbContext Db { get; }
        public BmsPollerWorker Worker { get; }
        public CapturingAlarmHandler AlarmHandler { get; }

        public static WorkerFixture Create(JkBmsOptions? options = null)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            connection.Open();
            var alarmHandler = new CapturingAlarmHandler();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContext<OutboxDbContext>(builder => builder.UseSqlite(connection));
            services.AddScoped<EdgeOutboxStore<OutboxDbContext>>();
            services.AddScoped<BmsOutboxWriter>();
            services.AddSingleton<IBmsAlarmHandler>(alarmHandler);
            services.AddSingleton<IBluetoothAdapterCoordinator>(new FakeBluetoothAdapterCoordinator());
            services.AddSingleton<IBmsTransportFactory>(FakeBmsTransportFactory.AlwaysSucceed());
            services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
            services.AddSingleton(new BmsTelemetry());
            services.AddSingleton<ITelemetryService>(new NoOpTelemetryService());
            services.AddSingleton<IOptions<JkBmsOptions>>(Options.Create(options ?? new JkBmsOptions
            {
                Devices = [new BmsDeviceConfig { Address = "AA:BB:CC:DD:EE:01", Alias = "bank-1" }],
            }));
            services.AddSingleton(sp => new BmsPollerWorker(
                sp.GetRequiredService<IBmsTransportFactory>(),
                sp.GetRequiredService<IBluetoothAdapterCoordinator>(),
                sp.GetRequiredService<ILoggerFactory>(),
                sp.GetRequiredService<IServiceScopeFactory>(),
                sp.GetRequiredService<IBmsAlarmHandler>(),
                sp.GetRequiredService<IOptions<JkBmsOptions>>(),
                sp.GetRequiredService<BmsTelemetry>(),
                sp.GetRequiredService<ITelemetryService>(),
                NullLogger<BmsPollerWorker>.Instance));
            var provider = services.BuildServiceProvider();
            provider.GetRequiredService<OutboxDbContext>().Database.EnsureCreated();
            return new WorkerFixture(connection, provider, alarmHandler);
        }

        public async Task InvokeEnqueueOutboxAsync(DevicePollState state, CellInfoPacket packet)
        {
            var method = typeof(BmsPollerWorker).GetMethod("EnqueueOutboxAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var task = (Task<BmsDeviceReading>)method.Invoke(Worker, [state, packet, CancellationToken.None])!;
            await task;
        }

        public async Task InvokeSuccessfulPollAsync(DevicePollState state, JkBmsClient client, CellInfoPacket packet)
        {
            var method = typeof(BmsPollerWorker).GetMethod("OnSuccessfulPollAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var task = (Task)method.Invoke(Worker, [state, client, packet, CancellationToken.None])!;
            await task;
        }

        public void Dispose()
        {
            Db.Dispose();
            _provider.Dispose();
            _connection.Dispose();
        }
    }

    private sealed class CapturingAlarmHandler : IBmsAlarmHandler
    {
        public List<BmsDeviceReading> Readings { get; } = [];

        public Task HandleAsync(BmsDeviceReading reading, CancellationToken ct)
        {
            Readings.Add(reading);
            return Task.CompletedTask;
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
        public System.Diagnostics.Activity? Activity => null;
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
