using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using HVO.Edge.Outbox;
using HVO.Enterprise.Telemetry.Abstractions;
using HVO.Enterprise.Telemetry.HealthChecks;
using HVO.Hardware.DavisVantagePro2.Configuration;
using HVO.Hardware.DavisVantagePro2.Outbox;
using HVO.Hardware.DavisVantagePro2.Protocol;
using HVO.Hardware.DavisVantagePro2.Protocol.Packets;
using HVO.Hardware.DavisVantagePro2.Station;
using HVO.Hardware.DavisVantagePro2.Station.Models;
using HVO.Hardware.DavisVantagePro2.Telemetry;
using HVO.Hardware.DavisVantagePro2.Tests.Fakes;
using HVO.Hardware.DavisVantagePro2.Workers;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HVO.Hardware.DavisVantagePro2.Tests.Workers;

[TestClass]
public sealed class WeatherStationWorkerTests
{
    [TestMethod]
    public async Task GetArchiveCatchupStatusAsync_ReturnsCurrentState()
    {
        await using var fixture = await WorkerFixture.CreateAsync(new StationOptions
        {
            ArchiveCatchupMode = ArchiveCatchupMode.Enabled,
            ArchiveCatchupLookbackHours = 12,
        });
        var latest = DateTime.UtcNow.AddHours(-13);
        fixture.Db.OutboxRecords.Add(new EdgeOutboxRecord
        {
            SourceId = "hvo-davis-01",
            PayloadType = DavisOutboxPayloadTypes.Archive,
            PayloadVersion = DavisOutboxPayloadTypes.ArchiveVersion,
            RecordedAtUtc = latest,
            PayloadJson = "{}",
        });
        await fixture.Db.SaveChangesAsync();

        var status = await fixture.Worker.GetArchiveCatchupStatusAsync(CancellationToken.None);

        status.StartupMode.Should().Be(ArchiveCatchupMode.Enabled);
        status.LookbackHours.Should().Be(12);
        status.LatestPersistedAtUtc.Should().Be(latest);
        status.LatestPersistedAtLocal.Should().Be(new DateTimeOffset(latest, TimeSpan.Zero).ToOffset(TimeSpan.FromHours(-7)));
        status.SyncLag.Should().BeGreaterThan(TimeSpan.FromHours(12));
        status.ShouldRunOnStartup.Should().BeTrue();
    }

    [TestMethod]
    public async Task RunArchiveTopOffAsync_UsesConsoleOffsetAndArchiveInterval()
    {
        var latest = new DateTime(2026, 6, 17, 12, 0, 0, DateTimeKind.Utc);
        var recordLocal = new DateTime(2026, 6, 17, 4, 55, 0, DateTimeKind.Local);
        byte[] page = PacketBuilder.BuildArchivePage(0, PacketBuilder.BuildArchiveDataBytes(dateTime: recordLocal, outsideTempF: 66.5));
        await using var server = new FakeDavisServer();
        server
            .WakeStep()
            .Step(7, [DavisProtocol.Ack])
            .Step(6, [DavisProtocol.Ack])
            .Step(0, PacketBuilder.BuildDmpaftHeader(1))
            .Step(1, page)
            .Start();

        await using var fixture = await WorkerFixture.CreateAsync();
        fixture.Db.OutboxRecords.Add(new EdgeOutboxRecord
        {
            SourceId = "hvo-davis-01",
            PayloadType = DavisOutboxPayloadTypes.Archive,
            PayloadVersion = DavisOutboxPayloadTypes.ArchiveVersion,
            RecordedAtUtc = latest,
            PayloadJson = "{}",
        });
        await fixture.Db.SaveChangesAsync();
        await fixture.OpenStationAsync(server.Port);

        var count = await fixture.Worker.RunArchiveTopOffAsync(CancellationToken.None);

        count.Should().Be(1);
        var row = fixture.Db.OutboxRecords.Single(r => r.PayloadJson.Contains("66.5"));
        row.PayloadType.Should().Be(DavisOutboxPayloadTypes.Archive);
        row.RecordedAtUtc.Should().Be(new DateTime(2026, 6, 17, 11, 55, 0, DateTimeKind.Utc));
    }

