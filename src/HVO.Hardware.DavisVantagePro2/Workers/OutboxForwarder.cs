using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using HVO.Edge.Contracts;
using HVO.Edge.Outbox;
using HVO.Enterprise.Telemetry.Abstractions;
using HVO.Hardware.DavisVantagePro2.Configuration;
using HVO.Hardware.DavisVantagePro2.Outbox;
using HVO.Hardware.DavisVantagePro2.Telemetry;
using Microsoft.Extensions.Options;

namespace HVO.Hardware.DavisVantagePro2.Workers;

/// <summary>Sweeps the Davis outbox and forwards readings to the website weather API.</summary>
public sealed class OutboxForwarder(
    IServiceScopeFactory scopeFactory,
    IHttpClientFactory httpFactory,
    IOptions<OutboxOptions> options,
    DavisTelemetry telemetry,
    ITelemetryService telemetryService,
    RuntimeOutboxSettings runtimeSettings,
    ILogger<OutboxForwarder> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly OutboxOptions _options = options.Value;
    private readonly RuntimeOutboxSettings _runtimeSettings = runtimeSettings;

    public int PendingCount { get; private set; }
    public int FailedCount { get; private set; }
    public DateTime? LastSentAt { get; private set; }
    public string? LastError { get; private set; }
    public int LastBatchCount { get; private set; }

    public event Action? SweptCompleted;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("OutboxForwarder starting. Remote forwarding is configured.");
        var lastCompactionAt = DateTime.MinValue;

        while (!stoppingToken.IsCancellationRequested)
        {
            bool anySent = false;
            try
            {
                anySent = await SweepAsync(stoppingToken);
                if ((DateTime.UtcNow - lastCompactionAt).TotalHours >= 24)
                {
                    await CompactAsync(stoppingToken);
                    lastCompactionAt = DateTime.UtcNow;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "OutboxForwarder sweep error");
            }

            if (anySent)
            {
                try
                {
                    await using var requeueScope = scopeFactory.CreateAsyncScope();
                    var requeueStore = requeueScope.ServiceProvider.GetRequiredService<EdgeOutboxStore<OutboxDbContext>>();
                    var requeued = await requeueStore.RequeueRetryExhaustedAsync(stoppingToken);
                    if (requeued > 0)
                        logger.LogInformation("Requeued {Count} RetryExhausted Davis outbox records", requeued);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Requeue of RetryExhausted Davis outbox records failed (non-fatal)");
                }
            }

            if (!anySent)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(_runtimeSettings.EffectiveSweepIntervalSeconds(_options.SweepIntervalSeconds)), stoppingToken);
                }
                catch (OperationCanceledException) { break; }
            }
        }

        logger.LogInformation("OutboxForwarder stopped");
    }

    internal async Task<bool> SweepAsync(CancellationToken ct)
    {
        using var sweepScope = telemetryService.StartOperation("Davis.Outbox.Sweep");
        await using var serviceScope = scopeFactory.CreateAsyncScope();
        var store = serviceScope.ServiceProvider.GetRequiredService<EdgeOutboxStore<OutboxDbContext>>();
        var now = DateTime.UtcNow;

        var rawSent = await SweepPayloadTypeAsync(store, DavisOutboxPayloadTypes.Raw, BuildWeatherBatchEndpoint(), now, ct);
        var archiveSent = await SweepPayloadTypeAsync(store, DavisOutboxPayloadTypes.Archive, BuildWeatherBatchEndpoint(), now, ct);

        PendingCount = await store.CountPendingAsync(ct);
        FailedCount = await store.CountFailedAsync(ct);
        telemetry.SetOutboxQueueDepth(PendingCount);

        sweepScope
            .WithTag("pending", PendingCount)
            .WithTag("failed", FailedCount)
            .WithTag("records_forwarded", LastBatchCount)
            .Succeed();
        SweptCompleted?.Invoke();
        return rawSent || archiveSent;
    }

    internal async Task<bool> SweepPayloadTypeAsync(
        EdgeOutboxStore<OutboxDbContext> store,
        string payloadType,
        string endpoint,
        DateTime now,
        CancellationToken ct)
    {
        var pending = await store.GetReadyBatchAsync(payloadType, now, _runtimeSettings.EffectiveBatchSize(_options.BatchSize), ct);
        if (pending.Count == 0)
            return false;

        await ForwardBatchAsync(store, endpoint, pending, now, ct);
        await store.SaveChangesAsync(ct);
        return LastBatchCount > 0;
    }

    internal async Task ForwardBatchAsync(
        EdgeOutboxStore<OutboxDbContext> store,
        string endpoint,
        IReadOnlyList<EdgeOutboxRecord> records,
        DateTime now,
        CancellationToken ct)
    {
        var ready = new List<(EdgeOutboxRecord Record, JsonElement Payload)>(records.Count);
        foreach (var record in records)
        {
            if (TryReadPayload(record, out var payload))
            {
                ready.Add((record, payload));
                continue;
            }

            store.MarkFailed(record, "Invalid outbox payload JSON; record cannot be forwarded.", EdgeOutboxFailureKind.Permanent);
            logger.LogError("Dead-lettering outbox record {Id} ({RecordedAt}) because payload JSON is invalid", record.Id, record.RecordedAtUtc);
        }

        store.MarkAttempt(records, now);
        if (ready.Count == 0)
        {
            LastError = "One or more outbox records have invalid payload JSON.";
            LastBatchCount = 0;
            return;
        }

        // Wrap valid records in CloudEvents 1.0 envelopes for standards-compliant delivery
        var cloudEvents = CloudEventsForwardingHelper.WrapBatchAsCloudEvents(ready, "davis");

        var sw = Stopwatch.StartNew();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = JsonContent.Create(cloudEvents, options: JsonOptions),
            };
            request.AddTraceContext();

            using var response = await httpFactory
                .CreateClient("WeatherApi")
                .SendAsync(request, ct);
            telemetry.OutboxForwardLatencyMs.Record(sw.Elapsed.TotalMilliseconds);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<BatchResponseDto>(JsonOptions, ct);
                MarkBatchResult(store, ready.Select(x => x.Record), result, DateTime.UtcNow);
                return;
            }

            var error = $"HTTP {(int)response.StatusCode}";
            foreach (var record in ready.Select(x => x.Record))
                store.ScheduleRetry(record, error, now, _options.MaxRetryAttempts, _options.MaxBackoffSeconds);
            LastError = error;
            logger.LogWarning("Batch forward failed for {Count} records with {Error}", ready.Count, error);
        }
        catch (HttpRequestException ex)
        {
            foreach (var record in ready.Select(x => x.Record))
                store.ScheduleRetry(record, ex.Message, now, _options.MaxRetryAttempts, _options.MaxBackoffSeconds);
            LastError = ex.Message;
            telemetry.OutboxForwardFailureCount.Add(ready.Count);
            logger.LogWarning(ex, "HTTP error forwarding batch of {Count} records", ready.Count);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            const string error = "Request timed out";
            foreach (var record in ready.Select(x => x.Record))
                store.ScheduleRetry(record, error, now, _options.MaxRetryAttempts, _options.MaxBackoffSeconds);
            LastError = error;
            telemetry.OutboxForwardFailureCount.Add(ready.Count);
            logger.LogWarning("Batch request timed out for {Count} records", ready.Count);
        }
        catch (JsonException ex)
        {
            const string error = "Weather API response JSON was invalid";
            foreach (var record in ready.Select(x => x.Record))
                store.ScheduleRetry(record, error, now, _options.MaxRetryAttempts, _options.MaxBackoffSeconds);
            LastError = error;
            telemetry.OutboxForwardFailureCount.Add(ready.Count);
            logger.LogWarning(ex, "Invalid JSON response while forwarding {Count} record(s)", ready.Count);
        }
    }

    private async Task CompactAsync(CancellationToken ct)
    {
        await using var serviceScope = scopeFactory.CreateAsyncScope();
        var store = serviceScope.ServiceProvider.GetRequiredService<EdgeOutboxStore<OutboxDbContext>>();

        var sentDeleted = _options.SentRetentionDays > 0
            ? await store.CompactSentAsync(TimeSpan.FromDays(_options.SentRetentionDays), ct)
            : 0;
        var failedDeleted = _options.FailedRetentionDays > 0
            ? await store.CompactFailedAsync(TimeSpan.FromDays(_options.FailedRetentionDays), ct)
            : 0;

        if (sentDeleted > 0 || failedDeleted > 0)
            logger.LogInformation("Davis outbox compaction deleted {Sent} sent and {Failed} failed record(s)", sentDeleted, failedDeleted);
    }

    private void MarkBatchResult(
        EdgeOutboxStore<OutboxDbContext> store,
        IEnumerable<EdgeOutboxRecord> records,
        BatchResponseDto? response,
        DateTime sentAt)
    {
        var failures = response?.Failed ?? [];
        var sentCount = 0;
        string? lastFailureError = null;

        foreach (var record in records)
        {
            var failure = failures.FirstOrDefault(f => MatchesFailure(record, f));
            if (failure is not null)
            {
                store.MarkFailed(record, failure.Error, EdgeOutboxFailureKind.Permanent);
                lastFailureError = failure.Error;
                logger.LogWarning("Dead-lettering record {Id} ({RecordedAt}): {Error}", record.Id, record.RecordedAtUtc, failure.Error);
                continue;
            }

            store.MarkSent(record, sentAt);
            sentCount++;
        }

        LastSentAt = sentAt;
        LastError = lastFailureError;
        LastBatchCount = sentCount;
        telemetry.OutboxRecordsForwarded.Add(sentCount);
        logger.LogDebug("Forwarded batch of {Count} Davis weather records", sentCount);
    }

    private static bool MatchesFailure(EdgeOutboxRecord record, BatchFailureDto failure)
    {
        var failureSource = string.IsNullOrWhiteSpace(failure.SourceId) ? failure.StationId : failure.SourceId;
        return failure.RecordedAt == record.RecordedAtUtc
            && (string.IsNullOrWhiteSpace(failureSource) || string.Equals(failureSource, record.SourceId, StringComparison.Ordinal));
    }

    private static bool TryReadPayload(EdgeOutboxRecord record, out JsonElement payload)
    {
        if (string.IsNullOrWhiteSpace(record.PayloadJson))
        {
            payload = default;
            return false;
        }

        try
        {
            payload = JsonSerializer.Deserialize<JsonElement>(record.PayloadJson, JsonOptions);
            return payload.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            payload = default;
            return false;
        }
    }

    private string BuildWeatherBatchEndpoint() => _options.ApiEndpoint.TrimEnd('/') + "/batch";

    private sealed class BatchResponseDto
    {
        [JsonPropertyName("failed")]
        public List<BatchFailureDto> Failed { get; init; } = [];
    }

    private sealed class BatchFailureDto
    {
        [JsonPropertyName("sourceId")]
        public string? SourceId { get; init; }

        [JsonPropertyName("stationId")]
        public string? StationId { get; init; }

        [JsonPropertyName("recordedAt")]
        public DateTime RecordedAt { get; init; }

        [JsonPropertyName("recordedAtUtc")]
        public DateTime RecordedAtUtc
        {
            init => RecordedAt = value;
        }

        [JsonPropertyName("error")]
        public string Error { get; init; } = string.Empty;
    }
}
