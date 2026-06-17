using System.Text.Json;
using HVO.Edge.Outbox;
using HVO.Hardware.JkBms.Bms;
using Microsoft.Extensions.Logging;

namespace HVO.Hardware.JkBms.Outbox;

public sealed class BmsOutboxWriter(
    EdgeOutboxStore<OutboxDbContext> store,
    ILogger<BmsOutboxWriter> logger)
{
    public async Task<bool> EnqueueReadingAsync(
        string deviceAddress,
        string deviceAlias,
        DateTime recordedAtUtc,
        BmsIngressRecord record,
        CancellationToken ct)
    {
        var payload = JsonSerializer.Serialize(record);
        var enqueued = await store.EnqueueAsync(new EdgeOutboxMessage(
            SourceId: deviceAddress,
            RecordedAtUtc: recordedAtUtc,
            PayloadType: BmsOutboxPayloadTypes.Reading,
            PayloadVersion: BmsOutboxPayloadTypes.ReadingVersion,
            PayloadJson: payload,
            DeviceId: deviceAlias), ct);

        if (!enqueued)
            logger.LogWarning(
                "Outbox duplicate skipped for {Alias} ({Address}) at {Timestamp:O}.",
                deviceAlias,
                deviceAddress,
                recordedAtUtc);

        return enqueued;
    }

    public async Task<bool> EnqueueConfigAsync(
        string deviceAddress,
        string deviceAlias,
        BmsConfigPayload config,
        CancellationToken ct)
    {
        var payload = JsonSerializer.Serialize(config);
        return await store.EnqueueAsync(new EdgeOutboxMessage(
            SourceId: deviceAddress,
            RecordedAtUtc: DateTime.UtcNow,
            PayloadType: BmsOutboxPayloadTypes.Config,
            PayloadVersion: BmsOutboxPayloadTypes.ConfigVersion,
            PayloadJson: payload,
            DeviceId: deviceAlias), ct);
    }

    public async Task<bool> EnqueueDeviceInfoAsync(
        string deviceAddress,
        string deviceAlias,
        BmsDeviceInfoPayload deviceInfo,
        CancellationToken ct)
    {
        var payload = JsonSerializer.Serialize(deviceInfo);
        return await store.EnqueueAsync(new EdgeOutboxMessage(
            SourceId: deviceAddress,
            RecordedAtUtc: DateTime.UtcNow,
            PayloadType: BmsOutboxPayloadTypes.DeviceInfo,
            PayloadVersion: BmsOutboxPayloadTypes.DeviceInfoVersion,
            PayloadJson: payload,
            DeviceId: deviceAlias), ct);
    }
}
