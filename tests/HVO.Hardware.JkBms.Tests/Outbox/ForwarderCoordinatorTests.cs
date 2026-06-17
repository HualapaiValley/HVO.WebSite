using System.Diagnostics;
using System.Reflection;
using FluentAssertions;
using HVO.Edge.Outbox;
using HVO.Enterprise.Telemetry.Abstractions;
using HVO.Enterprise.Telemetry.HealthChecks;
using HVO.Hardware.JkBms.Outbox;
using HVO.Hardware.JkBms.Telemetry;
using HVO.Hardware.JkBms.Workers;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HVO.Hardware.JkBms.Tests.Outbox;

[TestClass]
public sealed class ForwarderCoordinatorTests
{
    [TestMethod]
    public async Task SweepAsync_DrainsStandaloneConfigAndDeviceInfo_WhenNoReadingsArePending()
    {
        await using var fixture = await OutboxFixture.CreateAsync();
        await fixture.Store.EnqueueAsync(new EdgeOutboxMessage(
            SourceId: "AA:BB:CC:DD:EE:FF",
            DeviceId: "bank-1a",
            RecordedAtUtc: new DateTime(2026, 6, 16, 12, 0, 0, DateTimeKind.Utc),
            PayloadType: BmsOutboxPayloadTypes.Config,
            PayloadVersion: BmsOutboxPayloadTypes.ConfigVersion,
            PayloadJson: "{}"), CancellationToken.None);
        await fixture.Store.EnqueueAsync(new EdgeOutboxMessage(
            SourceId: "AA:BB:CC:DD:EE:FF",
            DeviceId: "bank-1a",
            RecordedAtUtc: new DateTime(2026, 6, 16, 12, 0, 1, DateTimeKind.Utc),
            PayloadType: BmsOutboxPayloadTypes.DeviceInfo,
            PayloadVersion: BmsOutboxPayloadTypes.DeviceInfoVersion,
            PayloadJson: "{}"), CancellationToken.None);

        var coordinator = new ForwarderCoordinator(
            fixture.Services.GetRequiredService<IServiceScopeFactory>(),
            [],
            Options.Create(new OutboxOptions { BatchSize = 50, SweepIntervalSeconds = 5 }),
            new BmsTelemetry(),
            new NoOpTelemetryService(),
            NullLogger<ForwarderCoordinator>.Instance);

        await InvokeSweepAsync(coordinator);

        fixture.Context.ChangeTracker.Clear();
        var rows = await fixture.Context.OutboxRecords.OrderBy(r => r.RecordedAtUtc).ToListAsync();
        rows.Should().HaveCount(2);
        rows.Should().OnlyContain(r => r.Status == EdgeOutboxStatus.Sent);
        coordinator.PendingCount.Should().Be(0);
    }

    private static async Task InvokeSweepAsync(ForwarderCoordinator coordinator)
    {
        var method = typeof(ForwarderCoordinator).GetMethod("SweepAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        var task = (Task)method!.Invoke(coordinator, [CancellationToken.None])!;
        await task;
    }

    private sealed class OutboxFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private OutboxFixture(SqliteConnection connection, ServiceProvider services, OutboxDbContext context)
        {
            _connection = connection;
            Services = services;
            Context = context;
            Store = new EdgeOutboxStore<OutboxDbContext>(context);
        }

        public ServiceProvider Services { get; }
        public OutboxDbContext Context { get; }
        public EdgeOutboxStore<OutboxDbContext> Store { get; }

        public static async Task<OutboxFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var services = new ServiceCollection()
                .AddDbContext<OutboxDbContext>(options => options.UseSqlite(connection))
                .AddScoped<EdgeOutboxStore<OutboxDbContext>>()
                .BuildServiceProvider();

            var context = services.GetRequiredService<OutboxDbContext>();
            await EdgeOutboxSqliteDatabaseInitializer.EnsureCreatedAsync(
                context,
                BmsOutboxPayloadTypes.Reading,
                BmsOutboxPayloadTypes.ReadingVersion);
            return new OutboxFixture(connection, services, context);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await Services.DisposeAsync();
            await _connection.DisposeAsync();
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
