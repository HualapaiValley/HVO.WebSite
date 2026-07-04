using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using HVO.Edge.Contracts;
using HVO.Edge.Outbox;
using HVO.Hardware.VictronSmartShunt.Configuration;
using HVO.Hardware.VictronSmartShunt.SmartShunt;
using Microsoft.Extensions.Options;

namespace HVO.Hardware.VictronSmartShunt.Outbox;

public sealed class PowerApiForwarder : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> PlaceholderEndpoints =
    [
        string.Empty,
        "https://localhost:5001/api/v1/power/readings",
    ];

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpClientFactory _httpFactory;
    private readonly OutboxOptions _options;
    private readonly ILogger<PowerApiForwarder> _logger;

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
    public DateTime? LastSentAt
    {
        get
        {
            var ticks = Volatile.Read(ref _lastSentAtTicks);
            return ticks == 0 ? null : new DateTime(ticks, DateTimeKind.Utc);
        }
    }

    private bool IsPlaceholderConfig =>
        PlaceholderEndpoints.Contains(_options.ApiEndpoint)
        || string.IsNullOrWhiteSpace(_options.ApiKey)
        || string.Equals(_options.ApiKey, "REPLACE_ME", StringComparison.OrdinalIgnoreCase);

    public PowerApiForwarder(
        IServiceScopeFactory scopeFactory,
        IHttpClientFactory httpFactory,
        IOptions<OutboxOptions> options,
        ILogger<PowerApiForwarder> logger)
    {
        _scopeFactory = scopeFactory;
        _httpFactory = httpFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("SmartShunt PowerApiForwarder starting. Endpoint: {Endpoint}", _options.ApiEndpoint);
        var lastCompactionAt = DateTime.MinValue;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepAsync(stoppingToken);
                await RequeueRetryExhaustedAsync(stoppingToken);

                if ((_options.SentRetentionDays > 0 || _options.FailedRetentionDays > 0) &&
                    (DateTime.UtcNow - lastCompactionAt).TotalHours >= 24)
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
                _logger.LogError(ex, "SmartShunt PowerApiForwarder sweep error");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_options.SweepIntervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    internal async Task<bool> SweepAsync(CancellationToken ct)
    {
        await using var serviceScope = _scopeFactory.CreateAsyncScope();
        var store = serviceScope.ServiceProvider.GetRequiredService<EdgeOutboxStore<OutboxDbContext>>();
        var now = DateTime.UtcNow;

        var pending = await store.GetReadyBatchAsync(SmartShuntOutboxPayloadTypes.Reading, now, _options.BatchSize, ct);

        await RefreshCountsAsync(store, ct);

        if (pending.Count == 0 || IsPlaceholderConfig)
            return false;

        store.MarkAttempt(pending, now);

        var ready = new List<(EdgeOutboxRecord Record, PowerReadingPayload Payload)>();
        var sentCount = 0;
        try
        {
            foreach (var record in pending)
            {
                if (TryReadPayload(record, out var payload))
                    ready.Add((record, payload));
            }

            if (ready.Count > 0)
            {
                // Wrap in CloudEvents 1.0 envelopes for standards-compliant delivery
                var readyJson = ready.Select(x =>
                {
                    var el = JsonSerializer.SerializeToElement(x.Payload, JsonOptions);
                    return (x.Record, el);
                }).ToList();
                var cloudEvents = CloudEventsForwardingHelper.WrapBatchAsCloudEvents(readyJson, "smartshunt");

                using var request = new HttpRequestMessage(HttpMethod.Post, _options.ApiEndpoint)
                {
                    Content = JsonContent.Create(cloudEvents, options: JsonOptions),
                };
                request.AddTraceContext();

                using var response = await _httpFactory.CreateClient("PowerApi")
                    .SendAsync(request, ct);

                if (response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadFromJsonAsync<PowerBatchResponse>(JsonOptions, ct);
                    sentCount = MarkBatchResult(store, ready.Select(x => x.Record), body, now);
                }
                else
                {
                    var error = $"HTTP {(int)response.StatusCode}: {await ReadBoundedBodyAsync(response, ct)}";
                    foreach (var record in ready.Select(x => x.Record))
                    {
                        if ((int)response.StatusCode is 400 or 401 or 403 or 404)
                            store.MarkFailed(record, error, EdgeOutboxFailureKind.Permanent);
                        else
                            store.ScheduleRetry(record, error, now, _options.MaxRetryAttempts, _options.MaxBackoffSeconds);
                    }
                    _lastError = error;
                }
            }
        }
        catch (HttpRequestException ex)
        {
            foreach (var record in ready.Select(x => x.Record))
                store.ScheduleRetry(record, ex.Message, now, _options.MaxRetryAttempts, _options.MaxBackoffSeconds);
            _lastError = ex.Message;
            _logger.LogWarning(ex, "SmartShunt forward HTTP error for {Count} record(s)", ready.Count);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            const string error = "Request timed out";
            foreach (var record in ready.Select(x => x.Record))
                store.ScheduleRetry(record, error, now, _options.MaxRetryAttempts, _options.MaxBackoffSeconds);
            _lastError = error;
            _logger.LogWarning("SmartShunt forward timed out for {Count} record(s)", ready.Count);
        }
        catch (JsonException ex)
        {
            const string error = "Power API response JSON was invalid";
            foreach (var record in ready.Select(x => x.Record))
                store.ScheduleRetry(record, error, now, _options.MaxRetryAttempts, _options.MaxBackoffSeconds);
            _lastError = error;
            _logger.LogWarning(ex, "SmartShunt forward JSON error for {Count} record(s)", ready.Count);
        }

        await store.SaveChangesAsync(ct);
        await RefreshCountsAsync(store, ct);
        return sentCount > 0;
    }

    private async Task CompactAsync(CancellationToken ct)
    {
        await using var serviceScope = _scopeFactory.CreateAsyncScope();
        var store = serviceScope.ServiceProvider.GetRequiredService<EdgeOutboxStore<OutboxDbContext>>();

        if (_options.SentRetentionDays > 0)
            await store.CompactSentAsync(TimeSpan.FromDays(_options.SentRetentionDays), ct);

        if (_options.FailedRetentionDays > 0)
            await store.CompactFailedAsync(TimeSpan.FromDays(_options.FailedRetentionDays), ct);
    }

    internal async Task RequeueRetryExhaustedAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var store = scope.ServiceProvider.GetRequiredService<EdgeOutboxStore<OutboxDbContext>>();
            var requeued = await store.RequeueRetryExhaustedAsync(ct);
            if (requeued > 0)
                _logger.LogInformation("Requeued {Count} SmartShunt RetryExhausted records", requeued);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Requeue of RetryExhausted failed (non-fatal)");
        }
    }

    private async Task RefreshCountsAsync(EdgeOutboxStore<OutboxDbContext> store, CancellationToken ct)
    {
        _pendingCount = await store.CountPendingAsync(SmartShuntOutboxPayloadTypes.Reading, ct);
        _failedCount = await store.CountFailedAsync(SmartShuntOutboxPayloadTypes.Reading, ct);
        _permanentFailedCount = await store.CountFailedAsync(EdgeOutboxFailureKind.Permanent, ct);
        _retryExhaustedCount = await store.CountFailedAsync(EdgeOutboxFailureKind.RetryExhausted, ct);
    }

    private bool TryReadPayload(EdgeOutboxRecord record, out PowerReadingPayload payload)
    {
        try
        {
            payload = JsonSerializer.Deserialize<PowerReadingPayload>(record.PayloadJson, JsonOptions)
                ?? throw new JsonException("Payload deserialized to null.");
            return true;
        }
        catch (JsonException ex)
        {
            const string error = "Outbox payload JSON is invalid.";
            record.Status = EdgeOutboxStatus.Failed;
            record.FailureKind = EdgeOutboxFailureKind.Permanent;
            record.LastError = error;
            _lastError = error;
            _logger.LogError(ex, "SmartShunt outbox record {Id} has invalid JSON and was not forwarded", record.Id);
            payload = null!;
            return false;
        }
    }

    private int MarkBatchResult(
        EdgeOutboxStore<OutboxDbContext> store,
        IEnumerable<EdgeOutboxRecord> records,
        PowerBatchResponse? response,
        DateTime sentAt)
    {
        var failures = response?.Failed ?? [];
        var sentCount = 0;

        foreach (var record in records)
        {
            var failure = failures.FirstOrDefault(f => string.Equals(f.SourceId, record.SourceId, StringComparison.Ordinal) && f.RecordedAtUtc == record.RecordedAtUtc);
            if (failure is not null)
            {
                store.MarkFailed(record, failure.Error, EdgeOutboxFailureKind.Permanent);
                continue;
            }

            store.MarkSent(record, sentAt);
            sentCount++;
        }

        _lastBatchCount = sentCount;
        _lastError = null;
        Volatile.Write(ref _lastSentAtTicks, sentAt.Ticks);
        return sentCount;
    }

    private static async Task<string> ReadBoundedBodyAsync(HttpResponseMessage response, CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream, leaveOpen: true);
        var buffer = new char[512];
        var read = await reader.ReadAsync(buffer, ct);
        return new string(buffer, 0, read);
    }

    private sealed class PowerBatchResponse
    {
        [JsonPropertyName("failed")]
        public List<PowerBatchFailure> Failed { get; init; } = [];
    }

    private sealed class PowerBatchFailure
    {
        [JsonPropertyName("sourceId")]
        public string SourceId { get; init; } = string.Empty;

        [JsonPropertyName("recordedAtUtc")]
        public DateTime RecordedAtUtc { get; init; }

        [JsonPropertyName("error")]
        public string Error { get; init; } = string.Empty;
    }
}
