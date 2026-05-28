using HVO.Enterprise.Telemetry.Abstractions;
using HVO.Hardware.DavisVantagePro2.Configuration;
using HVO.Hardware.DavisVantagePro2.Outbox;
using HVO.Hardware.DavisVantagePro2.Telemetry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;

namespace HVO.Hardware.DavisVantagePro2.Workers;

/// <summary>
/// Background service that sweeps the outbox for pending records and POSTs them
/// to the weather API in batches. Uses exponential back-off on failure.
/// </summary>
public sealed class OutboxForwarder(
    IServiceScopeFactory scopeFactory,
    IHttpClientFactory httpFactory,
    IOptions<OutboxOptions> options,
    DavisTelemetry telemetry,
    ITelemetryService telemetryService,
    ILogger<OutboxForwarder> logger) : BackgroundService
{
    private readonly OutboxOptions _options = options.Value;

    // Expose stats for the status page
    public int PendingCount { get; private set; }
    public int FailedCount { get; private set; }
    public DateTime? LastSentAt { get; private set; }
    public string? LastError { get; private set; }
    public int LastBatchCount { get; private set; }

    /// <summary>
    /// Raised after each outbox sweep completes (whether or not records were sent).
    /// Lets the status page refresh outbox counts independently of the weather reading cadence.
    /// </summary>
    public event Action? SweptCompleted;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("OutboxForwarder starting. Endpoint: {Endpoint}", _options.ApiEndpoint);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "OutboxForwarder sweep error");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_options.SweepIntervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException) { break; }
        }

        logger.LogInformation("OutboxForwarder stopped");
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        using var sweepScope = telemetryService.StartOperation("Davis.Outbox.Sweep");
        await using var serviceScope = scopeFactory.CreateAsyncScope();
        var db = serviceScope.ServiceProvider.GetRequiredService<OutboxDbContext>();

        var now = DateTime.UtcNow;
        var pending = await db.OutboxRecords
            .Where(r => r.Status == OutboxStatus.Pending && r.NextRetryAtUtc <= now)
            .OrderBy(r => r.RecordedAtUtc)
            .Take(_options.BatchSize)
            .ToListAsync(ct);

        PendingCount = await db.OutboxRecords.CountAsync(r => r.Status == OutboxStatus.Pending, ct);
        FailedCount = await db.OutboxRecords.CountAsync(r => r.Status == OutboxStatus.Failed, ct);
        telemetry.SetOutboxQueueDepth(PendingCount);

        if (pending.Count == 0)
        {
            sweepScope.WithTag("pending", 0).Succeed();
            SweptCompleted?.Invoke();
            return;
        }

        var client = httpFactory.CreateClient("WeatherApi");
        try
        {
            await ForwardBatchAsync(db, client, pending, ct);
            await db.SaveChangesAsync(ct);
            sweepScope
                .WithTag("records_forwarded", LastBatchCount)
                .WithTag("pending", PendingCount)
                .WithTag("failed", FailedCount)
                .Succeed();
        }
        catch (OperationCanceledException ex) { sweepScope.Fail(ex); throw; }
        catch (Exception ex) { sweepScope.RecordException(ex); sweepScope.Fail(ex); throw; }

        SweptCompleted?.Invoke();
    }

    private async Task ForwardBatchAsync(OutboxDbContext db, HttpClient client,
        List<OutboxRecord> records, CancellationToken ct)
    {
        var batchEndpoint = _options.ApiEndpoint + "/batch";

        // Build JSON array from the already-serialised per-record payloads
        var batchJson = "[" + string.Join(",", records.Select(r => r.Payload)) + "]";

        foreach (var record in records)
        {
            record.AttemptCount++;
            record.LastAttemptedAtUtc = DateTime.UtcNow;
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            using var content = new StringContent(batchJson, Encoding.UTF8, "application/json");
            using var response = await client.PostAsync(batchEndpoint, content, ct);
            telemetry.OutboxForwardLatencyMs.Record(sw.Elapsed.TotalMilliseconds);

            if (response.IsSuccessStatusCode)
            {
                // Parse per-record result to identify permanent failures
                var result = await response.Content.ReadFromJsonAsync<BatchResponseDto>(ct);

                if (result is null)
                {
                    // Null deserialization result is unexpected — schedule a retry for all records
                    string error = "Null response body from batch endpoint";
                    foreach (var record in records)
                    {
                        record.LastError = error;
                        ScheduleRetry(record);
                    }
                    LastError = error;
                    logger.LogWarning("Batch forward returned success status but null body ({Count} records scheduled for retry)", records.Count);
                    return;
                }

                var failedTimes = result.Failed?.ToHashSet() ?? [];

                var sentAt = DateTime.UtcNow;
                int sentCount = 0;

                foreach (var record in records)
                {
                    var failure = failedTimes.FirstOrDefault(f => f.RecordedAt == record.RecordedAtUtc);
                    if (failure is not null)
                    {
                        // Permanent validation failure — dead-letter it
                        record.Status = OutboxStatus.Failed;
                        record.LastError = failure.Error;
                        logger.LogWarning(
                            "Dead-lettering record {Id} ({RecordedAt}): {Error}",
                            record.Id, record.RecordedAtUtc, failure.Error);
                    }
                    else
                    {
                        // Inserted or skipped-as-duplicate — both mean the data is safely in the DB
                        record.Status = OutboxStatus.Sent;
                        record.SentAtUtc = sentAt;
                        record.LastError = null;
                        sentCount++;
                    }
                }

                LastSentAt = sentAt;
                LastError = null;
                LastBatchCount = sentCount;
                telemetry.OutboxRecordsForwarded.Add(sentCount);

                if (result?.Failed?.Count > 0)
                    logger.LogWarning("Batch of {Total}: {Sent} sent/skipped, {Dead} dead-lettered",
                        records.Count, sentCount, result.Failed.Count);
                else
                    logger.LogDebug("Forwarded batch of {Count} records", sentCount);
            }
            else
            {
                // Transient HTTP failure — retry all with backoff
                string body = await response.Content.ReadAsStringAsync(ct);
                string error = $"HTTP {(int)response.StatusCode}: {body[..Math.Min(200, body.Length)]}";
                foreach (var record in records)
                {
                    record.LastError = error;
                    ScheduleRetry(record);
                }
                LastError = error;
                logger.LogWarning("Batch forward failed ({Count} records): {Err}", records.Count, error);
            }
        }
        catch (HttpRequestException ex)
        {
            foreach (var record in records)
            {
                record.LastError = ex.Message;
                ScheduleRetry(record);
            }
            LastError = ex.Message;
            logger.LogWarning(ex, "HTTP error forwarding batch of {Count} records", records.Count);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            foreach (var record in records)
            {
                record.LastError = "Request timed out";
                ScheduleRetry(record);
            }
            LastError = "Request timed out";
            logger.LogWarning("Batch request timed out for {Count} records", records.Count);
        }
    }

    // Minimal DTOs for deserialising the batch response — mirrors WeatherV9Models shapes
    private sealed class BatchResponseDto
    {
        [JsonPropertyName("failed")]
        public List<BatchFailureDto>? Failed { get; init; }
    }

    private sealed class BatchFailureDto
    {
        [JsonPropertyName("recordedAt")]
        public DateTime RecordedAt { get; init; }

        [JsonPropertyName("error")]
        public string Error { get; init; } = string.Empty;
    }

    private void ScheduleRetry(OutboxRecord record)
    {
        if (record.AttemptCount >= _options.MaxRetryAttempts)
        {
            record.Status = OutboxStatus.Failed;
            record.LastError = $"Giving up after {record.AttemptCount} attempts. Last error: {record.LastError}";
            logger.LogError("Record {Id} permanently failed after {N} attempts", record.Id, record.AttemptCount);
            return;
        }

        // Exponential backoff: 2^n seconds, capped
        double delaySec = Math.Min(Math.Pow(2, record.AttemptCount), _options.MaxBackoffSeconds);
        record.NextRetryAtUtc = DateTime.UtcNow.AddSeconds(delaySec);
    }
}
