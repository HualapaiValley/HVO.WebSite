using System.Text.Json;
using HVO.Hardware.VictronSmartShunt.SmartShunt;
using Microsoft.EntityFrameworkCore;

namespace HVO.Hardware.VictronSmartShunt.Outbox;

public sealed class PowerOutboxWriter(OutboxDbContext db, ILogger<PowerOutboxWriter> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly OutboxDbContext _db = db;
    private readonly ILogger<PowerOutboxWriter> _logger = logger;

    public async Task<bool> EnqueueAsync(PowerReadingPayload payload, CancellationToken ct)
    {
        var sourceId = payload.SourceId?.Trim() ?? string.Empty;
        if (sourceId.Length == 0)
            throw new InvalidOperationException("Power reading payload must include SourceId.");

        var recordedAt = payload.RecordedAtUtc.ToUniversalTime();
        if (recordedAt == default)
            throw new InvalidOperationException("Power reading payload must include RecordedAtUtc.");

        var exists = await _db.OutboxRecords.AnyAsync(r => r.SourceId == sourceId && r.RecordedAtUtc == recordedAt, ct);
        if (exists)
        {
            _logger.LogDebug("SmartShunt outbox duplicate skipped for {SourceId} at {RecordedAt:O}", sourceId, recordedAt);
            return false;
        }

        _db.OutboxRecords.Add(new OutboxRecord
        {
            SourceId = sourceId,
            DeviceId = payload.DeviceId,
            RecordedAtUtc = recordedAt,
            Payload = JsonSerializer.Serialize(payload, JsonOptions),
            CreatedAtUtc = DateTime.UtcNow,
        });

        await _db.SaveChangesAsync(ct);
        return true;
    }
}
