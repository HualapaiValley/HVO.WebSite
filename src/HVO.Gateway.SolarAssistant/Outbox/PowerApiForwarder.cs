using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using HVO.Gateway.SolarAssistant.Configuration;
using HVO.Gateway.SolarAssistant.SolarAssistant;
using Microsoft.EntityFrameworkCore;
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
    private readonly ILogger<PowerApiForwarder> _logger;

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
        ILogger<PowerApiForwarder> logger)
    {
        _scopeFactory = scopeFactory;
        _httpFactory = httpFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("PowerApiForwarder starting. Endpoint: {Endpoint}", _options.ApiEndpoint);
        var lastCompactionAt = DateTime.MinValue;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepAsync(stoppingToken);
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

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_options.SweepIntervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException) { break; }
        }

        _logger.LogInformation("PowerApiForwarder stopped.");
    }

    internal async Task SweepAsync(CancellationToken ct)
    {
        await using var serviceScope = _scopeFactory.CreateAsyncScope();
        var db = serviceScope.ServiceProvider.GetRequiredService<OutboxDbContext>();
        var now = DateTime.UtcNow;

        var pending = await db.OutboxRecords
            .Where(r => r.Status == OutboxStatus.Pending && r.NextRetryAtUtc <= now)
            .OrderBy(r => r.RecordedAtUtc)
            .Take(_options.BatchSize)
            .ToListAsync(ct);

        _pendingCount = await db.OutboxRecords.CountAsync(r => r.Status == OutboxStatus.Pending, ct);
        _failedCount = await db.OutboxRecords.CountAsync(r => r.Status == OutboxStatus.Failed, ct);

        if (pending.Count == 0 || IsPlaceholderConfig)
            return;

        foreach (var record in pending)
        {
            record.AttemptCount++;
            record.LastAttemptedAtUtc = now;
        }

        var ready = new List<(OutboxRecord Record, PowerReadingPayload Payload)>();
        try
        {
            foreach (var record in pending)
            {
                if (TryReadPayload(record, out var payload))
                    ready.Add((record, payload));
            }

            if (ready.Count > 0)
            {
                var payloads = ready.Select(x => x.Payload).ToArray();

                using var response = await _httpFactory
                    .CreateClient("PowerApi")
                    .PostAsJsonAsync(_options.ApiEndpoint, payloads, JsonOptions, ct);

                if (response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadFromJsonAsync<PowerBatchResponse>(JsonOptions, ct);
                    MarkBatchResult(ready.Select(x => x.Record), body, now);
                }
                else
                {
                    var body = await ReadBoundedBodyAsync(response, ct);
                    var error = $"HTTP {(int)response.StatusCode}: {body}";
                    foreach (var record in ready.Select(x => x.Record))
                        ScheduleRetry(record, error, now);
                    _lastError = error;
                    _logger.LogWarning("Power API forward failed for {Count} record(s): {Error}", ready.Count, error);
                }
            }
        }
        catch (HttpRequestException ex)
        {
            foreach (var record in ready.Select(x => x.Record))
                ScheduleRetry(record, ex.Message, now);
            _lastError = ex.Message;
            _logger.LogWarning(ex, "HTTP error forwarding {Count} power record(s)", ready.Count);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            const string error = "Request timed out";
            foreach (var record in ready.Select(x => x.Record))
                ScheduleRetry(record, error, now);
            _lastError = error;
            _logger.LogWarning("Power API request timed out for {Count} record(s)", ready.Count);
        }
        catch (JsonException ex)
        {
            foreach (var record in ready.Select(x => x.Record))
                ScheduleRetry(record, "Power API response JSON was invalid", now);
            _lastError = ex.Message;
            _logger.LogWarning(ex, "Invalid JSON response while forwarding {Count} power record(s)", ready.Count);
        }

        await db.SaveChangesAsync(ct);
        _pendingCount = await db.OutboxRecords.CountAsync(r => r.Status == OutboxStatus.Pending, ct);
        _failedCount = await db.OutboxRecords.CountAsync(r => r.Status == OutboxStatus.Failed, ct);
    }

    private async Task CompactAsync(CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.AddDays(-_options.SentRetentionDays);
        await using var serviceScope = _scopeFactory.CreateAsyncScope();
        var db = serviceScope.ServiceProvider.GetRequiredService<OutboxDbContext>();
        var deleted = await db.OutboxRecords
            .Where(r => r.Status == OutboxStatus.Sent && r.SentAtUtc.HasValue && r.SentAtUtc.Value < cutoff)
            .ExecuteDeleteAsync(ct);

        if (deleted > 0)
            _logger.LogInformation("Power outbox compaction deleted {Count} sent record(s)", deleted);
    }

    private bool TryReadPayload(OutboxRecord record, out PowerReadingPayload payload)
    {
        try
        {
            payload = JsonSerializer.Deserialize<PowerReadingPayload>(record.Payload, JsonOptions)
                ?? throw new JsonException("Payload deserialized to null.");
            return true;
        }
        catch (JsonException ex)
        {
            const string error = "Outbox payload JSON is invalid.";
            record.Status = OutboxStatus.Failed;
            record.LastError = error;
            _lastError = error;
            _logger.LogError(ex, "Power outbox record {Id} has invalid JSON and was not forwarded", record.Id);
            payload = null!;
            return false;
        }
    }

    private void MarkBatchResult(IEnumerable<OutboxRecord> records, PowerBatchResponse? response, DateTime sentAt)
    {
        var failures = response?.Failed ?? [];
        var sentCount = 0;

        foreach (var record in records)
        {
            var failure = failures.FirstOrDefault(f =>
                string.Equals(f.SourceId, record.SourceId, StringComparison.Ordinal) &&
                f.RecordedAtUtc == record.RecordedAtUtc);
            if (failure is not null)
            {
                record.Status = OutboxStatus.Failed;
                record.LastError = failure.Error;
                _logger.LogWarning(
                    "Power outbox record {Id} dead-lettered by website validation: {Error}",
                    record.Id,
                    failure.Error);
                continue;
            }

            record.Status = OutboxStatus.Sent;
            record.SentAtUtc = sentAt;
            record.LastError = null;
            sentCount++;
        }

        _lastBatchCount = sentCount;
        _lastError = null;
        Volatile.Write(ref _lastSentAtTicks, sentAt.Ticks);
        _logger.LogInformation("Forwarded {Count} power record(s)", sentCount);
    }

    private void ScheduleRetry(OutboxRecord record, string error, DateTime now)
    {
        record.LastError = error;
        if (record.AttemptCount >= _options.MaxRetryAttempts)
        {
            record.Status = OutboxStatus.Failed;
            _logger.LogError("Power outbox record {Id} permanently failed after {Attempts} attempts", record.Id, record.AttemptCount);
            return;
        }

        var backoff = Math.Min((int)Math.Pow(2, record.AttemptCount), _options.MaxBackoffSeconds);
        record.NextRetryAtUtc = now.AddSeconds(backoff);
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
