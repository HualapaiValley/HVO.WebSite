using System.Text.Json;
using HVO.Edge.Contracts.PowerSystem;
using HVO.Edge.Outbox;

namespace HVO.Gateway.SolarAssistant.Outbox;

/// <summary>Persists mapped SolarAssistant snapshots into the local SQLite outbox.</summary>
public sealed class PowerOutboxWriter(EdgeOutboxStore<OutboxDbContext> store, ILogger<PowerOutboxWriter> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly EdgeOutboxStore<OutboxDbContext> _store = store;
    private readonly ILogger<PowerOutboxWriter> _logger = logger;

    public async Task<bool> EnqueueAsync(PowerReadingPayload payload, CancellationToken ct)
    {
        var sourceId = payload.SourceId?.Trim() ?? string.Empty;
        if (sourceId.Length == 0)
            throw new InvalidOperationException("Power reading payload must include SourceId.");

        var recordedAt = payload.RecordedAtUtc.ToUniversalTime();
        if (recordedAt == default)
            throw new InvalidOperationException("Power reading payload must include RecordedAtUtc.");

        var inserted = await _store.EnqueueAsync(new EdgeOutboxMessage(
            SourceId: sourceId,
            DeviceId: payload.DeviceId,
            RecordedAtUtc: recordedAt,
            PayloadType: PowerOutboxPayloadTypes.PowerReading,
            PayloadVersion: PowerOutboxPayloadTypes.PowerReadingVersion,
            PayloadJson: JsonSerializer.Serialize(payload, JsonOptions)), ct);
        if (!inserted)
        {
            _logger.LogDebug(
                "Power outbox duplicate skipped for {SourceId} at {RecordedAt:O}",
                sourceId,
                recordedAt);
            return false;
        }

        return true;
    }
}
