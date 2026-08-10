using System.Text.Json;
using HVO.Edge.Contracts;
using HVO.Edge.Contracts.PowerSystem;
using HVO.Edge.Outbox;
using Microsoft.EntityFrameworkCore;

namespace HVO.Gateway.SolarAssistant.Outbox;

public sealed class PowerInventoryConfigurationWriter(EdgeOutboxStore<OutboxDbContext> store)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly EdgeOutboxStore<OutboxDbContext> _store = store;

    public async Task<bool> EnqueueDeviceInventoryAsync(PowerDeviceInventoryPayload payload, CancellationToken ct)
    {
        var payloadJson = JsonSerializer.Serialize(payload, JsonOptions);
        if (await HasLatestPayloadAsync(payload.SourceId, PowerOutboxPayloadTypes.DeviceInventory, payloadJson, ct))
            return false;

        return await _store.EnqueueAsync(new EdgeOutboxMessage(
            SourceId: payload.SourceId,
            DeviceId: payload.DeviceId,
            RecordedAtUtc: payload.RecordedAtUtc,
            PayloadType: PowerOutboxPayloadTypes.DeviceInventory,
            PayloadVersion: PowerOutboxPayloadTypes.DeviceInventoryVersion,
            PayloadJson: payloadJson), ct);
    }

    public async Task<bool> EnqueueConfigurationAsync(PowerConfigurationPayload payload, CancellationToken ct)
    {
        var payloadJson = JsonSerializer.Serialize(payload, JsonOptions);
        if (await HasLatestPayloadAsync(payload.SourceId, PowerOutboxPayloadTypes.Configuration, payloadJson, ct))
            return false;

        return await _store.EnqueueAsync(new EdgeOutboxMessage(
            SourceId: payload.SourceId,
            DeviceId: payload.DeviceId,
            RecordedAtUtc: payload.RecordedAtUtc,
            PayloadType: PowerOutboxPayloadTypes.Configuration,
            PayloadVersion: PowerOutboxPayloadTypes.ConfigurationVersion,
            PayloadJson: payloadJson), ct);
    }

    public async Task<bool> EnqueueEnergyAsync(PowerEnergyPayload payload, CancellationToken ct)
    {
        var payloadJson = JsonSerializer.Serialize(payload, JsonOptions);
        if (await HasLatestPayloadAsync(payload.SourceId, PowerOutboxPayloadTypes.Energy, payloadJson, ct))
            return false;

        return await _store.EnqueueAsync(new EdgeOutboxMessage(
            SourceId: payload.SourceId,
            DeviceId: payload.DeviceId,
            RecordedAtUtc: payload.RecordedAtUtc,
            PayloadType: PowerOutboxPayloadTypes.Energy,
            PayloadVersion: PowerOutboxPayloadTypes.EnergyVersion,
            PayloadJson: payloadJson), ct);
    }

    public async Task<bool> EnqueueInverterDetailAsync(PowerInverterDetailPayload payload, CancellationToken ct)
    {
        var payloadJson = JsonSerializer.Serialize(payload, JsonOptions);
        if (await HasLatestPayloadAsync(payload.SourceId, PowerOutboxPayloadTypes.InverterDetail, payloadJson, ct))
            return false;

        return await _store.EnqueueAsync(new EdgeOutboxMessage(
            SourceId: payload.SourceId,
            DeviceId: payload.DeviceId,
            RecordedAtUtc: payload.RecordedAtUtc,
            PayloadType: PowerOutboxPayloadTypes.InverterDetail,
            PayloadVersion: PowerOutboxPayloadTypes.InverterDetailVersion,
            PayloadJson: payloadJson), ct);
    }

    public async Task<bool> EnqueueMpptDetailAsync(PowerMpptDetailPayload payload, CancellationToken ct)
    {
        var payloadJson = JsonSerializer.Serialize(payload, JsonOptions);
        return await _store.EnqueueAsync(new EdgeOutboxMessage(
            SourceId: payload.SourceId,
            DeviceId: payload.DeviceId,
            RecordedAtUtc: payload.RecordedAtUtc,
            PayloadType: PowerOutboxPayloadTypes.MpptDetail,
            PayloadVersion: PowerOutboxPayloadTypes.MpptDetailVersion,
            PayloadJson: payloadJson), ct);
    }

    public async Task<bool> EnqueueGatewayStatusAsync(GatewayStatusPayload payload, CancellationToken ct)
    {
        var payloadJson = JsonSerializer.Serialize(payload, JsonOptions);
        return await _store.EnqueueAsync(new EdgeOutboxMessage(
            SourceId: payload.SourceId,
            DeviceId: payload.DeviceId,
            RecordedAtUtc: payload.RecordedAtUtc,
            PayloadType: PowerOutboxPayloadTypes.GatewayStatus,
            PayloadVersion: PowerOutboxPayloadTypes.GatewayStatusVersion,
            PayloadJson: payloadJson), ct);
    }

    private async Task<bool> HasLatestPayloadAsync(string sourceId, string payloadType, string payloadJson, CancellationToken ct)
    {
        var latest = await _store.Db.OutboxRecords
            .AsNoTracking()
            .Where(r => r.SourceId == sourceId && r.PayloadType == payloadType)
            .OrderByDescending(r => r.RecordedAtUtc)
            .Select(r => r.PayloadJson)
            .FirstOrDefaultAsync(ct);

        return string.Equals(RemoveRecordedAt(payloadJson), RemoveRecordedAt(latest), StringComparison.Ordinal);
    }

    private static string? RemoveRecordedAt(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
            return null;

        using var document = JsonDocument.Parse(payloadJson);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (string.Equals(property.Name, "recordedAtUtc", StringComparison.OrdinalIgnoreCase))
                    continue;
                property.WriteTo(writer);
            }
            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }
}
