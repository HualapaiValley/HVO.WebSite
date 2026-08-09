using System.Diagnostics;
using System.Net;
using FluentAssertions;
using HVO.Edge.Outbox;
using HVO.Enterprise.Telemetry.Abstractions;
using HVO.Enterprise.Telemetry.HealthChecks;
using HVO.Hardware.DavisVantagePro2.Configuration;
using HVO.Hardware.DavisVantagePro2.Outbox;
using HVO.Hardware.DavisVantagePro2.Telemetry;
using HVO.Hardware.DavisVantagePro2.Workers;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HVO.Hardware.DavisVantagePro2.Tests.Outbox;

[TestClass]
public class OutboxForwarderTests
{
    [TestMethod]
    public async Task ForwardBatchAsync_InvalidPayload_DeadLettersWithoutHttpCall()
    {
        int calls = 0;
        using var client = new HttpClient(new StubHandler(_ =>
        {
            calls++;
            return new HttpResponseMessage(HttpStatusCode.Created);
        }));
        using var forwarder = CreateForwarder(client);
        await using var db = await CreateDbAsync();
        var store = new EdgeOutboxStore<OutboxDbContext>(db);
        var record = CreateRecord("not json");

        await forwarder.ForwardBatchAsync(store, "https://example.test/api/v1/weather/raw/batch", [record], DateTime.UtcNow, CancellationToken.None);

        calls.Should().Be(0);
        record.Status.Should().Be(EdgeOutboxStatus.Failed);
        record.FailureKind.Should().Be(EdgeOutboxFailureKind.Permanent);
        record.LastError.Should().Contain("Invalid outbox payload JSON");
    }