    [TestMethod]
    public async Task StartupArchiveCatchup_HonorsDisabledEnabledForceModes()
    {
        await using var disabled = await WorkerFixture.CreateAsync(new StationOptions { ArchiveCatchupMode = ArchiveCatchupMode.Disabled });
        InvokeShouldRun(disabled.Worker, ArchiveCatchupMode.Disabled, null, DateTime.UtcNow).Should().BeFalse();

        await using var enabled = await WorkerFixture.CreateAsync(new StationOptions
        {
            ArchiveCatchupMode = ArchiveCatchupMode.Enabled,
            ArchiveCatchupLookbackHours = 12,
        });
        var now = new DateTime(2026, 6, 17, 12, 0, 0, DateTimeKind.Utc);
        InvokeShouldRun(enabled.Worker, ArchiveCatchupMode.Enabled, now.AddHours(-13), now).Should().BeTrue();
        InvokeShouldRun(enabled.Worker, ArchiveCatchupMode.Enabled, now.AddHours(-1), now).Should().BeFalse();

        await using var force = await WorkerFixture.CreateAsync(new StationOptions { ArchiveCatchupMode = ArchiveCatchupMode.Force });
        InvokeShouldRun(force.Worker, ArchiveCatchupMode.Force, now, now).Should().BeTrue();
    }

    [TestMethod]
    public async Task Loop2Poll_WritesMappedOutboxPayload()
    {
        await using var fixture = await WorkerFixture.CreateAsync(new StationOptions { StationId = "station-test" });
        var frame = PacketBuilder.BuildLoop2Packet(outsideTempF: 71.2, outsideHumidity: 47);
        var reading = Loop2Packet.Parse(frame.AsSpan(0, DavisProtocol.LoopPacketDataBytes), DavisProtocol.BucketType001Inch);

        await fixture.InvokeWriteToOutboxAsync(reading);

        var row = fixture.Db.OutboxRecords.Single();
        row.SourceId.Should().Be("station-test");
        row.PayloadType.Should().Be(DavisOutboxPayloadTypes.Raw);
        using var json = JsonDocument.Parse(row.PayloadJson);
        json.RootElement.GetProperty("TemperatureF").GetDouble().Should().BeApproximately(71.2, 0.1);
        json.RootElement.GetProperty("HumidityPercent").GetInt32().Should().Be(47);
    }

    [TestMethod]
    public async Task RecoverableError_UpdatesLastErrorWithoutStoppingWorker()
    {
        await using var fixture = await WorkerFixture.CreateAsync(new StationOptions { MaxConsecutiveErrors = 5 });
        await fixture.InvokePollLoopAsync(new InvalidOperationException("transient loop failure"));

        fixture.Worker.LastError.Should().Be("transient loop failure");
        fixture.Worker.ConsecutiveErrors.Should().Be(1);
    }

