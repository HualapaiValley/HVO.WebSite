using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using HVO.Edge.Contracts;
using HVO.Edge.Contracts.PowerSystem;
using HVO.Edge.Outbox;
using HVO.Gateway.SolarAssistant.Configuration;
using HVO.Gateway.SolarAssistant.SolarAssistant;
using HVO.Gateway.SolarAssistant.Telemetry;
using Microsoft.Extensions.Options;

namespace HVO.Gateway.SolarAssistant.Outbox;

/// <summary>Sweeps the local SQLite outbox and forwards power readings to the HVO website API.</summary>
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
    private readonly RuntimeOutboxSettings _runtimeSettings;
    private readonly ILogger<PowerApiForwarder> _logger;
    private readonly SolarAssistantTelemetry _telemetry;

    private volatile int _pendingCount;
    private volatile int _failedCount;
    private long _lastSentAtTicks;
    private volatile string? _lastError;
    private volatile int _lastBatchCount;

    public int PendingCount => _pendingCount;
    public int FailedCount => _failedCount;
    public string? LastError => _lastError;
    public int LastBatchCount => _lastBatchCount;
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
        RuntimeOutboxSettings runtimeSettings,
        ILogger<PowerApiForwarder> logger,
        SolarAssistantTelemetry telemetry)
    {
        _scopeFactory = scopeFactory;
        _httpFactory = httpFactory;
        _options = options.Value;
        _runtimeSettings = runtimeSettings;
        _logger = logger;
        _telemetry = telemetry;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("PowerApiForwarder starting. Remote forwarding is configured.");
        var lastCompactionAt = DateTime.MinValue;

        while (!stoppingToken.IsCancellationRequested)
        {
            bool anyWork = false;
            try
            {
                anyWork = await SweepAsync(stoppingToken);
                if (_options.SentRetentionDays > 0 &&
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
                _logger.LogError(ex, "PowerApiForwarder sweep error");
            }

            if (!anyWork)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(_runtimeSettings.EffectiveSweepIntervalSeconds(_options.SweepIntervalSeconds)), stoppingToken);
                }
                catch (OperationCanceledException) { break; }
            }
        }

        _logger.LogInformation("PowerApiForwarder stopped.");
    }

    internal async Task<bool> SweepAsync(CancellationToken ct)
    {
        await using var serviceScope = _scopeFactory.CreateAsyncScope();
        var store = serviceScope.ServiceProvider.GetRequiredService<EdgeOutboxStore<OutboxDbContext>>();
        var now = DateTime.UtcNow;

        var pending = await store.GetReadyBatchAsync(PowerOutboxPayloadTypes.PowerReading, now, _runtimeSettings.EffectiveBatchSize(_options.BatchSize), ct);

        _pendingCount = await store.CountPendingAsync(PowerOutboxPayloadTypes.PowerReading, ct);
        _failedCount = await store.CountFailedAsync(PowerOutboxPayloadTypes.PowerReading, ct);

        if (pending.Count == 0 || IsPlaceholderConfig)
        {
            if (!IsPlaceholderConfig)
            {
                await ForwardSnapshotPayloadsAsync<PowerDeviceInventoryPayload>(
                    store,
                    PowerOutboxPayloadTypes.DeviceInventory,
                    BuildPowerEndpoint("device-inventory"),
                    now,
                    ct);
                await ForwardSnapshotPayloadsAsync<PowerConfigurationPayload>(
                    store,
                    PowerOutboxPayloadTypes.Configuration,
                    BuildPowerEndpoint("configuration"),
                    now,
                    ct);
                await ForwardSnapshotPayloadsAsync<PowerEnergyPayload>(
                    store,
                    PowerOutboxPayloadTypes.Energy,
                    BuildPowerEndpoint("energy"),
                    now,
                    ct);
                await ForwardSnapshotPayloadsAsync<PowerInverterDetailPayload>(
                    store,
                    PowerOutboxPayloadTypes.InverterDetail,
                    BuildPowerEndpoint("inverter-detail"),
                    now,
                    ct);
                await ForwardSnapshotPayloadsAsync<GatewayStatusPayload>(
                    store,
                    PowerOutboxPayloadTypes.GatewayStatus,
                    BuildPowerEndpoint("gateway-status"),
                    now,
                    ct);
            }
            return false;
        }

        store.MarkAttempt(pending, now);

        var ready = new List<(EdgeOutboxRecord Record, PowerReadingPayload Payload)>();
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
                var cloudEvents = CloudEventsForwardingHelper.WrapBatchAsCloudEvents(readyJson, "solarassistant");

                using var request = new HttpRequestMessage(HttpMethod.Post, _options.ApiEndpoint)
                {
                    Content = JsonContent.Create(cloudEvents, options: JsonOptions),
                };
                request.AddTraceContext();

                var fwSw = Stopwatch.StartNew();
                using var response = await _httpFactory
                    .CreateClient("PowerApi")
                    .SendAsync(request, ct);

                if (response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadFromJsonAsync<PowerBatchResponse>(JsonOptions, ct);
                    MarkBatchResult(ready.Select(x => x.Record), body, now);
                    _telemetry.OutboxForwardLatencyMs.Record(fwSw.Elapsed.TotalMilliseconds);
                    _telemetry.OutboxRecordsForwarded.Add(_lastBatchCount);
                }
                else
                {
                    var error = $"HTTP {(int)response.StatusCode}";
                    foreach (var record in ready.Select(x => x.Record))
                        store.ScheduleRetry(record, error, now, _options.MaxRetryAttempts, _options.MaxBackoffSeconds);
                    _lastError = error;
                    _logger.LogWarning("Power API forward failed for {Count} record(s): {Error}", ready.Count, error);
                }
            }
        }
        catch (HttpRequestException ex)
        {
            foreach (var record in ready.Select(x => x.Record))
                store.ScheduleRetry(record, ex.Message, now, _options.MaxRetryAttempts, _options.MaxBackoffSeconds);
            _lastError = ex.Message;
            _logger.LogWarning(ex, "HTTP error forwarding {Count} power record(s)", ready.Count);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            const string error = "Request timed out";
            foreach (var record in ready.Select(x => x.Record))
                store.ScheduleRetry(record, error, now, _options.MaxRetryAttempts, _options.MaxBackoffSeconds);
            _lastError = error;
            _logger.LogWarning("Power API request timed out for {Count} record(s)", ready.Count);
        }
        catch (JsonException ex)
        {
            const string error = "Power API response JSON was invalid";
            foreach (var record in ready.Select(x => x.Record))
                store.ScheduleRetry(record, error, now, _options.MaxRetryAttempts, _options.MaxBackoffSeconds);
            _lastError = error;
            _logger.LogWarning(ex, "Invalid JSON response while forwarding {Count} power record(s)", ready.Count);
        }

        await store.SaveChangesAsync(ct);
        _pendingCount = await store.CountPendingAsync(PowerOutboxPayloadTypes.PowerReading, ct);
        _failedCount = await store.CountFailedAsync(PowerOutboxPayloadTypes.PowerReading, ct);
        _telemetry.SetOutboxQueueDepth(_pendingCount);

        await ForwardSnapshotPayloadsAsync<PowerDeviceInventoryPayload>(
            store,
            PowerOutboxPayloadTypes.DeviceInventory,
            BuildPowerEndpoint("device-inventory"),
            now,
            ct);
        await ForwardSnapshotPayloadsAsync<PowerConfigurationPayload>(
            store,
            PowerOutboxPayloadTypes.Configuration,
            BuildPowerEndpoint("configuration"),
            now,
            ct);
        await ForwardSnapshotPayloadsAsync<PowerEnergyPayload>(
            store,
            PowerOutboxPayloadTypes.Energy,
            BuildPowerEndpoint("energy"),
            now,
            ct);
        await ForwardSnapshotPayloadsAsync<PowerInverterDetailPayload>(
            store,
            PowerOutboxPayloadTypes.InverterDetail,
            BuildPowerEndpoint("inverter-detail"),
            now,
            ct);
        await ForwardSnapshotPayloadsAsync<GatewayStatusPayload>(
            store,
            PowerOutboxPayloadTypes.GatewayStatus,
            BuildPowerEndpoint("gateway-status"),
            now,
            ct);
        return true;
    }

    private async Task ForwardSnapshotPayloadsAsync<TPayload>(
        EdgeOutboxStore<OutboxDbContext> store,
        string payloadType,
        string endpoint,
        DateTime now,
        CancellationToken ct)
    {
        if (IsPlaceholderConfig)
            return;

        var pending = await store.GetReadyBatchAsync(payloadType, now, _runtimeSettings.EffectiveBatchSize(_options.BatchSize), ct);
        if (pending.Count == 0)
            return;

        store.MarkAttempt(pending, now);
        foreach (var record in pending)
        {
            if (!TryReadPayload<TPayload>(record, out var payload))
                continue;

            try
            {
                // Wrap snapshot in CloudEvents envelope for standards-compliant delivery
                var dataEl = JsonSerializer.SerializeToElement(payload, JsonOptions);
                var cloudEvent = new Dictionary<string, object?>
                {
                    ["specversion"] = CloudEventsConstants.SpecVersion,
                    ["type"] = EdgePayloadTypes.ToCloudEventType(payloadType),
                    ["source"] = $"/gateways/solarassistant/{record.SourceId}",
                    ["id"] = Guid.NewGuid().ToString("D"),
                    ["time"] = record.RecordedAtUtc.ToString("O"),
                    ["datacontenttype"] = CloudEventsConstants.JsonContentType,
                    ["data"] = dataEl,
                };
                var cloudEventEl = JsonSerializer.SerializeToElement(cloudEvent, JsonOptions);

                using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
                {
                    Content = JsonContent.Create(cloudEventEl, options: JsonOptions),
                };
                request.AddTraceContext();

                using var response = await _httpFactory
                    .CreateClient("PowerApi")
                    .SendAsync(request, ct);

                if (response.IsSuccessStatusCode)
                {
                    record.Status = EdgeOutboxStatus.Sent;
                    record.SentAtUtc = now;
                    record.LastError = null;
                    _logger.LogDebug("Forwarded {PayloadType} outbox record {Id}", payloadType, record.Id);
                    continue;
                }

                store.ScheduleRetry(record, $"HTTP {(int)response.StatusCode}", now, _options.MaxRetryAttempts, _options.MaxBackoffSeconds);
            }
            catch (HttpRequestException ex)
            {
                store.ScheduleRetry(record, ex.Message, now, _options.MaxRetryAttempts, _options.MaxBackoffSeconds);
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested)
            {
                store.ScheduleRetry(record, "Request timed out", now, _options.MaxRetryAttempts, _options.MaxBackoffSeconds);
            }
        }

        await store.SaveChangesAsync(ct);
    }

    private async Task CompactAsync(CancellationToken ct)
    {
        await using var serviceScope = _scopeFactory.CreateAsyncScope();
        var store = serviceScope.ServiceProvider.GetRequiredService<EdgeOutboxStore<OutboxDbContext>>();
        var deleted = await store.CompactSentAsync(TimeSpan.FromDays(_options.SentRetentionDays), ct);

        if (deleted > 0)
            _logger.LogInformation("Power outbox compaction deleted {Count} sent record(s)", deleted);
    }

    private bool TryReadPayload(EdgeOutboxRecord record, out PowerReadingPayload payload)
    {
        return TryReadPayload<PowerReadingPayload>(record, out payload);
    }

    private bool TryReadPayload<TPayload>(EdgeOutboxRecord record, out TPayload payload)
    {
        try
        {
            payload = JsonSerializer.Deserialize<TPayload>(record.PayloadJson, JsonOptions)
                ?? throw new JsonException("Payload deserialized to null.");
            return true;
        }
        catch (JsonException ex)
        {
            const string error = "Outbox payload JSON is invalid.";
            record.Status = EdgeOutboxStatus.Failed;
            record.LastError = error;
            _lastError = error;
            _logger.LogError(ex, "Power outbox record {Id} has invalid JSON and was not forwarded", record.Id);
            payload = default!;
            return false;
        }
    }

    private string BuildPowerEndpoint(string leaf)
    {
        var endpoint = _options.ApiEndpoint.TrimEnd('/');
        if (endpoint.EndsWith("/readings", StringComparison.OrdinalIgnoreCase))
            endpoint = endpoint[..^"/readings".Length];
        return $"{endpoint}/{leaf}";
    }

    private void MarkBatchResult(IEnumerable<EdgeOutboxRecord> records, PowerBatchResponse? response, DateTime sentAt)
    {
        var failures = response?.Failed ?? [];
        var sentCount = 0;
        string? lastFailureError = null;

        foreach (var record in records)
        {
            var failure = failures.FirstOrDefault(f =>
                string.Equals(f.SourceId, record.SourceId, StringComparison.Ordinal) &&
                f.RecordedAtUtc == record.RecordedAtUtc);
            if (failure is not null)
            {
                record.Status = EdgeOutboxStatus.Failed;
                record.LastError = failure.Error;
                lastFailureError = failure.Error;
                _logger.LogWarning(
                    "Power outbox record {Id} dead-lettered by website validation: {Error}",
                    record.Id,
                    failure.Error);
                continue;
            }

            record.Status = EdgeOutboxStatus.Sent;
            record.SentAtUtc = sentAt;
            record.LastError = null;
            sentCount++;
        }

        _lastBatchCount = sentCount;
        _lastError = lastFailureError;
        Volatile.Write(ref _lastSentAtTicks, sentAt.Ticks);
        _logger.LogInformation("Forwarded {Count} power record(s)", sentCount);
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
