using System.Text.Json;
using System.Text.Json.Serialization;
using HVO.Edge.Contracts;
using HVO.Edge.Outbox;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;

namespace HVO.Edge.Exporter.HomeAssistant;

internal interface IHomeAssistantObservationWriter
{
    Task<HomeAssistantPersistenceOutcome> EnqueueAsync(HomeAssistantMappedObservation observation, CancellationToken cancellationToken);
}

internal enum HomeAssistantPersistenceOutcome { Inserted, IdenticalReplay, Conflict }

internal static class HomeAssistantObservationSerialization
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public static string Serialize(HomeAssistantExportContract contract, object payload) =>
        JsonSerializer.Serialize(new HomeAssistantOutboxPayload(contract,
            JsonSerializer.SerializeToElement(payload, payload.GetType(), JsonOptions)), JsonOptions);
}

internal sealed class HomeAssistantObservationWriter(IServiceScopeFactory scopeFactory) : IHomeAssistantObservationWriter
{
    public async Task<HomeAssistantPersistenceOutcome> EnqueueAsync(HomeAssistantMappedObservation observation, CancellationToken cancellationToken)
    {
        var payload = HomeAssistantObservationSerialization.Serialize(observation.Contract, observation.Payload);
        // Match EdgeOutboxStore's envelope normalization for insertion and replay.
        // Preserve the typed payload bytes so a real content change remains a conflict.
        var sourceId = observation.SourceId.Trim();
        var deviceId = string.IsNullOrWhiteSpace(observation.DeviceId) ? null : observation.DeviceId.Trim();
        await using var scope = scopeFactory.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<EdgeOutboxStore<DefaultEdgeOutboxDbContext>>();
        var inserted = await store.EnqueueAsync(new EdgeOutboxMessage(
            sourceId,
            observation.RecordedAtUtc.UtcDateTime,
            EdgePayloadTypes.HomeAssistantObservation,
            "1",
            payload,
            deviceId), cancellationToken);
        if (inserted)
            return HomeAssistantPersistenceOutcome.Inserted;

        // A uniqueness collision proves identity only, never equality of intended data.
        // Read both pending and sent rows without replacing canonical history.
        var existing = await store.Db.OutboxRecords.AsNoTracking().SingleOrDefaultAsync(record =>
            record.SourceId == sourceId
            && record.PayloadType == EdgePayloadTypes.HomeAssistantObservation
            && record.RecordedAtUtc == observation.RecordedAtUtc.UtcDateTime, cancellationToken);
        return existing is not null && existing.DeviceId == deviceId
            && existing.PayloadVersion == "1" && existing.PayloadJson == payload
                ? HomeAssistantPersistenceOutcome.IdenticalReplay
                : HomeAssistantPersistenceOutcome.Conflict;
    }
}