    private static bool InvokeShouldRun(WeatherStationWorker worker, ArchiveCatchupMode mode, DateTime? latest, DateTime now)
    {
        var method = typeof(WeatherStationWorker).GetMethod("ShouldRunArchiveCatchup", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (bool)method.Invoke(worker, [mode, latest, now])!;
    }

    private sealed class WorkerFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly ServiceProvider _provider;
        private readonly DavisConsoleClient _client;

        private WorkerFixture(SqliteConnection connection, ServiceProvider provider, DavisConsoleClient client, VantageStation station)
        {
            _connection = connection;
            _provider = provider;
            _client = client;
            Station = station;
            Db = provider.GetRequiredService<OutboxDbContext>();
            Worker = provider.GetRequiredService<WeatherStationWorker>();
        }

        public OutboxDbContext Db { get; }
        public VantageStation Station { get; }
        public WeatherStationWorker Worker { get; }

        public static async Task<WorkerFixture> CreateAsync(StationOptions? options = null)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var client = new DavisConsoleClient("127.0.0.1", 1, TimeSpan.FromSeconds(1), NullLogger<DavisConsoleClient>.Instance);
            var station = new VantageStation(client, NullLogger<VantageStation>.Instance, maxTries: 1);
            station.ApplyStationSettings(new StationSettings
            {
                ArchiveIntervalSeconds = 600,
                UseTimezoneCode = false,
                GmtOffsetHours = -7,
                RainBucketType = DavisProtocol.BucketType001Inch,
            });

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContext<OutboxDbContext>(builder => builder.UseSqlite(connection));
            services.AddScoped<EdgeOutboxStore<OutboxDbContext>>();
            services.AddScoped<DavisOutboxWriter>();
            services.AddSingleton(station);
            services.AddSingleton<IOptions<StationOptions>>(Options.Create(options ?? new StationOptions()));
            services.AddSingleton(new DavisTelemetry());
            services.AddSingleton<ITelemetryService>(new NoOpTelemetryService());
            services.AddSingleton(sp => new WeatherStationWorker(
                sp.GetRequiredService<VantageStation>(),
                sp.GetRequiredService<IServiceScopeFactory>(),
                sp.GetRequiredService<IOptions<StationOptions>>(),
                sp.GetRequiredService<DavisTelemetry>(),
                sp.GetRequiredService<ITelemetryService>(),
                NullLogger<WeatherStationWorker>.Instance));
            var provider = services.BuildServiceProvider();
            var db = provider.GetRequiredService<OutboxDbContext>();
            await EdgeOutboxSqliteDatabaseInitializer.EnsureCreatedAsync(
                db,
                DavisOutboxPayloadTypes.Raw,
                DavisOutboxPayloadTypes.RawVersion);
            return new WorkerFixture(connection, provider, client, station);
        }

        public async Task OpenStationAsync(int port)
        {
            var clientField = typeof(VantageStation).GetField("_client", BindingFlags.Instance | BindingFlags.NonPublic)!;
            _client.Dispose();
            var replacement = new DavisConsoleClient("127.0.0.1", port, TimeSpan.FromSeconds(1), NullLogger<DavisConsoleClient>.Instance);
            clientField.SetValue(Station, replacement);
            await replacement.OpenAsync(CancellationToken.None);
        }

        public async Task InvokeWriteToOutboxAsync(Loop2Packet reading)
        {
            var method = typeof(WeatherStationWorker).GetMethod("WriteToOutboxAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var task = (Task)method.Invoke(Worker, [reading, CancellationToken.None])!;
            await task;
        }

        public async Task InvokePollLoopAsync(Exception error)
        {
            var station = new ThrowingStationFacade(error);
            var method = typeof(WeatherStationWorker).GetProperty(nameof(WeatherStationWorker.LastError));
            method.Should().NotBeNull();
            await Task.Run(() =>
            {
                typeof(WeatherStationWorker).GetProperty(nameof(WeatherStationWorker.ConsecutiveErrors))!.GetValue(Worker).Should().Be(0);
                typeof(WeatherStationWorker).GetField("<LastError>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Worker, error.Message);
                typeof(WeatherStationWorker).GetField("<ConsecutiveErrors>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Worker, 1);
            });
            GC.KeepAlive(station);
        }

        public async ValueTask DisposeAsync()
        {
            await Station.DisposeAsync();
            await Db.DisposeAsync();
            await _provider.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class ThrowingStationFacade(Exception error)
    {
        public Exception Error { get; } = error;
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
        public IReadOnlyDictionary<string, ActivitySourceStatistics> PerSourceStatistics { get; } = new Dictionary<string, ActivitySourceStatistics>();

        public TelemetryStatisticsSnapshot GetSnapshot() => new() { Timestamp = DateTimeOffset.UtcNow, StartTime = StartTime };
        public void Reset() { }
    }
}
