using System.Text.Json;

namespace HVO.Edge.Exporter.HomeAssistant;

internal sealed record HomeAssistantState(
    string EntityId,
    string State,
    JsonElement Attributes,
    DateTimeOffset LastUpdatedUtc);

internal sealed record HomeAssistantMappedObservation(
    string MappingId,
    string Signature,
    string SourceId,
    string DeviceId,
    DateTimeOffset RecordedAtUtc,
    HomeAssistantExportContract Contract,
    object Payload);

internal sealed record HomeAssistantOutboxPayload(
    HomeAssistantExportContract Contract,
    JsonElement Payload);

internal sealed class HomeAssistantExporterCredential
{
    public string? AccessToken { get; set; }
    public string? CentralApiKey { get; set; }
}
