using HVO.Enterprise.Telemetry.Abstractions;
using HVO.Hardware.JkBms.Outbox;
using HVO.Hardware.JkBms.Outbox.Forwarders;
using HVO.Hardware.JkBms.Telemetry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HVO.Hardware.JkBms.Workers;

/// <summary>
/// Background service that sweeps pending outbox records and delivers them to
/// all registered <see cref="IReadingForwarder"/> implementations.
///
/// Delivery is fan-out: every forwarder receives the same batch.
/// Each record is marked <see cref="OutboxStatus.Sent"/> only when ALL forwarders succeed.
/// On partial failure the record stays <see cref="OutboxStatus.Pending"/> with exponential backoff.
/// After <see cref="OutboxOptions.MaxRetryAttempts"/> failures the record is marked
/// <see cref="OutboxStatus.Failed"/> to prevent endless retries.
/// </summary>
public sealed class ForwarderCoordinator : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IReadOnlyList<IReadingForwarder> _forwarders;
    private readonly OutboxOptions _options;
    private readonly BmsTelemetry _telemetry;
    private readonly ITelemetryService _telemetryService;
    private readonly ILogger<ForwarderCoordinator> _logger;

    // ── Public state for the status page ─────────────────────────────────────

    public int PendingCount { get; private set; }
    public int FailedCount { get; private set; }
    public DateTime? LastSentAt { get; private set; }
    public string? LastError { get; private set; }
    public int LastBatchCount { get; private set; }

    public event Action? SweepCompleted;

    public ForwarderCoordinator(
        IServiceScopeFactory scopeFactory,
        IEnumerable<IReadingForwarder> forwarders,
        IOptions<OutboxOptions> options,
        BmsTelemetry telemetry,
        ITelemetryService telemetryService,
        ILogger<ForwarderCoordinator> logger)
    {
        _scopeFactory = scopeFactory;
        _forwarders = forwarders.ToList();
        _options = options.Value;
        _telemetry = telemetry;
        _telemetryService = telemetryService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "ForwarderCoordinator starting. {Count} forwarder(s) registered.",
            _forwarders.Count);

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
                _logger.LogError(ex, "ForwarderCoordinator sweep error");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_options.SweepIntervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException) { break; }
        }

        _logger.LogInformation("ForwarderCoordinator stopped.");
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        using var sweepScope = _telemetryService.StartOperation("JkBms.Outbox.Sweep");
        await using var serviceScope = _scopeFactory.CreateAsyncScope();
        var db = serviceScope.ServiceProvider.GetRequiredService<OutboxDbContext>();

        var now = DateTime.UtcNow;
        var pending = await db.OutboxRecords
            .Where(r => r.Status == OutboxStatus.Pending && r.NextRetryAtUtc <= now)
            .OrderBy(r => r.RecordedAtUtc)
            .Take(_options.BatchSize)
            .ToListAsync(ct);

        PendingCount = await db.OutboxRecords.CountAsync(r => r.Status == OutboxStatus.Pending, ct);
        FailedCount = await db.OutboxRecords.CountAsync(r => r.Status == OutboxStatus.Failed, ct);
        _telemetry.SetOutboxQueueDepth(PendingCount);

        if (pending.Count == 0)
        {
            sweepScope.WithTag("pending", 0).Succeed();
            SweepCompleted?.Invoke();
            return;
        }

        _logger.LogDebug("Forwarding {Count} pending record(s). Pending total: {Total}, Failed total: {Failed}.",
            pending.Count, PendingCount, FailedCount);
        LastBatchCount = pending.Count;

        bool allSucceeded = true;
        string? firstError = null;
        Exception? firstException = null;

        foreach (var forwarder in _forwarders)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                await forwarder.ForwardAsync(pending, ct);
                sw.Stop();
                _telemetry.OutboxForwardLatencyMs.Record(sw.Elapsed.TotalMilliseconds);
            }
            catch (OperationCanceledException ex) { sweepScope.Fail(ex); throw; }
            catch (Exception ex)
            {
                sw.Stop();
                allSucceeded = false;
                firstError ??= $"[{forwarder.Name}] {ex.Message}";
                firstException ??= ex;
                _logger.LogWarning(ex, "Forwarder '{Name}' failed for batch of {Count} after {Ms:F0}ms.", forwarder.Name, pending.Count, sw.Elapsed.TotalMilliseconds);
            }
        }

        var sentAt = DateTime.UtcNow;
        foreach (var record in pending)
        {
            record.AttemptCount++;
            record.LastAttemptedAtUtc = sentAt;

            if (allSucceeded)
            {
                record.Status = OutboxStatus.Sent;
                record.SentAtUtc = sentAt;
                record.LastError = null;
            }
            else
            {
                record.LastError = firstError;
                if (record.AttemptCount >= _options.MaxRetryAttempts)
                {
                    record.Status = OutboxStatus.Failed;
                    _logger.LogError(
                        "Outbox record {Id} (device {Alias}) marked Failed after {N} attempts. Last error: {Error}",
                        record.Id, record.DeviceAlias, record.AttemptCount, record.LastError);
                }
                else
                {
                    // Exponential backoff: 2^attempts seconds, capped at MaxBackoffSeconds
                    int backoff = Math.Min((int)Math.Pow(2, record.AttemptCount), _options.MaxBackoffSeconds);
                    record.NextRetryAtUtc = sentAt.AddSeconds(backoff);
                }
            }
        }

        await db.SaveChangesAsync(ct);

        if (allSucceeded)
        {
            LastSentAt = sentAt;
            LastError = null;
            _telemetry.OutboxRecordsForwarded.Add(pending.Count);
            sweepScope
                .WithTag("records_forwarded", pending.Count)
                .WithTag("pending", PendingCount)
                .WithTag("failed", FailedCount)
                .Succeed();
            _logger.LogInformation("Forwarded {Count} record(s) successfully.", pending.Count);
        }
        else
        {
            LastError = firstError;
            sweepScope
                .WithTag("records_attempted", pending.Count)
                .WithTag("pending", PendingCount)
                .Fail(firstException!);
        }

        // Refresh counts after the save
        PendingCount = await db.OutboxRecords.CountAsync(r => r.Status == OutboxStatus.Pending, ct);
        FailedCount = await db.OutboxRecords.CountAsync(r => r.Status == OutboxStatus.Failed, ct);

        SweepCompleted?.Invoke();
    }
}
