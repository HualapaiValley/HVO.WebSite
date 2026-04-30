using HVO.Hardware.JkBms.Outbox;
using HVO.Hardware.JkBms.Outbox.Forwarders;
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
        ILogger<ForwarderCoordinator> logger)
    {
        _scopeFactory = scopeFactory;
        _forwarders = forwarders.ToList();
        _options = options.Value;
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
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OutboxDbContext>();

        var now = DateTime.UtcNow;
        var pending = await db.OutboxRecords
            .Where(r => r.Status == OutboxStatus.Pending && r.NextRetryAtUtc <= now)
            .OrderBy(r => r.RecordedAtUtc)
            .Take(_options.BatchSize)
            .ToListAsync(ct);

        PendingCount = await db.OutboxRecords.CountAsync(r => r.Status == OutboxStatus.Pending, ct);
        FailedCount = await db.OutboxRecords.CountAsync(r => r.Status == OutboxStatus.Failed, ct);

        if (pending.Count == 0)
        {
            SweepCompleted?.Invoke();
            return;
        }

        _logger.LogDebug("Forwarding {Count} pending record(s).", pending.Count);
        LastBatchCount = pending.Count;

        bool allSucceeded = true;
        string? firstError = null;

        foreach (var forwarder in _forwarders)
        {
            try
            {
                await forwarder.ForwardAsync(pending, ct);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                allSucceeded = false;
                firstError ??= $"[{forwarder.Name}] {ex.Message}";
                _logger.LogWarning(ex, "Forwarder '{Name}' failed for batch of {Count}.", forwarder.Name, pending.Count);
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
                        "Outbox record {Id} (device {Alias}) marked Failed after {N} attempts.",
                        record.Id, record.DeviceAlias, record.AttemptCount);
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
            _logger.LogInformation("Forwarded {Count} record(s) successfully.", pending.Count);
        }
        else
        {
            LastError = firstError;
        }

        // Refresh counts after the save
        PendingCount = await db.OutboxRecords.CountAsync(r => r.Status == OutboxStatus.Pending, ct);
        FailedCount = await db.OutboxRecords.CountAsync(r => r.Status == OutboxStatus.Failed, ct);

        SweepCompleted?.Invoke();
    }
}
