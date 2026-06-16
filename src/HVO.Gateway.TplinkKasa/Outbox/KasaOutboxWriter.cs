using System.Text.Json;
using HVO.Edge.Outbox;
using HVO.Gateway.TplinkKasa.Devices;
using HVO.Gateway.TplinkKasa.Models;

namespace HVO.Gateway.TplinkKasa.Outbox;

public sealed class KasaOutboxWriter(EdgeOutboxStore<OutboxDbContext> store, ILogger<KasaOutboxWriter> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<bool> EnqueueEnergyAsync(KasaDeviceSnapshot snapshot, CancellationToken ct)
    {
        if (snapshot.Energy is null)
            return false;

        var recordedAt = snapshot.ObservedAtUtc.UtcDateTime;
        var payload = new KasaEnergyPayload
        {
            SourceId = snapshot.SourceId,
            DeviceId = snapshot.DeviceId,
            RecordedAtUtc = recordedAt,
            Model = snapshot.Model,
            Alias = snapshot.Alias,
            PowerW = snapshot.Energy.PowerW,
            VoltageV = snapshot.Energy.VoltageV,
            CurrentA = snapshot.Energy.CurrentA,
            EnergyKWh = snapshot.Energy.EnergyKWh,
        };

        var inserted = await store.EnqueueAsync(new EdgeOutboxMessage(
            SourceId: payload.SourceId,
            RecordedAtUtc: recordedAt,
            PayloadType: KasaOutboxPayloadTypes.Energy,
            PayloadVersion: KasaOutboxPayloadTypes.EnergyVersion,
            PayloadJson: JsonSerializer.Serialize(payload, JsonOptions),
            DeviceId: payload.DeviceId), ct);

        if (!inserted)
            logger.LogDebug("Kasa energy outbox duplicate skipped for {SourceId} at {RecordedAt:O}", payload.SourceId, recordedAt);

        return inserted;
    }

    public async Task<bool> EnqueueInventoryAsync(KasaDeviceSnapshot snapshot, CancellationToken ct)
    {
        var recordedAt = snapshot.ObservedAtUtc.UtcDateTime;
        var payload = new KasaInventoryPayload
        {
            SourceId = snapshot.SourceId,
            DeviceId = snapshot.DeviceId,
            RecordedAtUtc = recordedAt,
            DeviceKind = snapshot.DeviceKind.ToString(),
            Model = snapshot.Model,
            Alias = snapshot.Alias,
            HardwareVersion = snapshot.HardwareVersion ?? snapshot.DeviceInfo?.HardwareVersion,
            SoftwareVersion = snapshot.SoftwareVersion ?? snapshot.DeviceInfo?.SoftwareVersion,
            MacAddress = snapshot.MacAddress ?? snapshot.DeviceInfo?.MacAddress,
            HardwareId = snapshot.DeviceInfo?.HardwareId,
            FirmwareId = snapshot.DeviceInfo?.FirmwareId,
            OemId = snapshot.DeviceInfo?.OemId,
            Capabilities = snapshot.Capabilities.Select(capability => capability.ToString()).Order(StringComparer.Ordinal).ToArray(),
        };

        var inserted = await store.EnqueueAsync(new EdgeOutboxMessage(
            SourceId: payload.SourceId,
            RecordedAtUtc: recordedAt,
            PayloadType: KasaOutboxPayloadTypes.Inventory,
            PayloadVersion: KasaOutboxPayloadTypes.InventoryVersion,
            PayloadJson: JsonSerializer.Serialize(payload, JsonOptions),
            DeviceId: payload.DeviceId), ct);

        if (!inserted)
            logger.LogDebug("Kasa inventory outbox duplicate skipped for {SourceId} at {RecordedAt:O}", payload.SourceId, recordedAt);

        return inserted;
    }
}
