using System.Text.Json;
using HVO.Edge.Contracts;
using HVO.Edge.Hosting;
using HVO.Edge.Hosting.Diagnostics;
using HVO.Edge.Outbox;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HVO.Edge.HomeAssistant.Mqtt;

internal sealed class HomeAssistantGatewayDiagnosticsWorker(
    IHomeAssistantMqttProjection projection,
    IEdgeDiagnosticsSnapshotProvider diagnostics,
    IServiceScopeFactory scopeFactory,
    EdgeRuntimeIdentity identity,
    IOptions<HomeAssistantMqttOptions> options,
    TimeProvider timeProvider,
    ILogger<HomeAssistantGatewayDiagnosticsWorker> logger) : BackgroundService
{
    private static readonly TimeSpan PublishInterval = TimeSpan.FromSeconds(30);
    private readonly HomeAssistantDeviceKey key = new(
        identity.SiteId ?? string.Empty,
        identity.GatewayId,
        $"{identity.GatewayId}-diagnostics");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
            return;

        projection.UpsertDevice(CreateDefinition(identity));
        await Task.Delay(PublishInterval, timeProvider, stoppingToken).ConfigureAwait(false);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var snapshot = await diagnostics.GetSnapshotAsync(stoppingToken).ConfigureAwait(false);
                await using var scope = scopeFactory.CreateAsyncScope();
                var outbox = await scope.ServiceProvider.GetRequiredService<EdgeOutboxDiagnostics>()
                    .ReadAsync(stoppingToken)
                    .ConfigureAwait(false);
                projection.PublishCurrentState(CreateState(key, snapshot, outbox, timeProvider.GetUtcNow()));
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Home Assistant gateway diagnostics projection failed");
                projection.PublishCurrentState(new(
                    key,
                    timeProvider.GetUtcNow(),
                    Array.Empty<KeyValuePair<string, JsonElement>>(),
                    available: false));
            }

            await Task.Delay(PublishInterval, timeProvider, stoppingToken).ConfigureAwait(false);
        }
    }

    internal static HomeAssistantDeviceDefinition CreateDefinition(EdgeRuntimeIdentity identity)
    {
        var prefix = $"hvo_{ReadableId(identity.GatewayId)}";
        return new(
            new(identity.SiteId ?? string.Empty, identity.GatewayId, $"{identity.GatewayId}-diagnostics"),
            $"{identity.DisplayName} Diagnostics",
            [
                new HomeAssistantSensorDefinition("gateway_health", "Gateway health", icon: "mdi:heart-pulse", entityCategory: "diagnostic", defaultEntityId: $"sensor.{prefix}_gateway_health"),
                new HomeAssistantSensorDefinition("source_freshness", "Source freshness", icon: "mdi:clock-check-outline", entityCategory: "diagnostic", defaultEntityId: $"sensor.{prefix}_source_freshness"),
                new HomeAssistantSensorDefinition("alert_count", "Active alerts", stateClass: "measurement", icon: "mdi:alert-outline", entityCategory: "diagnostic", defaultEntityId: $"sensor.{prefix}_alert_count", suggestedDisplayPrecision: 0),
                new HomeAssistantSensorDefinition("devices_online", "Devices online", stateClass: "measurement", icon: "mdi:check-network-outline", entityCategory: "diagnostic", defaultEntityId: $"sensor.{prefix}_devices_online", suggestedDisplayPrecision: 0),
                new HomeAssistantSensorDefinition("devices_degraded", "Devices degraded", stateClass: "measurement", icon: "mdi:network-strength-2-alert", entityCategory: "diagnostic", defaultEntityId: $"sensor.{prefix}_devices_degraded", suggestedDisplayPrecision: 0),
                new HomeAssistantSensorDefinition("devices_offline", "Devices offline", stateClass: "measurement", icon: "mdi:network-off-outline", entityCategory: "diagnostic", defaultEntityId: $"sensor.{prefix}_devices_offline", suggestedDisplayPrecision: 0),
                new HomeAssistantSensorDefinition("outbox_pending", "Outbox pending", stateClass: "measurement", icon: "mdi:tray-arrow-up", entityCategory: "diagnostic", defaultEntityId: $"sensor.{prefix}_outbox_pending", suggestedDisplayPrecision: 0),
                new HomeAssistantSensorDefinition("outbox_failed", "Outbox failed", stateClass: "measurement", icon: "mdi:tray-alert", entityCategory: "diagnostic", defaultEntityId: $"sensor.{prefix}_outbox_failed", suggestedDisplayPrecision: 0),
                new HomeAssistantSensorDefinition("outbox_state", "Outbox state", icon: "mdi:database-sync-outline", entityCategory: "diagnostic", defaultEntityId: $"sensor.{prefix}_outbox_state"),
                new HomeAssistantBinarySensorDefinition("gateway_problem", "Gateway problem", "problem", entityCategory: "diagnostic", defaultEntityId: $"binary_sensor.{prefix}_gateway_problem"),
                new HomeAssistantBinarySensorDefinition("outbox_problem", "Outbox problem", "problem", entityCategory: "diagnostic", defaultEntityId: $"binary_sensor.{prefix}_outbox_problem")
            ],
            "HVO",
            identity.GatewayType,
            identity.ServiceVersion);
    }

    internal static HomeAssistantCurrentState CreateState(
        HomeAssistantDeviceKey key,
        EdgeDiagnosticsSnapshot snapshot,
        GatewayOutboxDiagnostics outbox,
        DateTimeOffset observedAtUtc)
    {
        var waiting = snapshot.Health.SourceFreshness is GatewaySampleState.Unknown or GatewaySampleState.Waiting;
        var outboxProblem = !outbox.Schema.IsCompatible
            || outbox.FailedCount > 0
            || outbox.PendingCount > 10
            || !string.IsNullOrWhiteSpace(outbox.LastError);
        var values = new Dictionary<string, JsonElement>
        {
            ["gateway_health"] = JsonSerializer.SerializeToElement(
                waiting
                    ? "waiting"
                    : State(snapshot.Health.State)),
            ["source_freshness"] = JsonSerializer.SerializeToElement(State(snapshot.Health.SourceFreshness)),
            ["alert_count"] = JsonSerializer.SerializeToElement(snapshot.Health.Alerts.Count),
            ["devices_online"] = JsonSerializer.SerializeToElement(snapshot.Devices.Online),
            ["devices_degraded"] = JsonSerializer.SerializeToElement(snapshot.Devices.Degraded),
            ["devices_offline"] = JsonSerializer.SerializeToElement(snapshot.Devices.Offline),
            ["outbox_pending"] = JsonSerializer.SerializeToElement(outbox.PendingCount),
            ["outbox_failed"] = JsonSerializer.SerializeToElement(outbox.FailedCount),
            ["outbox_state"] = JsonSerializer.SerializeToElement(outbox.MaintenanceState),
            ["gateway_problem"] = JsonSerializer.SerializeToElement(!waiting && snapshot.Health.State is GatewayHealthState.Warning or GatewayHealthState.Critical),
            ["outbox_problem"] = JsonSerializer.SerializeToElement(outboxProblem)
        };
        return new(key, observedAtUtc, values);
    }

    private static string State<T>(T value) where T : struct, Enum => value.ToString().ToLowerInvariant();

    private static string ReadableId(string value) => string.Concat(value.ToLowerInvariant().Select(character =>
        character is >= 'a' and <= 'z' or >= '0' and <= '9' ? character : '_'));
}
