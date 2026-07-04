using HVO.Edge.Outbox;
using HVO.Enterprise.Telemetry.Abstractions;
using HVO.Hardware.JkBms.Outbox;
using HVO.Hardware.JkBms.Outbox.Forwarders;
using HVO.Hardware.JkBms.Telemetry;
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
/// Each record is marked <see cref="EdgeOutboxStatus.Sent"/> only when ALL forwarders succeed.
/// On partial failure the record stays <see cref="EdgeOutboxStatus.Pending"/> with exponential backoff.
/// After <see cref="OutboxOptions.MaxRetryAttempts"/> failures the record is marked
/// <see cref="EdgeOutboxStatus.Failed"/> to prevent endless retries.
/// </summary>
public sealed class ForwarderCoordinator : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IReadOnlyList<IReadingForwarder> _forwarders;
    private readonly OutboxOptions _options;
    private readonly RuntimeOutboxSettings _runtimeSettings;
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
        RuntimeOutboxSettings runtimeSettings,
        BmsTelemetry telemetry,
        ITelemetryService telemetryService,
        ILogger<ForwarderCoordinator> logger)
    {
        _scopeFactory = scopeFactory;
        _forwarders = forwarders.ToList();
        _options = options.Value;
        _runtimeSettings = runtimeSettings;
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
            bool anyWork = false;
            try
            {
                anyWork = await SweepAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ForwarderCoordinator sweep error");
            }

            // Compact terminal records once per day (or at startup).
            if ((_options.SentRetentionDays > 0 || _options.FailedRetentionDays > 0) &&
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

            if (!anyWork)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(_runtimeSettings.EffectiveSweepIntervalSeconds(_options.SweepIntervalSeconds)), stoppingToken);
                }
                catch (OperationCanceledException) { break; }
            }
        }

        _logger.LogInformation("ForwarderCoordinator stopped.");
    }

    /// <summary>
    /// Deletes terminal outbox records older than
    /// <see cref="OutboxOptions.SentRetentionDays"/> days to prevent unbounded DB growth.
    /// </summary>
    private async Task CompactAsync(CancellationToken ct)
    {
        await using var serviceScope = _scopeFactory.CreateAsyncScope();
        var store = serviceScope.ServiceProvider.GetRequiredService<EdgeOutboxStore<OutboxDbContext>>();

        if (_options.SentRetentionDays > 0)
        {
            var deleted = await store.CompactSentAsync(TimeSpan.FromDays(_options.SentRetentionDays), ct);
            if (deleted > 0)
                _logger.LogInformation(
                    "Outbox compaction: deleted {Count} Sent record(s) older than {Days} day(s).",
                    deleted, _options.SentRetentionDays);
        }

        if (_options.FailedRetentionDays > 0)
        {
            var deleted = await store.CompactFailedAsync(TimeSpan.FromDays(_options.FailedRetentionDays), ct);
            if (deleted > 0)
                _logger.LogInformation(
                    "Outbox compaction: deleted {Count} Failed record(s) older than {Days} day(s).",
                    deleted, _options.FailedRetentionDays);
        }
    }

    private async Task<bool> SweepAsync(CancellationToken ct)
    {
        using var sweepScope = _telemetryService.StartOperation("JkBms.Outbox.Sweep");
        await using var serviceScope = _scopeFactory.CreateAsyncScope();
        var store = serviceScope.ServiceProvider.GetRequiredService<EdgeOutboxStore<OutboxDbContext>>();

        var now = DateTime.UtcNow;
        var pending = await store.GetReadyBatchAsync(BmsOutboxPayloadTypes.Reading, now, _runtimeSettings.EffectiveBatchSize(_options.BatchSize), ct);

        PendingCount = await store.CountPendingAsync(ct);
        FailedCount = await store.CountFailedAsync(ct);
        _telemetry.SetOutboxQueueDepth(PendingCount);

        if (pending.Count == 0)
        {
            var snapshotsDrained = await DrainStandaloneSnapshotPayloadsAsync(store, now, ct);
            if (snapshotsDrained > 0)
            {
                PendingCount = await store.CountPendingAsync(ct);
                _telemetry.SetOutboxQueueDepth(PendingCount);
            }

            sweepScope.WithTag("pending", 0).Succeed();
            // Snapshot before invoking to avoid a race on concurrent subscribe/unsubscribe.
            var noWorkHandler = SweepCompleted;
            noWorkHandler?.Invoke();
            return snapshotsDrained > 0;
        }

        _logger.LogDebug("Forwarding {Count} pending record(s). Pending total: {Total}, Failed total: {Failed}.",
            pending.Count, PendingCount, FailedCount);
        LastBatchCount = pending.Count;

        string? firstError = null;
        Exception? firstException = null;
        var permanentlyFailedIds = new HashSet<long>();
        var transientFailureIds = new HashSet<long>();

        foreach (var forwarder in _forwarders)
        {
            var recordsForForwarder = pending
                .Where(r => !permanentlyFailedIds.Contains(r.Id))
                .ToList();
            if (recordsForForwarder.Count == 0)
                break;

            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                await forwarder.ForwardAsync(recordsForForwarder, ct);
                sw.Stop();
                _telemetry.OutboxForwardLatencyMs.Record(sw.Elapsed.TotalMilliseconds);
            }
            catch (OperationCanceledException ex) { sweepScope.Fail(ex); throw; }
            catch (PermanentForwarderException ex)
            {
                sw.Stop();
                firstError ??= $"[{forwarder.Name}] {ex.Message}";
                firstException ??= ex;
                var recordsById = recordsForForwarder.ToDictionary(r => r.Id);
                foreach (var failure in ex.FailedRecords)
                {
                    if (!recordsById.TryGetValue(failure.RecordId, out var record))
                        continue;

                    permanentlyFailedIds.Add(record.Id);
                    transientFailureIds.Remove(record.Id);
                    store.MarkFailed(record, $"[{forwarder.Name}] {failure.Error}", EdgeOutboxFailureKind.Permanent);
                    _logger.LogError(
                        "Outbox record {Id} (device {Alias}) permanently failed in forwarder '{Name}'. Error: {Error}",
                        record.Id,
                        record.DeviceId,
                        forwarder.Name,
                        failure.Error);
                }

                _logger.LogWarning(
                    ex,
                    "Forwarder '{Name}' permanently failed {FailedCount} record(s) from batch of {Count} after {Ms:F0}ms.",
                    forwarder.Name,
                    ex.FailedRecords.Count,
                    recordsForForwarder.Count,
                    sw.Elapsed.TotalMilliseconds);
            }
            catch (Exception ex)
            {
                sw.Stop();
                firstError ??= $"[{forwarder.Name}] {ex.Message}";
                firstException ??= ex;
                foreach (var record in recordsForForwarder)
                    transientFailureIds.Add(record.Id);
                _logger.LogWarning(ex, "Forwarder '{Name}' transiently failed for batch of {Count} after {Ms:F0}ms.", forwarder.Name, recordsForForwarder.Count, sw.Elapsed.TotalMilliseconds);
            }
        }

        var sentAt = DateTime.UtcNow;
        var sentCount = 0;
        var transientCount = 0;
        store.MarkAttempt(pending, sentAt);
        foreach (var record in pending)
        {
            if (permanentlyFailedIds.Contains(record.Id))
                continue;

            if (transientFailureIds.Contains(record.Id))
            {
                store.ScheduleRetry(record, firstError ?? "Forwarder failed.", sentAt, _options.MaxRetryAttempts, _options.MaxBackoffSeconds);
                transientCount++;
                if (record.Status == EdgeOutboxStatus.Failed)
                    _logger.LogError(
                        "Outbox record {Id} (device {Alias}) marked Failed after {N} attempts. Last error: {Error}",
                        record.Id,
                        record.DeviceId,
                        record.AttemptCount,
                        record.LastError);
            }
            else
            {
                store.MarkSent(record, sentAt);
                sentCount++;
            }
        }

        await store.SaveChangesAsync(ct);

        var snapshotSentCount = await DrainStandaloneSnapshotPayloadsAsync(store, now, ct);

        if (transientFailureIds.Count == 0 && permanentlyFailedIds.Count == 0)
        {
            LastSentAt = sentAt;
            LastError = null;
            _telemetry.OutboxRecordsForwarded.Add(sentCount);
            if (snapshotSentCount > 0)
                _telemetry.OutboxRecordsForwarded.Add(snapshotSentCount);
            sweepScope
                .WithTag("records_forwarded", sentCount)
                .WithTag("snapshots_drained", snapshotSentCount)
                .WithTag("pending", PendingCount)
                .WithTag("failed", FailedCount)
                .Succeed();
            _logger.LogInformation("Forwarded {Count} record(s) successfully.", sentCount);
        }
        else
        {
            LastError = firstError;
            if (sentCount > 0)
            {
                LastSentAt = sentAt;
                _telemetry.OutboxRecordsForwarded.Add(sentCount);
            }

            sweepScope
                .WithTag("records_attempted", pending.Count)
                .WithTag("records_forwarded", sentCount)
                .WithTag("records_transient_failed", transientCount)
                .WithTag("records_permanent_failed", permanentlyFailedIds.Count)
                .WithTag("pending", PendingCount)
                .Fail(firstException!);
        }

        if (sentCount > 0)
        {
            var requeued = await store.RequeueRetryExhaustedAsync(ct);
            if (requeued > 0)
                _logger.LogInformation("Outbox auto-requeued {Count} retry-exhausted record(s) after successful forward.", requeued);
        }

        // Refresh counts after the save
        PendingCount = await store.CountPendingAsync(ct);
        FailedCount = await store.CountFailedAsync(ct);

        // Snapshot before invoking to avoid a race on concurrent subscribe/unsubscribe.
        var sweepDoneHandler = SweepCompleted;
        sweepDoneHandler?.Invoke();

        return true;
    }

    private async Task<int> DrainStandaloneSnapshotPayloadsAsync(
        EdgeOutboxStore<OutboxDbContext> store,
        DateTime now,
        CancellationToken ct)
    {
        var sentCount = 0;
        sentCount += await DrainStandaloneSnapshotPayloadTypeAsync(store, BmsOutboxPayloadTypes.Config, now, ct);
        sentCount += await DrainStandaloneSnapshotPayloadTypeAsync(store, BmsOutboxPayloadTypes.DeviceInfo, now, ct);
        if (sentCount > 0)
        {
            await store.SaveChangesAsync(ct);
            _logger.LogInformation(
                "Drained {Count} standalone JK BMS config/device-info snapshot outbox record(s); bundled reading payloads remain the website-compatible forwarding path.",
                sentCount);
        }

        return sentCount;
    }

    private async Task<int> DrainStandaloneSnapshotPayloadTypeAsync(
        EdgeOutboxStore<OutboxDbContext> store,
        string payloadType,
        DateTime now,
        CancellationToken ct)
    {
        var pending = await store.GetReadyBatchAsync(payloadType, now, _runtimeSettings.EffectiveBatchSize(_options.BatchSize), ct);
        if (pending.Count == 0)
            return 0;

        store.MarkAttempt(pending, now);
        foreach (var record in pending)
            store.MarkSent(record, now);
        return pending.Count;
    }
}
