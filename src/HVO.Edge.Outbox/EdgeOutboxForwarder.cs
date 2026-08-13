using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HVO.Edge.Outbox;

public sealed class EdgeOutboxForwarder(
    IServiceScopeFactory scopeFactory,
    IEdgeOutboxBatchSender sender,
    IOptions<EdgeOutboxOptions> options,
    RuntimeOutboxSettings runtimeSettings,
    TimeProvider timeProvider,
    ILogger<EdgeOutboxForwarder> logger) : BackgroundService
{
    private readonly EdgeOutboxOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var lastCompactionDate = DateOnly.MinValue;

        while (!stoppingToken.IsCancellationRequested)
        {
            var didWork = false;
            try
            {
                didWork = await SweepAsync(stoppingToken).ConfigureAwait(false);

                var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
                if (today > lastCompactionDate)
                {
                    await CompactAsync(stoppingToken).ConfigureAwait(false);
                    lastCompactionDate = today;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Edge outbox forwarding sweep failed");
            }

            if (didWork)
                continue;

            try
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(runtimeSettings.EffectiveSweepIntervalSeconds(_options.SweepIntervalSeconds)),
                    timeProvider,
                    stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    internal async Task<bool> SweepAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<EdgeOutboxStore<DefaultEdgeOutboxDbContext>>();
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var records = await store.GetReadyBatchAsync(
            _options.EffectivePayloadTypes,
            now,
            runtimeSettings.EffectiveBatchSize(_options.BatchSize),
            cancellationToken).ConfigureAwait(false);

        if (records.Count == 0)
            return false;

        store.MarkAttempt(records, now);
        // Persist the attempt before network I/O so crashes and cancellation cannot erase delivery history.
        await store.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var outcomes = await sender.SendAsync(records, cancellationToken).ConfigureAwait(false);
            ApplyOutcomes(store, records, outcomes, now);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            const string error = "Outbox batch sender failed";
            foreach (var record in records)
                store.ScheduleRetry(record, error, now, _options.MaxRetryAttempts, _options.MaxBackoffSeconds);

            logger.LogWarning(exception, "Edge outbox sender failed for {RecordCount} record(s)", records.Count);
        }

        await store.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    private void ApplyOutcomes(
        EdgeOutboxStore<DefaultEdgeOutboxDbContext> store,
        IReadOnlyList<EdgeOutboxRecord> records,
        IReadOnlyList<EdgeOutboxSendOutcome>? outcomes,
        DateTime now)
    {
        var byRecordId = outcomes?
            .GroupBy(static outcome => outcome.RecordId)
            .Where(static group => group.Count() == 1)
            .ToDictionary(static group => group.Key, static group => group.Single())
            ?? [];

        foreach (var record in records)
        {
            if (!byRecordId.TryGetValue(record.Id, out var outcome))
            {
                store.ScheduleRetry(record, "Batch sender returned no unique outcome", now, _options.MaxRetryAttempts, _options.MaxBackoffSeconds);
                continue;
            }

            switch (outcome.Status)
            {
                case EdgeOutboxSendStatus.Sent:
                    store.MarkSent(record, now);
                    break;
                case EdgeOutboxSendStatus.TransientFailure:
                    store.ScheduleRetry(record, NormalizeError(outcome.Error, "Transient send failure"), now, _options.MaxRetryAttempts, _options.MaxBackoffSeconds);
                    break;
                case EdgeOutboxSendStatus.PermanentFailure:
                    store.MarkFailed(record, NormalizeError(outcome.Error, "Permanent send failure"), EdgeOutboxFailureKind.Permanent);
                    break;
                default:
                    store.ScheduleRetry(record, "Batch sender returned an unknown outcome", now, _options.MaxRetryAttempts, _options.MaxBackoffSeconds);
                    break;
            }
        }
    }

    private async Task CompactAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<EdgeOutboxStore<DefaultEdgeOutboxDbContext>>();
        if (_options.SentRetentionDays > 0)
            await store.CompactSentAsync(TimeSpan.FromDays(_options.SentRetentionDays), cancellationToken).ConfigureAwait(false);
        if (_options.FailedRetentionDays > 0)
            await store.CompactFailedAsync(TimeSpan.FromDays(_options.FailedRetentionDays), cancellationToken).ConfigureAwait(false);
        await store.ReclaimFreePagesAsync(4096, cancellationToken).ConfigureAwait(false);
    }

    private static string NormalizeError(string? error, string fallback) =>
        string.IsNullOrWhiteSpace(error) ? fallback : error.Trim();
}
