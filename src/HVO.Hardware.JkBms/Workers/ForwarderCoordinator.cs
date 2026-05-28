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
    // These properties are written by the sweep background thread and read by
    // Blazor circuit threads. volatile is used for types that support it.
    // DateTime is a 64-bit struct that C# does not permit as a volatile field;
    // DateTime? (Nullable<DateTime>) is larger still. Both are stored as a long
    // (ticks) with Volatile.Read/Write to provide the same acquire/release
    // semantics that volatile gives for supported types.

    private volatile int _pendingCount;
    private volatile int _failedCount;
    private long _lastSentAtTicks;     // 0 means "never sent" (null)
    private volatile string? _lastError;
    private volatile int _lastBatchCount;

    public int PendingCount
    {
        get => _pendingCount;
        private set => _pendingCount = value;
    }

    public int FailedCount
    {
        get => _failedCount;
        private set => _failedCount = value;
    }

    public DateTime? LastSentAt
    {
        get
        {
            var t = Volatile.Read(ref _lastSentAtTicks);
            return t == 0 ? null : new DateTime(t, DateTimeKind.Utc);
        }
        private set => Volatile.Write(ref _lastSentAtTicks, value?.Ticks ?? 0L);
    }

    public string? LastError
    {
        get => _lastError;
        private set => _lastError = value;
    }

    public int LastBatchCount
    {
        get => _lastBatchCount;
        private set => _lastBatchCount = value;
    }

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

        // Run compaction once at startup, then every 24 h.
        var lastCompactionAt = DateTime.MinValue;

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

            // Compact Sent records once per day (or at startup).
            if (_options.SentRetentionDays > 0 &&
                (DateTime.UtcNow - lastCompactionAt).TotalHours >= 24)
            {
                try
                {
                    await CompactAsync(stoppingToken);
                    lastCompactionAt = DateTime.UtcNow;
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "ForwarderCoordinator compaction error (non-fatal)");
                }
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_options.SweepIntervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException) { break; }
        }

        _logger.LogInformation("ForwarderCoordinator stopped.");
    }

    /// <summary>
    /// Deletes <see cref="OutboxStatus.Sent"/> records older than
    /// <see cref="OutboxOptions.SentRetentionDays"/> days to prevent unbounded DB growth.
    /// </summary>
    private async Task CompactAsync(CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.AddDays(-_options.SentRetentionDays);
        await using var serviceScope = _scopeFactory.CreateAsyncScope();
        var db = serviceScope.ServiceProvider.GetRequiredService<OutboxDbContext>();

        var deleted = await db.OutboxRecords
            .Where(r => r.Status == OutboxStatus.Sent && r.SentAtUtc.HasValue && r.SentAtUtc.Value < cutoff)
            .ExecuteDeleteAsync(ct);

        if (deleted > 0)
            _logger.LogInformation(
                "Outbox compaction: deleted {Count} Sent record(s) older than {Days} day(s).",
                deleted, _options.SentRetentionDays);
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
            // Snapshot before invoking to avoid a race on concurrent subscribe/unsubscribe.
            var noWorkHandler = SweepCompleted;
            noWorkHandler?.Invoke();
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

        // Snapshot before invoking to avoid a race on concurrent subscribe/unsubscribe.
        var sweepDoneHandler = SweepCompleted;
        sweepDoneHandler?.Invoke();
    }
}
