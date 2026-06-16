using System.Net;
using System.Reflection;
using System.Diagnostics;
using HVO.Enterprise.Telemetry.Abstractions;
using HVO.Enterprise.Telemetry.HealthChecks;
using HVO.Hardware.DavisVantagePro2.Configuration;
using HVO.Hardware.DavisVantagePro2.Outbox;
using HVO.Hardware.DavisVantagePro2.Telemetry;
using HVO.Hardware.DavisVantagePro2.Workers;
using FluentAssertions;
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
        var record = CreateRecord("not json");

        await InvokeForwardBatchAsync(forwarder, client, [record]);

        calls.Should().Be(0);
        record.Status.Should().Be(OutboxStatus.Failed);
        record.FailureKind.Should().Be(OutboxFailureKind.InvalidPayload);
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
        var record = CreateRecord("{}", recordedAt);

        await InvokeForwardBatchAsync(forwarder, client, [record]);

        record.Status.Should().Be(OutboxStatus.Failed);
        record.FailureKind.Should().Be(OutboxFailureKind.ApiValidation);
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
        var record = CreateRecord("{}");

        await InvokeForwardBatchAsync(forwarder, client, [record]);

        record.Status.Should().Be(OutboxStatus.Failed);
        record.FailureKind.Should().Be(OutboxFailureKind.TransientExhausted);
        record.LastError.Should().Contain("Giving up after 1 attempts");
        record.LastError.Should().Contain("HTTP 503");
    }

    private static OutboxRecord CreateRecord(string payload, DateTime? recordedAt = null) => new()
    {
        Id = 42,
        RecordedAtUtc = recordedAt ?? new DateTime(2026, 5, 28, 22, 0, 0, DateTimeKind.Utc),
        Payload = payload,
        Status = OutboxStatus.Pending,
    };

    private static async Task InvokeForwardBatchAsync(OutboxForwarder forwarder, HttpClient client, List<OutboxRecord> records)
    {
        var method = typeof(OutboxForwarder).GetMethod("ForwardBatchAsync", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("ForwardBatchAsync was not found.");
        var task = (Task?)method.Invoke(forwarder, [client, records, CancellationToken.None])
            ?? throw new InvalidOperationException("ForwardBatchAsync did not return a task.");
        await task;
    }

    private static OutboxForwarder CreateForwarder(HttpClient client, OutboxOptions? options = null) => new(
        scopeFactory: null!,
        httpFactory: new StubHttpClientFactory(client),
        options: Options.Create(options ?? new OutboxOptions { ApiEndpoint = "https://example.test/api/v1/weather/raw", ApiKey = "test" }),
        telemetry: new DavisTelemetry(),
        telemetryService: new NoOpTelemetryService(),
        logger: NullLogger<OutboxForwarder>.Instance);

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
