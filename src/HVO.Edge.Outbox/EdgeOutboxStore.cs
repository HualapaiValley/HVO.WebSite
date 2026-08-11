using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

namespace HVO.Edge.Outbox;

public sealed class EdgeOutboxStore<TContext>(TContext db)
    where TContext : EdgeOutboxDbContext
{
    private const int MaxLastErrorLength = 1024;
    private static readonly Regex SensitiveErrorPattern = new(
        @"(?i)(authorization\s*:\s*bearer|bearer|api[-_ ]?key|access[-_ ]?token|password|token)\s*[=:]?\s*[^\s&,;]+",
        RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100));

    private readonly TContext _db = db;

    public TContext Db => _db;

    public async Task<bool> EnqueueAsync(EdgeOutboxMessage message, CancellationToken ct)
    {
        var sourceId = NormalizeRequired(message.SourceId, nameof(message.SourceId));
        var payloadType = NormalizeRequired(message.PayloadType, nameof(message.PayloadType));
        var payloadVersion = NormalizeRequired(message.PayloadVersion, nameof(message.PayloadVersion));
        var payloadJson = NormalizeRequired(message.PayloadJson, nameof(message.PayloadJson));
        var recordedAt = message.RecordedAtUtc.ToUniversalTime();
        if (recordedAt == default)
            throw new InvalidOperationException("Outbox message must include RecordedAtUtc.");

        var record = new EdgeOutboxRecord
        {
            SourceId = sourceId,
            DeviceId = NormalizeOptional(message.DeviceId),
            PayloadType = payloadType,
            PayloadVersion = payloadVersion,
            RecordedAtUtc = recordedAt,
            PayloadJson = payloadJson,
            CreatedAtUtc = DateTime.UtcNow,
        };

        _db.OutboxRecords.Add(record);

        try
        {
            await _db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            _db.Entry(record).State = EntityState.Detached;
            return false;
        }
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

    public Task<int> CountPendingAsync(string payloadType, CancellationToken ct)
    {
        var normalizedPayloadType = NormalizeRequired(payloadType, nameof(payloadType));
        return _db.OutboxRecords.CountAsync(
            r => r.PayloadType == normalizedPayloadType && r.Status == EdgeOutboxStatus.Pending,
            ct);
    }

    public Task<int> CountFailedAsync(CancellationToken ct) =>
        _db.OutboxRecords.CountAsync(r => r.Status == EdgeOutboxStatus.Failed, ct);

    public Task<int> CountFailedAsync(EdgeOutboxFailureKind kind, CancellationToken ct) =>
        _db.OutboxRecords.CountAsync(
            r => r.Status == EdgeOutboxStatus.Failed && r.FailureKind == kind,
            ct);

    public Task<int> CountFailedAsync(string payloadType, CancellationToken ct)
    {
        var normalizedPayloadType = NormalizeRequired(payloadType, nameof(payloadType));
        return _db.OutboxRecords.CountAsync(
            r => r.PayloadType == normalizedPayloadType && r.Status == EdgeOutboxStatus.Failed,
            ct);
    }

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
        record.FailureKind = EdgeOutboxFailureKind.None;
    }

    public void MarkFailed(
        EdgeOutboxRecord record,
        string error,
        EdgeOutboxFailureKind kind = EdgeOutboxFailureKind.Permanent)
    {
        record.Status = EdgeOutboxStatus.Failed;
        record.FailureKind = kind;
        record.LastError = NormalizeError(error);
    }

    public void ScheduleRetry(EdgeOutboxRecord record, string error, DateTime nowUtc, int maxRetryAttempts, int maxBackoffSeconds)
    {
        record.LastError = NormalizeError(error);
        if (record.AttemptCount >= maxRetryAttempts)
        {
            record.Status = EdgeOutboxStatus.Failed;
            record.FailureKind = EdgeOutboxFailureKind.RetryExhausted;
            return;
        }

        var backoff = Math.Min((int)Math.Pow(2, record.AttemptCount), maxBackoffSeconds);
        record.NextRetryAtUtc = nowUtc.AddSeconds(backoff);
        record.Status = EdgeOutboxStatus.Pending;
        record.FailureKind = EdgeOutboxFailureKind.None;
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

    public async Task<int> CompactFailedAsync(TimeSpan failedRetention, CancellationToken ct)
    {
        if (failedRetention < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(failedRetention), failedRetention, "Retention cannot be negative.");

        var cutoff = DateTime.UtcNow.Subtract(failedRetention);
        return await _db.OutboxRecords
            .Where(r => r.Status == EdgeOutboxStatus.Failed
                && ((r.LastAttemptedAtUtc.HasValue && r.LastAttemptedAtUtc.Value < cutoff)
                    || (!r.LastAttemptedAtUtc.HasValue && r.CreatedAtUtc < cutoff)))
            .ExecuteDeleteAsync(ct);
    }

    public async Task<int> RequeueRetryExhaustedAsync(CancellationToken ct)
    {
        var records = await _db.OutboxRecords
            .Where(r => r.Status == EdgeOutboxStatus.Failed
                && r.FailureKind == EdgeOutboxFailureKind.RetryExhausted)
            .ToListAsync(ct);

        foreach (var record in records)
        {
            record.Status = EdgeOutboxStatus.Pending;
            record.FailureKind = EdgeOutboxFailureKind.None;
            record.AttemptCount = 0;
            record.NextRetryAtUtc = DateTime.MinValue;
            record.LastError = AppendRequeueNote(record.LastError, DateTime.UtcNow);
        }

        await _db.SaveChangesAsync(ct);
        return records.Count;
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

    private static string AppendRequeueNote(string? lastError, DateTime requeuedAtUtc)
    {
        var note = $"Requeued after retry exhaustion at {requeuedAtUtc:O}.";
        var updated = string.IsNullOrWhiteSpace(lastError)
            ? note
            : $"{lastError.Trim()} {note}";

        return updated.Length <= MaxLastErrorLength
            ? updated
            : updated[^MaxLastErrorLength..];
    }

    private static string NormalizeError(string error)
    {
        try
        {
            var candidate = error.Trim();
            if (candidate.Length > MaxLastErrorLength * 4)
                candidate = candidate[..(MaxLastErrorLength * 4)];
            var normalized = SensitiveErrorPattern.Replace(candidate, "$1=[REDACTED]");
            return normalized.Length <= MaxLastErrorLength
                ? normalized
                : normalized[..MaxLastErrorLength];
        }
        catch (RegexMatchTimeoutException)
        {
            return "Outbox forwarding error could not be safely normalized.";
        }
    }

    private static bool IsUniqueConstraintViolation(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            var typeName = current.GetType().FullName;
            if (typeName == "Microsoft.Data.Sqlite.SqliteException")
            {
                var sqliteErrorCode = current.GetType().GetProperty("SqliteErrorCode")?.GetValue(current) as int?;
                if (sqliteErrorCode == 19)
                    return true;

                if (current.Message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            if (typeName == "Microsoft.Data.SqlClient.SqlException" || typeName == "System.Data.SqlClient.SqlException")
            {
                foreach (var error in (System.Collections.IEnumerable)current.GetType().GetProperty("Errors")!.GetValue(current)!)
                {
                    var number = error.GetType().GetProperty("Number")?.GetValue(error) as int?;
                    if (number is 2601 or 2627)
                        return true;
                }
            }
        }

        return false;
    }
}
