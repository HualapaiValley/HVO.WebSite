using System.Text.Json;
using System.Text.Json.Serialization;
using HVO.Edge.Contracts;
using HVO.Edge.Outbox;
using Microsoft.Extensions.DependencyInjection;

namespace HVO.Edge.Exporter.HomeAssistant;

internal interface IHomeAssistantObservationWriter
{
    Task<bool> EnqueueAsync(HomeAssistantMappedObservation observation, CancellationToken cancellationToken);
}

internal sealed class HomeAssistantObservationWriter(IServiceScopeFactory scopeFactory) : IHomeAssistantObservationWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<bool> EnqueueAsync(HomeAssistantMappedObservation observation, CancellationToken cancellationToken)
    {
        var payload = new HomeAssistantOutboxPayload(
            observation.Contract,
            JsonSerializer.SerializeToElement(observation.Payload, observation.Payload.GetType(), JsonOptions));
        await using var scope = scopeFactory.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<EdgeOutboxStore<DefaultEdgeOutboxDbContext>>();
        return await store.EnqueueAsync(new EdgeOutboxMessage(
            observation.SourceId,
            observation.RecordedAtUtc.UtcDateTime,
            EdgePayloadTypes.HomeAssistantObservation,
            "1",
            JsonSerializer.Serialize(payload, JsonOptions),
            observation.DeviceId), cancellationToken);
    }
}
