using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using HVO.Edge.Contracts;
using HVO.Edge.Contracts.PowerSystem;
using HVO.Edge.Hosting.Telemetry;
using HVO.Edge.Outbox;
using HVO.Hardware.Eg4.Configuration;
using Microsoft.Extensions.Options;

namespace HVO.Hardware.Eg4.Outbox;

public sealed class PowerApiForwarder(
    IServiceScopeFactory scopeFactory,
    IHttpClientFactory httpFactory,
    IOptions<OutboxOptions> options,
    RuntimeOutboxSettings runtimeSettings,
    GatewayTelemetry telemetry,
    TimeProvider timeProvider,
    ILogger<PowerApiForwarder> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> PlaceholderEndpoints =
    [
        string.Empty,
        "https://localhost:5001/api/v1/power/readings",
    ];
    private readonly OutboxOptions _options = options.Value;
    private volatile int _pendingCount;
    private volatile int _failedCount;
    private volatile string? _lastError;
    private volatile int _lastBatchCount;
    private volatile int _permanentFailedCount;
    private volatile int _retryExhaustedCount;
    private long _lastSentAtTicks;

    public int PendingCount => _pendingCount;
    public int FailedCount => _failedCount;
    public string? LastError => _lastError;
    public int LastBatchCount => _lastBatchCount;
    public int PermanentFailedCount => _permanentFailedCount;
    public int RetryExhaustedCount => _retryExhaustedCount;
    public bool IsConfigured => !IsPlaceholderConfig;
    public DateTime? LastSentAtUtc
    {
        get
        {
            var ticks = Volatile.Read(ref _lastSentAtTicks);
            return ticks == 0 ? null : new DateTime(ticks, DateTimeKind.Utc);
        }
    }
    public string? EndpointHost => Uri.TryCreate(_options.ApiEndpoint, UriKind.Absolute, out var uri) ? uri.Host : null;

    private bool IsPlaceholderConfig =>
        PlaceholderEndpoints.Contains(_options.ApiEndpoint) || string.IsNullOrWhiteSpace(_options.ApiKey) ||
        string.Equals(_options.ApiKey, "REPLACE_ME", StringComparison.OrdinalIgnoreCase);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var lastCompactionAt = DateTime.MinValue;
        var lastRetryRequeueAt = DateTime.MinValue;
        while (!stoppingToken.IsCancellationRequested)
        {
            var anyWork = false;
            try
            {
                anyWork = await SweepAsync(stoppingToken);
                var now = timeProvider.GetUtcNow().UtcDateTime;
                if (now - lastRetryRequeueAt >= TimeSpan.FromSeconds(_options.MaxBackoffSeconds))
                {
                    await RequeueRetryExhaustedAsync(stoppingToken);
                    lastRetryRequeueAt = now;
                }
                if ((_options.SentRetentionDays > 0 || _options.FailedRetentionDays > 0) &&
                    now - lastCompactionAt >= TimeSpan.FromHours(24))
                {
                    await CompactAsync(stoppingToken);
                    lastCompactionAt = now;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                _lastError = "Forwarding sweep failed";
                logger.LogError(exception, "EG4 power forwarding sweep failed");
            }

            if (!anyWork)
            {
                try
                {
                    await Task.Delay(
                        TimeSpan.FromSeconds(runtimeSettings.EffectiveSweepIntervalSeconds(_options.SweepIntervalSeconds)),
                        timeProvider,
                        stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            }
        }
    }

    internal async Task<bool> SweepAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<EdgeOutboxStore<OutboxDbContext>>();
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var pending = await store.GetReadyBatchAsync(
            Eg4OutboxPayloadTypes.Reading,
            now,
            runtimeSettings.EffectiveBatchSize(_options.BatchSize),
            cancellationToken);
        await RefreshCountsAsync(store, cancellationToken);
        telemetry.SetOutboxState(_pendingCount, _failedCount);
        if (pending.Count == 0 || IsPlaceholderConfig) return false;

        store.MarkAttempt(pending, now);
        var ready = new List<(EdgeOutboxRecord Record, PowerReadingPayload Payload)>();
        var sentCount = 0;
        Activity? activity = null;
        Stopwatch? stopwatch = null;
        try
        {
            foreach (var record in pending)
            {
                if (TryReadPayload(record, out var payload)) ready.Add((record, payload));
            }
            if (ready.Count > 0)
            {
                var readyJson = ready.Select(item =>
                    (item.Record, JsonSerializer.SerializeToElement(item.Payload, JsonOptions))).ToList();
                var events = CloudEventsForwardingHelper.WrapBatchAsCloudEvents(readyJson, "eg4");
                activity = telemetry.StartOperation(GatewayTelemetryConventions.OperationNames.OutboxForward, ActivityKind.Client);
                stopwatch = Stopwatch.StartNew();
                using var request = new HttpRequestMessage(HttpMethod.Post, _options.ApiEndpoint)
                {
                    Content = JsonContent.Create(events, options: JsonOptions),
                };
                request.AddTraceContext();
                using var response = await httpFactory.CreateClient("PowerApi").SendAsync(request, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadFromJsonAsync<PowerBatchResponse>(JsonOptions, cancellationToken);
                    ValidateBatchResponse(ready.Select(item => item.Record).ToArray(), body);
                    sentCount = MarkBatchResult(store, ready.Select(item => item.Record), body, now);
                    telemetry.RecordForwardBatch(sentCount, ready.Count - sentCount,
                        stopwatch.Elapsed.TotalSeconds, "hvo.power.reading.v1", "validation", activity);
                }
                else
                {
                    var error = $"HTTP {(int)response.StatusCode}";
                    foreach (var record in ready.Select(item => item.Record))
                    {
                        if ((int)response.StatusCode == 400)
                            store.MarkFailed(record, error, EdgeOutboxFailureKind.Permanent);
                        else
                            store.ScheduleRetry(record, error, now, _options.MaxRetryAttempts, _options.MaxBackoffSeconds);
                    }
                    _lastError = error;
                    telemetry.RecordForward(ready.Count, false, stopwatch.Elapsed.TotalSeconds,
                        "hvo.power.reading.v1", "http_status", activity);
                }
            }
        }
        catch (HttpRequestException exception)
        {
            const string error = "HTTP request failed";
            foreach (var record in ready.Select(item => item.Record))
                store.ScheduleRetry(record, error, now, _options.MaxRetryAttempts, _options.MaxBackoffSeconds);
            _lastError = error;
            telemetry.RecordForward(ready.Count, false, stopwatch?.Elapsed.TotalSeconds ?? 0,
                "hvo.power.reading.v1", "http_request", activity);
            logger.LogWarning(exception, "EG4 forwarding HTTP request failed for {Count} record(s)", ready.Count);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            const string error = "Request timed out";
            foreach (var record in ready.Select(item => item.Record))
                store.ScheduleRetry(record, error, now, _options.MaxRetryAttempts, _options.MaxBackoffSeconds);
            _lastError = error;
            telemetry.RecordForward(ready.Count, false, stopwatch?.Elapsed.TotalSeconds ?? 0,
                "hvo.power.reading.v1", "timeout", activity);
        }
        catch (JsonException exception)
        {
            const string error = "Power API response was invalid";
            foreach (var record in ready.Select(item => item.Record))
                store.ScheduleRetry(record, error, now, _options.MaxRetryAttempts, _options.MaxBackoffSeconds);
            _lastError = error;
            telemetry.RecordForward(ready.Count, false, stopwatch?.Elapsed.TotalSeconds ?? 0,
                "hvo.power.reading.v1", "invalid_response", activity);
            logger.LogWarning(exception, "EG4 forwarding response was invalid for {Count} record(s)", ready.Count);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            const string error = "Transient forwarding pipeline failure";
            foreach (var record in ready.Select(item => item.Record))
                store.ScheduleRetry(record, error, now, _options.MaxRetryAttempts, _options.MaxBackoffSeconds);
            _lastError = error;
            telemetry.RecordForward(ready.Count, false, stopwatch?.Elapsed.TotalSeconds ?? 0,
                "hvo.power.reading.v1", "resilience", activity);
            logger.LogWarning(exception, "EG4 forwarding pipeline failed transiently for {Count} record(s)", ready.Count);
        }
        finally
        {
            activity?.Dispose();
        }

        await store.SaveChangesAsync(cancellationToken);
        await RefreshCountsAsync(store, cancellationToken);
        telemetry.SetOutboxState(_pendingCount, _failedCount);
        return sentCount > 0;
    }

    internal async Task RequeueRetryExhaustedAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<EdgeOutboxStore<OutboxDbContext>>();
        var requeued = await store.RequeueRetryExhaustedAsync(cancellationToken);
        if (requeued > 0) logger.LogInformation("Requeued {Count} EG4 retry-exhausted records", requeued);
    }

    internal async Task CompactAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<EdgeOutboxStore<OutboxDbContext>>();
        if (_options.SentRetentionDays > 0)
            await store.CompactSentAsync(TimeSpan.FromDays(_options.SentRetentionDays), cancellationToken);
        if (_options.FailedRetentionDays > 0)
            await store.CompactFailedAsync(TimeSpan.FromDays(_options.FailedRetentionDays), cancellationToken);
    }

    private async Task RefreshCountsAsync(EdgeOutboxStore<OutboxDbContext> store, CancellationToken cancellationToken)
    {
        _pendingCount = await store.CountPendingAsync(Eg4OutboxPayloadTypes.Reading, cancellationToken);
        _failedCount = await store.CountFailedAsync(Eg4OutboxPayloadTypes.Reading, cancellationToken);
        _permanentFailedCount = await store.CountFailedAsync(EdgeOutboxFailureKind.Permanent, cancellationToken);
        _retryExhaustedCount = await store.CountFailedAsync(EdgeOutboxFailureKind.RetryExhausted, cancellationToken);
    }

    private bool TryReadPayload(EdgeOutboxRecord record, out PowerReadingPayload payload)
    {
        try
        {
            payload = JsonSerializer.Deserialize<PowerReadingPayload>(record.PayloadJson, JsonOptions)
                ?? throw new JsonException("Payload deserialized to null.");
            return true;
        }
        catch (JsonException exception)
        {
            const string error = "Outbox payload JSON is invalid";
            record.Status = EdgeOutboxStatus.Failed;
            record.FailureKind = EdgeOutboxFailureKind.Permanent;
            record.LastError = error;
            _lastError = error;
            logger.LogError(exception, "EG4 outbox record {RecordId} contains invalid JSON", record.Id);
            payload = null!;
            return false;
        }
    }

    private int MarkBatchResult(
        EdgeOutboxStore<OutboxDbContext> store,
        IEnumerable<EdgeOutboxRecord> records,
        PowerBatchResponse? response,
        DateTime sentAtUtc)
    {
        var recordList = records.ToArray();
        var failures = response?.Failed ?? [];
        var sentCount = 0;
        foreach (var record in recordList)
        {
            var failure = failures.FirstOrDefault(item =>
                string.Equals(item.SourceId, record.SourceId, StringComparison.Ordinal) &&
                item.RecordedAtUtc == record.RecordedAtUtc);
            if (failure is not null)
                store.MarkFailed(record, "Power API validation rejected the record", EdgeOutboxFailureKind.Permanent);
            else
            {
                store.MarkSent(record, sentAtUtc);
                sentCount++;
            }
        }
        _lastBatchCount = sentCount;
        _lastError = sentCount == recordList.Length ? null : "Power API validation rejected one or more records";
        if (sentCount > 0) Volatile.Write(ref _lastSentAtTicks, sentAtUtc.Ticks);
        return sentCount;
    }

    private static void ValidateBatchResponse(IReadOnlyList<EdgeOutboxRecord> records, PowerBatchResponse? response)
    {
        if (response is null || response.Inserted < 0 || response.Skipped < 0 ||
            response.Inserted + response.Skipped + response.Failed.Count != records.Count)
            throw new JsonException("Power API batch counts do not match the submitted records.");

        var submitted = records.Select(record => (record.SourceId, record.RecordedAtUtc)).ToHashSet();
        var failures = response.Failed.Select(failure => (failure.SourceId, failure.RecordedAtUtc)).ToArray();
        if (failures.Distinct().Count() != failures.Length || failures.Any(failure => !submitted.Contains(failure)))
            throw new JsonException("Power API batch failures do not match unique submitted records.");
    }

    private sealed class PowerBatchResponse
    {
        [JsonPropertyName("inserted")] public int Inserted { get; init; }
        [JsonPropertyName("skipped")] public int Skipped { get; init; }
        [JsonPropertyName("failed")] public List<PowerBatchFailure> Failed { get; init; } = [];
    }

    private sealed class PowerBatchFailure
    {
        [JsonPropertyName("sourceId")] public string SourceId { get; init; } = string.Empty;
        [JsonPropertyName("recordedAtUtc")] public DateTime RecordedAtUtc { get; init; }
    }
}
