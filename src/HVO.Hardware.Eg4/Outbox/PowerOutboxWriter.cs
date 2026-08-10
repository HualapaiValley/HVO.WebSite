using System.Text.Json;
using HVO.Edge.Contracts.PowerSystem;
using HVO.Edge.Outbox;

namespace HVO.Hardware.Eg4.Outbox;

public interface IEg4PowerOutboxWriter
{
    Task<bool> EnqueueAsync(PowerReadingPayload payload, CancellationToken cancellationToken);
    Task<bool> EnqueueMpptDetailAsync(PowerMpptDetailPayload payload, CancellationToken cancellationToken);
    Task<bool> EnqueueInverterDetailAsync(PowerInverterDetailPayload payload, CancellationToken cancellationToken);
}

public sealed class PowerOutboxWriter(
    EdgeOutboxStore<OutboxDbContext> store,
    ILogger<PowerOutboxWriter> logger) : IEg4PowerOutboxWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<bool> EnqueueAsync(PowerReadingPayload payload, CancellationToken cancellationToken)
        => await EnqueueCoreAsync(payload, payload.SourceId, payload.DeviceId, payload.RecordedAtUtc,
            Eg4OutboxPayloadTypes.Reading, Eg4OutboxPayloadTypes.ReadingVersion, cancellationToken);

    public async Task<bool> EnqueueMpptDetailAsync(PowerMpptDetailPayload payload, CancellationToken cancellationToken)
        => await EnqueueCoreAsync(payload, payload.SourceId, payload.DeviceId, payload.RecordedAtUtc,
            Eg4OutboxPayloadTypes.MpptDetail, Eg4OutboxPayloadTypes.MpptDetailVersion, cancellationToken);

    public async Task<bool> EnqueueInverterDetailAsync(PowerInverterDetailPayload payload, CancellationToken cancellationToken)
        => await EnqueueCoreAsync(payload, payload.SourceId, payload.DeviceId, payload.RecordedAtUtc,
            Eg4OutboxPayloadTypes.InverterDetail, Eg4OutboxPayloadTypes.InverterDetailVersion, cancellationToken);

    private async Task<bool> EnqueueCoreAsync<TPayload>(
        TPayload payload,
        string? payloadSourceId,
        string? payloadDeviceId,
        DateTime recordedAtUtc,
        string payloadType,
        string payloadVersion,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var sourceId = payloadSourceId?.Trim() ?? string.Empty;
        var deviceId = payloadDeviceId?.Trim() ?? string.Empty;
        if (sourceId.Length == 0 || deviceId.Length == 0)
            throw new InvalidOperationException("EG4 power readings require stable source and device identities.");
        if (recordedAtUtc == default || recordedAtUtc.Kind != DateTimeKind.Utc)
            throw new InvalidOperationException("EG4 power readings require a UTC RecordedAtUtc value.");

        var inserted = await store.EnqueueAsync(new EdgeOutboxMessage(
            SourceId: sourceId,
            RecordedAtUtc: recordedAtUtc,
            PayloadType: payloadType,
            PayloadVersion: payloadVersion,
            PayloadJson: JsonSerializer.Serialize(payload, JsonOptions),
            DeviceId: deviceId), cancellationToken);
        if (!inserted)
            logger.LogDebug("EG4 outbox duplicate skipped for {SourceId} at {RecordedAt:O}", sourceId, recordedAtUtc);
        return inserted;
    }
}
