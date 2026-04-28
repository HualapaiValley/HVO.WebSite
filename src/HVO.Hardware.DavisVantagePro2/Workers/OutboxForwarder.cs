using HVO.Hardware.DavisVantagePro2.Configuration;
using HVO.Hardware.DavisVantagePro2.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HVO.Hardware.DavisVantagePro2.Workers;

/// <summary>
/// Background service that sweeps the outbox for pending records and POSTs them
/// to the weather API. Uses exponential back-off on failure.
/// </summary>
public sealed class OutboxForwarder(
    IServiceScopeFactory scopeFactory,
    IHttpClientFactory httpFactory,
    IOptions<OutboxOptions> options,
    ILogger<OutboxForwarder> logger) : BackgroundService
{
    private readonly OutboxOptions _options = options.Value;

    // Expose stats for the status page
    public int PendingCount { get; private set; }
    public DateTime? LastSentAt { get; private set; }
    public string? LastError { get; private set; }

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
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OutboxDbContext>();

        var now = DateTime.UtcNow;
        var pending = await db.OutboxRecords
            .Where(r => r.Status == OutboxStatus.Pending && r.NextRetryAtUtc <= now)
            .OrderBy(r => r.RecordedAtUtc)
            .Take(50)
            .ToListAsync(ct);

        PendingCount = await db.OutboxRecords.CountAsync(r => r.Status == OutboxStatus.Pending, ct);

        if (pending.Count == 0) return;

        var client = httpFactory.CreateClient("WeatherApi");

        foreach (var record in pending)
        {
            if (ct.IsCancellationRequested) break;
            await ForwardRecordAsync(db, client, record, ct);
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task ForwardRecordAsync(OutboxDbContext db, HttpClient client,
        OutboxRecord record, CancellationToken ct)
    {
        record.AttemptCount++;
        record.LastAttemptedAtUtc = DateTime.UtcNow;

        try
        {
            using var content = new StringContent(record.Payload, System.Text.Encoding.UTF8, "application/json");
            using var response = await client.PostAsync(_options.ApiEndpoint, content, ct);

            if (response.IsSuccessStatusCode)
            {
                record.Status   = OutboxStatus.Sent;
                record.SentAtUtc = DateTime.UtcNow;
                record.LastError = null;
                LastSentAt = DateTime.UtcNow;
                logger.LogDebug("Forwarded record {Id}", record.Id);
            }
            else
            {
                string body = await response.Content.ReadAsStringAsync(ct);
                record.LastError = $"HTTP {(int)response.StatusCode}: {body[..Math.Min(200, body.Length)]}";
                ScheduleRetry(record);
                LastError = record.LastError;
                logger.LogWarning("Forward failed for record {Id}: {Err}", record.Id, record.LastError);
            }
        }
        catch (HttpRequestException ex)
        {
            record.LastError = ex.Message;
            ScheduleRetry(record);
            LastError = ex.Message;
            logger.LogWarning(ex, "HTTP error forwarding record {Id}", record.Id);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            record.LastError = "Request timed out";
            ScheduleRetry(record);
        }
    }

    private void ScheduleRetry(OutboxRecord record)
    {
        if (record.AttemptCount >= _options.MaxRetryAttempts)
        {
            record.Status   = OutboxStatus.Failed;
            record.LastError = $"Giving up after {record.AttemptCount} attempts. Last error: {record.LastError}";
            logger.LogError("Record {Id} permanently failed after {N} attempts", record.Id, record.AttemptCount);
            return;
        }

        // Exponential backoff: 2^n seconds, capped
        double delaySec = Math.Min(Math.Pow(2, record.AttemptCount), _options.MaxBackoffSeconds);
        record.NextRetryAtUtc = DateTime.UtcNow.AddSeconds(delaySec);
    }
}
