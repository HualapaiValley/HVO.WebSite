using Microsoft.EntityFrameworkCore;

namespace HVO.Edge.Outbox;

public sealed class EdgeOutboxStore<TContext>(TContext db)
    where TContext : EdgeOutboxDbContext
{
    private readonly TContext _db = db;

    public async Task<bool> EnqueueAsync(EdgeOutboxMessage message, CancellationToken ct)
    {
        var sourceId = NormalizeRequired(message.SourceId, nameof(message.SourceId));
        var payloadType = NormalizeRequired(message.PayloadType, nameof(message.PayloadType));
        var payloadVersion = NormalizeRequired(message.PayloadVersion, nameof(message.PayloadVersion));
        var payloadJson = NormalizeRequired(message.PayloadJson, nameof(message.PayloadJson));
        var recordedAt = message.RecordedAtUtc.ToUniversalTime();
        if (recordedAt == default)
            throw new InvalidOperationException("Outbox message must include RecordedAtUtc.");

        var exists = await _db.OutboxRecords.AnyAsync(
            r => r.SourceId == sourceId && r.PayloadType == payloadType && r.RecordedAtUtc == recordedAt,
            ct);
        if (exists)
            return false;

        _db.OutboxRecords.Add(new EdgeOutboxRecord
        {
            SourceId = sourceId,
            DeviceId = NormalizeOptional(message.DeviceId),
            PayloadType = payloadType,
            PayloadVersion = payloadVersion,
            RecordedAtUtc = recordedAt,
            PayloadJson = payloadJson,
            CreatedAtUtc = DateTime.UtcNow,
        });

        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<EdgeOutboxRecord>> GetReadyBatchAsync(
        string payloadType,
        DateTime nowUtc,
        int batchSize,
        CancellationToken ct)
    {
        var normalizedPayloadType = NormalizeRequired(payloadType, nameof(payloadType));
        if (batchSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(batchSize), batchSize, "Batch size must be positive.");

        return await _db.OutboxRecords
            .Where(r => r.PayloadType == normalizedPayloadType
                && r.Status == EdgeOutboxStatus.Pending
                && r.NextRetryAtUtc <= nowUtc)
            .OrderBy(r => r.RecordedAtUtc)
            .Take(batchSize)
            .ToListAsync(ct);
    }

    public Task<int> CountPendingAsync(CancellationToken ct) =>
        _db.OutboxRecords.CountAsync(r => r.Status == EdgeOutboxStatus.Pending, ct);

    public Task<int> CountFailedAsync(CancellationToken ct) =>
        _db.OutboxRecords.CountAsync(r => r.Status == EdgeOutboxStatus.Failed, ct);

    public void MarkAttempt(IEnumerable<EdgeOutboxRecord> records, DateTime nowUtc)
    {
        foreach (var record in records)
        {
            record.AttemptCount++;
            record.LastAttemptedAtUtc = nowUtc;
        }
    }

    public void MarkSent(EdgeOutboxRecord record, DateTime sentAtUtc)
    {
        record.Status = EdgeOutboxStatus.Sent;
        record.SentAtUtc = sentAtUtc;
        record.LastError = null;
    }

    public void MarkFailed(EdgeOutboxRecord record, string error)
    {
        record.Status = EdgeOutboxStatus.Failed;
        record.LastError = error;
    }

    public void ScheduleRetry(EdgeOutboxRecord record, string error, DateTime nowUtc, int maxRetryAttempts, int maxBackoffSeconds)
    {
        record.LastError = error;
        if (record.AttemptCount >= maxRetryAttempts)
        {
            record.Status = EdgeOutboxStatus.Failed;
            return;
        }

        var backoff = Math.Min((int)Math.Pow(2, record.AttemptCount), maxBackoffSeconds);
        record.NextRetryAtUtc = nowUtc.AddSeconds(backoff);
    }

    public async Task<int> CompactSentAsync(TimeSpan sentRetention, CancellationToken ct)
    {
        if (sentRetention < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(sentRetention), sentRetention, "Retention cannot be negative.");

        var cutoff = DateTime.UtcNow.Subtract(sentRetention);
        return await _db.OutboxRecords
            .Where(r => r.Status == EdgeOutboxStatus.Sent && r.SentAtUtc.HasValue && r.SentAtUtc.Value < cutoff)
            .ExecuteDeleteAsync(ct);
    }

    public Task SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);

    private static string NormalizeRequired(string value, string name)
    {
        var normalized = value.Trim();
        if (normalized.Length == 0)
            throw new InvalidOperationException($"Outbox message must include {name}.");
        return normalized;
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