    [TestMethod]
    public async Task ForwardBatchAsync_ApiValidationFailure_RecordsDeadLetterReason()
    {
        var recordedAt = new DateTime(2026, 5, 28, 22, 0, 0, DateTimeKind.Utc);
        using var client = new HttpClient(new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent($"{{\"failed\":[{{\"recordedAt\":\"{recordedAt:O}\",\"error\":\"bad format\"}}]}}")
            }));
        using var forwarder = CreateForwarder(client);
        await using var db = await CreateDbAsync();
        var store = new EdgeOutboxStore<OutboxDbContext>(db);
        var record = CreateRecord("{}", recordedAt);

        await forwarder.ForwardBatchAsync(store, "https://example.test/api/v1/weather/raw/batch", [record], DateTime.UtcNow, CancellationToken.None);

        record.Status.Should().Be(EdgeOutboxStatus.Failed);
        record.FailureKind.Should().Be(EdgeOutboxFailureKind.Permanent);
        record.LastError.Should().Be("bad format");
    }

    [TestMethod]
    public async Task ForwardBatchAsync_TransientFailureAfterMaxAttempts_RecordsRetryExhaustedReason()
    {
        using var client = new HttpClient(new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = new StringContent("cloud server down")
            }));
        using var forwarder = CreateForwarder(
            client,
            new OutboxOptions { ApiEndpoint = "https://example.test/api/v1/weather/raw", ApiKey = "test", MaxRetryAttempts = 1 });
        await using var db = await CreateDbAsync();
        var store = new EdgeOutboxStore<OutboxDbContext>(db);
        var record = CreateRecord("{}");

        await forwarder.ForwardBatchAsync(store, "https://example.test/api/v1/weather/raw/batch", [record], DateTime.UtcNow, CancellationToken.None);

        record.Status.Should().Be(EdgeOutboxStatus.Failed);
        record.FailureKind.Should().Be(EdgeOutboxFailureKind.RetryExhausted);
        record.LastError.Should().Contain("HTTP 503");
        record.LastError.Should().NotContain("cloud server down");
    }

    [TestMethod]
    public async Task SweepAsync_DoesNotRequeueRetryExhaustedRows_AndCountsAllPendingPayloads()
    {
        var calls = 0;
        using var client = new HttpClient(new StubHandler(_ =>
        {
            calls++;
            return new HttpResponseMessage(HttpStatusCode.Created);
        }));
        await using var fixture = await OutboxFixture.CreateAsync();
        fixture.Db.OutboxRecords.Add(new EdgeOutboxRecord
        {
            SourceId = "hvo-davis-01",
            PayloadType = DavisOutboxPayloadTypes.Raw,
            PayloadVersion = DavisOutboxPayloadTypes.RawVersion,
            RecordedAtUtc = new DateTime(2026, 5, 28, 22, 0, 0, DateTimeKind.Utc),
            PayloadJson = "{}",
            Status = EdgeOutboxStatus.Failed,
            FailureKind = EdgeOutboxFailureKind.RetryExhausted,
            AttemptCount = 3,
            LastError = "HTTP 503",
        });
        fixture.Db.OutboxRecords.Add(new EdgeOutboxRecord
        {
            SourceId = "hvo-davis-01",
            PayloadType = DavisOutboxPayloadTypes.Config,
            PayloadVersion = DavisOutboxPayloadTypes.ConfigVersion,
            RecordedAtUtc = new DateTime(2026, 5, 28, 22, 1, 0, DateTimeKind.Utc),
            PayloadJson = "{}",
            Status = EdgeOutboxStatus.Pending,
        });
        await fixture.Db.SaveChangesAsync();

        using var forwarder = CreateForwarder(client, scopeFactory: fixture.Services.GetRequiredService<IServiceScopeFactory>());

        var sent = await forwarder.SweepAsync(CancellationToken.None);

        sent.Should().BeFalse();
        calls.Should().Be(0);
        forwarder.PendingCount.Should().Be(1);
        fixture.Db.ChangeTracker.Clear();
        var retryExhausted = await fixture.Db.OutboxRecords.SingleAsync(r => r.PayloadType == DavisOutboxPayloadTypes.Raw);
        retryExhausted.Status.Should().Be(EdgeOutboxStatus.Failed);
        retryExhausted.FailureKind.Should().Be(EdgeOutboxFailureKind.RetryExhausted);
        retryExhausted.AttemptCount.Should().Be(3);
    }

    private static async Task<OutboxDbContext> CreateDbAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<OutboxDbContext>()
            .UseSqlite(connection)
            .Options;
        var db = new OutboxDbContext(options);
        await EdgeOutboxSqliteDatabaseInitializer.EnsureCreatedAsync(
            db,
            DavisOutboxPayloadTypes.Raw,
            DavisOutboxPayloadTypes.RawVersion);
        return db;
    }

    private static EdgeOutboxRecord CreateRecord(string payload, DateTime? recordedAt = null) => new()
    {
        Id = 42,
        SourceId = "hvo-davis-01",
        PayloadType = DavisOutboxPayloadTypes.Raw,
        PayloadVersion = DavisOutboxPayloadTypes.RawVersion,
        RecordedAtUtc = recordedAt ?? new DateTime(2026, 5, 28, 22, 0, 0, DateTimeKind.Utc),
        PayloadJson = payload,
        Status = EdgeOutboxStatus.Pending,
    };

    private static OutboxForwarder CreateForwarder(
        HttpClient client,
        OutboxOptions? options = null,
        IServiceScopeFactory? scopeFactory = null) => new(
        scopeFactory: scopeFactory!,
        httpFactory: new StubHttpClientFactory(client),
        options: Options.Create(options ?? new OutboxOptions { ApiEndpoint = "https://example.test/api/v1/weather/raw", ApiKey = "test" }),
        telemetry: new DavisTelemetry(),
        telemetryService: new NoOpTelemetryService(),
        runtimeSettings: new RuntimeOutboxSettings(),
        logger: NullLogger<OutboxForwarder>.Instance);

    private sealed class OutboxFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private OutboxFixture(SqliteConnection connection, ServiceProvider services, OutboxDbContext db)
        {
            _connection = connection;
            Services = services;
            Db = db;
        }

        public ServiceProvider Services { get; }
        public OutboxDbContext Db { get; }

        public static async Task<OutboxFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var services = new ServiceCollection()
                .AddDbContext<OutboxDbContext>(options => options.UseSqlite(connection))
                .AddScoped<EdgeOutboxStore<OutboxDbContext>>()
                .BuildServiceProvider();
            var db = services.GetRequiredService<OutboxDbContext>();
            await EdgeOutboxSqliteDatabaseInitializer.EnsureCreatedAsync(
                db,
                DavisOutboxPayloadTypes.Raw,
                DavisOutboxPayloadTypes.RawVersion);
            return new OutboxFixture(connection, services, db);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await Services.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class StubHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responseFactory(request));
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
