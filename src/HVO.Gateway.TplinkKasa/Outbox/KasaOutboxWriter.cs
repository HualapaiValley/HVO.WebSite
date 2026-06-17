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
        var recordedAt = snapshot.ObservedAtUtc.UtcDateTime;
        var insertedAny = false;

        if (snapshot.Energy is not null)
        {
            insertedAny |= await EnqueueEnergyPayloadAsync(
                snapshot.SourceId,
                snapshot.DeviceId,
                recordedAt,
                snapshot.Energy,
                ct).ConfigureAwait(false);
        }

        foreach (var outlet in snapshot.Outlets.Where(outlet => outlet.Energy is not null))
        {
            var outletSourceId = BuildOutletSourceId(snapshot.SourceId, outlet);
            var outletDeviceId = BuildOutletDeviceId(snapshot.DeviceId, outlet);
            insertedAny |= await EnqueueEnergyPayloadAsync(
                outletSourceId,
                outletDeviceId,
                recordedAt,
                outlet.Energy!,
                ct).ConfigureAwait(false);
        }

        return insertedAny;
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

    private async Task<bool> EnqueueEnergyPayloadAsync(
        string sourceId,
        string? deviceId,
        DateTime recordedAt,
        KasaEnergyReading energy,
        CancellationToken ct)
    {
        var payload = new KasaEnergyPayload
        {
            SourceId = sourceId,
            DeviceId = deviceId,
            RecordedAtUtc = recordedAt,
            LoadPowerW = energy.PowerW,
            GridVoltageV = energy.VoltageV,
        };

        var inserted = await store.EnqueueAsync(new EdgeOutboxMessage(
            SourceId: payload.SourceId,
            RecordedAtUtc: recordedAt,
            PayloadType: KasaOutboxPayloadTypes.Energy,
            PayloadVersion: KasaOutboxPayloadTypes.EnergyVersion,
            PayloadJson: JsonSerializer.Serialize(payload, JsonOptions),
            DeviceId: payload.DeviceId), ct).ConfigureAwait(false);

        if (!inserted)
            logger.LogDebug("Kasa energy outbox duplicate skipped for {SourceId} at {RecordedAt:O}", payload.SourceId, recordedAt);

        return inserted;
    }

    private static string BuildOutletSourceId(string sourceId, KasaOutletSnapshot outlet)
    {
        var outletKey = !string.IsNullOrWhiteSpace(outlet.OutletId)
            ? outlet.OutletId.Trim()
            : outlet.Index?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unknown";
        return $"{sourceId}:outlet:{outletKey}";
    }

    private static string BuildOutletDeviceId(string deviceId, KasaOutletSnapshot outlet)
    {
        if (!string.IsNullOrWhiteSpace(outlet.OutletId))
            return outlet.OutletId.Trim();

        return outlet.Index.HasValue
            ? $"{deviceId}:outlet:{outlet.Index.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}"
            : $"{deviceId}:outlet";
    }
}
