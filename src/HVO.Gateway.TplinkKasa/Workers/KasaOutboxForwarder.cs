using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using HVO.Edge.Contracts;
using HVO.Edge.Contracts.PowerSystem;
using HVO.Edge.Outbox;
using HVO.Gateway.TplinkKasa.Configuration;
using HVO.Gateway.TplinkKasa.Models;
using HVO.Gateway.TplinkKasa.Outbox;
using HVO.Gateway.TplinkKasa.Telemetry;
using Microsoft.Extensions.Options;

namespace HVO.Gateway.TplinkKasa.Workers;

public sealed class KasaOutboxForwarder : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly KasaGatewayOptions.OutboxSection _options;
    private readonly RuntimeOutboxSettings _runtimeSettings;
    private readonly ILogger<KasaOutboxForwarder> _logger;
    private readonly KasaGatewayTelemetry _telemetry;
    private volatile int _pendingCount;
    private volatile int _failedCount;
    private long _lastSentAtTicks;
    private volatile string? _lastError;
    private volatile int _lastBatchCount;

    public KasaOutboxForwarder(
        IServiceScopeFactory scopeFactory,
        IHttpClientFactory httpClientFactory,
        IOptions<KasaGatewayOptions.OutboxSection> options,
        RuntimeOutboxSettings runtimeSettings,
        ILogger<KasaOutboxForwarder> logger,
        KasaGatewayTelemetry telemetry)
    {
        _scopeFactory = scopeFactory;
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _runtimeSettings = runtimeSettings;
        _logger = logger;
        _telemetry = telemetry;
    }

    public event Action? SweepCompleted;

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
        string.IsNullOrWhiteSpace(_options.ApiEndpoint)
        || string.IsNullOrWhiteSpace(_options.ApiKey)
        || string.Equals(_options.ApiKey, "REPLACE_ME", StringComparison.OrdinalIgnoreCase);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Kasa outbox forwarder starting. Endpoint: {Endpoint}", _options.ApiEndpoint);
        var lastCompactionAtUtc = DateTime.MinValue;

        while (!stoppingToken.IsCancellationRequested)
        {
            bool anySent = false;
            try
            {
                anySent = await SweepAsync(stoppingToken).ConfigureAwait(false);
                if (anySent)
                    await RequeueRetryExhaustedAsync(stoppingToken).ConfigureAwait(false);

                if ((DateTime.UtcNow - lastCompactionAtUtc).TotalHours >= 24)
                {
                    await CompactAsync(stoppingToken).ConfigureAwait(false);
                    lastCompactionAtUtc = DateTime.UtcNow;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                _logger.LogError(ex, "Kasa outbox sweep error");
            }
            finally
            {
                SweepCompleted?.Invoke();
            }

            if (!anySent)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(_runtimeSettings.EffectiveSweepIntervalSeconds(_options.SweepIntervalSeconds)), stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }

        _logger.LogInformation("Kasa outbox forwarder stopped.");
    }

    internal async Task<bool> SweepAsync(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<EdgeOutboxStore<OutboxDbContext>>();
        var now = DateTime.UtcNow;
        var anySent = false;

        var energy = await store.GetReadyBatchAsync(KasaOutboxPayloadTypes.Energy, now, _runtimeSettings.EffectiveBatchSize(_options.BatchSize), ct).ConfigureAwait(false);
        await RefreshCountsAsync(store, ct).ConfigureAwait(false);
        _telemetry.SetOutboxQueueDepth(_pendingCount);

        if (IsPlaceholderConfig)
            return false;

        if (energy.Count > 0)
            anySent = await ForwardBatchAsync<KasaEnergyPayload>(store, energy, _options.ApiEndpoint, now, ct).ConfigureAwait(false);

        anySent |= await ForwardInventoryAsync(store, now, ct).ConfigureAwait(false);

        await RefreshCountsAsync(store, ct).ConfigureAwait(false);
        return anySent;
    }

    private async Task<bool> ForwardInventoryAsync(EdgeOutboxStore<OutboxDbContext> store, DateTime now, CancellationToken ct)
    {
        var inventory = await store.GetReadyBatchAsync(KasaOutboxPayloadTypes.Inventory, now, _runtimeSettings.EffectiveBatchSize(_options.BatchSize), ct).ConfigureAwait(false);
        if (inventory.Count == 0)
            return false;

        return await ForwardInventorySnapshotsAsync(store, inventory, BuildPowerEndpoint("device-inventory"), now, ct).ConfigureAwait(false);
    }

    private async Task<bool> ForwardInventorySnapshotsAsync(
        EdgeOutboxStore<OutboxDbContext> store,
        IReadOnlyList<EdgeOutboxRecord> records,
        string endpoint,
        DateTime now,
        CancellationToken ct)
    {
        store.MarkAttempt(records, now);
        var ready = new List<(EdgeOutboxRecord Record, PowerDeviceInventoryPayload Payload)>();
        var anySent = false;

        foreach (var record in records)
        {
            if (TryReadPayload(record, out KasaInventoryPayload payload))
                ready.Add((record, MapInventoryPayload(payload)));
        }

        foreach (var item in ready)
        {
            try
            {
                // Wrap snapshot in CloudEvents envelope for standards-compliant delivery
                var dataEl = JsonSerializer.SerializeToElement(item.Payload, JsonOptions);
                var cloudEvent = new Dictionary<string, object?>
                {
                    ["specversion"] = CloudEventsConstants.SpecVersion,
                    ["type"] = EdgePayloadTypes.ToCloudEventType(KasaOutboxPayloadTypes.Inventory),
                    ["source"] = $"/gateways/tplinkkasa/{item.Record.SourceId}",
                    ["id"] = Guid.NewGuid().ToString("D"),
                    ["time"] = item.Record.RecordedAtUtc.ToString("O"),
                    ["datacontenttype"] = CloudEventsConstants.JsonContentType,
                    ["data"] = dataEl,
                };
                var cloudEventEl = JsonSerializer.SerializeToElement(cloudEvent, JsonOptions);

                using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
                {
                    Content = JsonContent.Create(cloudEventEl, options: JsonOptions),
                };
                request.AddTraceContext();

                using var response = await _httpClientFactory
                    .CreateClient("KasaPowerApi")
                    .SendAsync(request, ct)
                    .ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    store.MarkSent(item.Record, now);
                    anySent = true;
                    continue;
                }

                var body = await ReadBoundedBodyAsync(response, ct).ConfigureAwait(false);
                ScheduleRetry(store, [item.Record], $"HTTP {(int)response.StatusCode}: {body}", now);
            }
            catch (HttpRequestException ex)
            {
                ScheduleRetry(store, [item.Record], ex.Message, now);
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested)
            {
                ScheduleRetry(store, [item.Record], "Request timed out", now);
            }
            catch (JsonException ex)
            {
                const string error = "Kasa inventory API response JSON was invalid.";
                ScheduleRetry(store, [item.Record], error, now);
                _logger.LogWarning(ex, "Invalid JSON response while forwarding Kasa inventory outbox record {Id}", item.Record.Id);
            }
        }

        if (ready.Count == 0 || anySent)
        {
            _lastBatchCount = anySent ? ready.Count(item => item.Record.Status == EdgeOutboxStatus.Sent) : _lastBatchCount;
            if (anySent)
            {
                _lastError = null;
                Volatile.Write(ref _lastSentAtTicks, now.Ticks);
            }
        }

        await store.SaveChangesAsync(ct).ConfigureAwait(false);
        return anySent;
    }

    private async Task<bool> ForwardBatchAsync<TPayload>(
        EdgeOutboxStore<OutboxDbContext> store,
        IReadOnlyList<EdgeOutboxRecord> records,
        string endpoint,
        DateTime now,
        CancellationToken ct)
    {
        store.MarkAttempt(records, now);
        var ready = new List<(EdgeOutboxRecord Record, TPayload Payload)>();
        var anySent = false;

        foreach (var record in records)
        {
            if (TryReadPayload(record, out TPayload payload))
                ready.Add((record, payload));
        }

        if (ready.Count == 0)
        {
            await store.SaveChangesAsync(ct).ConfigureAwait(false);
            return false;
        }

        try
        {
            // Wrap in CloudEvents 1.0 envelopes for standards-compliant delivery
            var readyJson = ready.Select(x =>
            {
                var el = JsonSerializer.SerializeToElement(x.Payload, JsonOptions);
                return (x.Record, el);
            }).ToList();
            var cloudEvents = CloudEventsForwardingHelper.WrapBatchAsCloudEvents(readyJson, "tplinkkasa");

            var fwSw = Stopwatch.StartNew();
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = JsonContent.Create(cloudEvents, options: JsonOptions),
            };
            request.AddTraceContext();

            using var response = await _httpClientFactory
                .CreateClient("KasaPowerApi")
                .SendAsync(request, ct)
                .ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadFromJsonAsync<KasaBatchResponse>(JsonOptions, ct).ConfigureAwait(false);
                if (body is null)
                {
                    ScheduleRetry(store, ready.Select(item => item.Record), "Kasa API response body was null.", now);
                }
                else
                {
                    anySent = MarkBatchResult(store, ready.Select(item => item.Record), body, now);
                    _telemetry.OutboxForwardLatencyMs.Record(fwSw.Elapsed.TotalMilliseconds);
                }
            }
            else
            {
                var body = await ReadBoundedBodyAsync(response, ct).ConfigureAwait(false);
                var error = $"HTTP {(int)response.StatusCode}: {body}";
                if ((int)response.StatusCode is 400 or 401 or 403 or 404)
                {
                    foreach (var record in ready.Select(item => item.Record))
                        store.MarkFailed(record, error, EdgeOutboxFailureKind.Permanent);
                    _lastError = error;
                }
                else
                {
                    ScheduleRetry(store, ready.Select(item => item.Record), error, now);
                }
            }
        }
        catch (HttpRequestException ex)
        {
            ScheduleRetry(store, ready.Select(item => item.Record), ex.Message, now);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            ScheduleRetry(store, ready.Select(item => item.Record), "Request timed out", now);
        }
        catch (JsonException ex)
        {
            const string error = "Kasa API response JSON was invalid.";
            ScheduleRetry(store, ready.Select(item => item.Record), error, now);
            _logger.LogWarning(ex, "Invalid JSON response while forwarding {Count} Kasa outbox record(s)", ready.Count);
        }

        await store.SaveChangesAsync(ct).ConfigureAwait(false);
        return anySent;
    }

    private async Task CompactAsync(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<EdgeOutboxStore<OutboxDbContext>>();
        var sentDeleted = await store.CompactSentAsync(TimeSpan.FromDays(_options.SentRetentionDays), ct).ConfigureAwait(false);
        var failedDeleted = await store.CompactFailedAsync(TimeSpan.FromDays(_options.FailedRetentionDays), ct).ConfigureAwait(false);
        if (sentDeleted > 0 || failedDeleted > 0)
            _logger.LogInformation("Kasa outbox compaction deleted {SentCount} sent and {FailedCount} failed record(s)", sentDeleted, failedDeleted);
    }

    private async Task RequeueRetryExhaustedAsync(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<EdgeOutboxStore<OutboxDbContext>>();
        var requeued = await store.RequeueRetryExhaustedAsync(ct).ConfigureAwait(false);
        if (requeued > 0)
            _logger.LogInformation("Kasa outbox requeued {Count} retry-exhausted record(s) after successful forward", requeued);
    }

    private async Task RefreshCountsAsync(EdgeOutboxStore<OutboxDbContext> store, CancellationToken ct)
    {
        _pendingCount = await store.CountPendingAsync(ct).ConfigureAwait(false);
        _failedCount = await store.CountFailedAsync(ct).ConfigureAwait(false);
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
            record.FailureKind = EdgeOutboxFailureKind.Permanent;
            record.LastError = error;
            _lastError = error;
            _logger.LogError(ex, "Kasa outbox record {Id} has invalid JSON and was not forwarded", record.Id);
            payload = default!;
            return false;
        }
    }

    private bool MarkBatchResult(EdgeOutboxStore<OutboxDbContext> store, IEnumerable<EdgeOutboxRecord> records, KasaBatchResponse response, DateTime sentAt)
    {
        var failures = response.Failed;
        var sentCount = 0;
        string? lastFailureError = null;

        foreach (var record in records)
        {
            var failure = failures.FirstOrDefault(f =>
                string.Equals(f.SourceId, record.SourceId, StringComparison.Ordinal)
                && f.RecordedAtUtc == record.RecordedAtUtc);
            if (failure is not null)
            {
                store.MarkFailed(record, failure.Error, EdgeOutboxFailureKind.Permanent);
                lastFailureError = failure.Error;
                continue;
            }

            store.MarkSent(record, sentAt);
            sentCount++;
        }

        _lastBatchCount = sentCount;
        _lastError = lastFailureError;
        if (sentCount > 0)
            Volatile.Write(ref _lastSentAtTicks, sentAt.Ticks);
        _logger.LogInformation("Forwarded {Count} Kasa outbox record(s)", sentCount);
        _telemetry.OutboxRecordsForwarded.Add(sentCount);
        return sentCount > 0;
    }

    private void ScheduleRetry(EdgeOutboxStore<OutboxDbContext> store, IEnumerable<EdgeOutboxRecord> records, string error, DateTime now)
    {
        foreach (var record in records)
            store.ScheduleRetry(record, error, now, _options.MaxRetryAttempts, _options.MaxBackoffSeconds);
        _lastError = error;
        _logger.LogWarning("Kasa API transient failure for outbox batch: {Error}", error);
    }

    private string BuildPowerEndpoint(string leaf)
    {
        var endpoint = _options.ApiEndpoint.TrimEnd('/');
        if (endpoint.EndsWith("/readings", StringComparison.OrdinalIgnoreCase))
            endpoint = endpoint[..^"/readings".Length];
        return $"{endpoint}/{leaf}";
    }

    private static PowerDeviceInventoryPayload MapInventoryPayload(KasaInventoryPayload payload) => new()
    {
        SourceId = payload.SourceId,
        SourceSystem = payload.SourceSystem,
        DeviceId = payload.DeviceId,
        RecordedAtUtc = payload.RecordedAtUtc,
        Devices =
        [
            new PowerDeviceInventoryDevice
            {
                DeviceId = string.IsNullOrWhiteSpace(payload.DeviceId) ? payload.SourceId : payload.DeviceId,
                Name = string.IsNullOrWhiteSpace(payload.Alias) ? payload.SourceId : payload.Alias,
                Manufacturer = "TP-Link",
                Model = payload.Model,
                FirmwareVersion = payload.SoftwareVersion,
                EntityCount = payload.Capabilities?.Count ?? 0,
            }
        ],
    };

    private static async Task<string> ReadBoundedBodyAsync(HttpResponseMessage response, CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var reader = new StreamReader(stream, leaveOpen: true);
        var buffer = new char[512];
        var read = await reader.ReadAsync(buffer, ct).ConfigureAwait(false);
        return new string(buffer, 0, read);
    }

    private sealed class KasaBatchResponse
    {
        [JsonPropertyName("failed")]
        public List<KasaBatchFailure> Failed { get; init; } = [];
    }

    private sealed class KasaBatchFailure
    {
        [JsonPropertyName("sourceId")]
        public string SourceId { get; init; } = string.Empty;

        [JsonPropertyName("recordedAtUtc")]
        public DateTime RecordedAtUtc { get; init; }

        [JsonPropertyName("error")]
        public string Error { get; init; } = string.Empty;
    }
}
