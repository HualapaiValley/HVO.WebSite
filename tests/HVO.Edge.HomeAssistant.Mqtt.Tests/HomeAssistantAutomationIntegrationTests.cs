using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HVO.Edge.HomeAssistant.Mqtt.Tests;

[TestClass]
[TestCategory("Integration")]
[TestCategory("HomeAssistantIntegration")]
public sealed class HomeAssistantAutomationIntegrationTests
{
    [TestMethod]
    public async Task PinnedHomeAssistant_SimulatedCriticalStatesTriggerAlertsAndRecoveries()
    {
        if (Environment.GetEnvironmentVariable("HVO_HA_TEST_URL") is null)
            Assert.Inconclusive("Run tools/run-home-assistant-integration-tests.sh to provide the disposable environment.");

        using var http = CreateHaClient(Required("HVO_HA_TEST_URL"), Required("HVO_HA_TEST_TOKEN"));
        var configured = TestSupport.Options();
        configured.Host = Required("HVO_HA_TEST_BROKER_HOST");
        configured.Port = int.Parse(Required("HVO_HA_TEST_BROKER_PORT"), System.Globalization.CultureInfo.InvariantCulture);
        var options = Options.Create(configured);
        var projection = new HomeAssistantMqttProjection(TestSupport.Identity(), options);
        await using var session = new MqttNetSession();
        var credentials = new MqttRuntimeCredential
        {
            Settings = new(
                configured.Host,
                configured.Port,
                "hvo_automation_integration_test",
                "hvo-test",
                Guid.NewGuid().ToString("N"),
                TestSupport.Topics.GatewayAvailability(TestSupport.Key))
        };
        using var worker = new HomeAssistantMqttWorker(
            projection,
            session,
            credentials,
            options,
            NullLogger<HomeAssistantMqttWorker>.Instance);

        projection.UpsertDevice(SimulatorDefinition());
        var observedAt = DateTimeOffset.UtcNow;
        projection.PublishCurrentState(SimulatorState(observedAt, "healthy", "live", 0, 80));
        await worker.StartAsync(CancellationToken.None);
        var sourceOfflineAutomation = await WaitForAutomationEntityAsync(http, "HVO source offline");
        var sourceStaleAutomation = await WaitForAutomationEntityAsync(http, "HVO source stale");
        var gatewayCriticalAutomation = await WaitForAutomationEntityAsync(http, "HVO gateway critical");
        var outboxBacklogAutomation = await WaitForAutomationEntityAsync(http, "HVO outbox backlog");
        var batteryCriticalAutomation = await WaitForAutomationEntityAsync(http, "HVO battery state of charge critical");
        var sourceOnlineAutomation = await WaitForAutomationEntityAsync(http, "HVO source online");
        var sourceFreshAutomation = await WaitForAutomationEntityAsync(http, "HVO source fresh");
        var gatewayRecoveredAutomation = await WaitForAutomationEntityAsync(http, "HVO gateway recovered");
        var outboxRecoveredAutomation = await WaitForAutomationEntityAsync(http, "HVO outbox recovered");
        var batteryRecoveredAutomation = await WaitForAutomationEntityAsync(http, "HVO battery state of charge recovered");
        await WaitForStateAsync(http, "sensor.hvo_eg4_gateway_health", "healthy");

        projection.PublishCurrentState(SimulatorState(observedAt = observedAt.AddSeconds(1), "healthy", "live", 0, 80, available: false));
        await WaitForStateAsync(http, "sensor.hvo_eg4_gateway_health", "unavailable");
        await WaitForAutomationAsync(http, sourceOfflineAutomation);

        projection.PublishCurrentState(SimulatorState(observedAt = observedAt.AddSeconds(1), "healthy", "live", 0, 80));
        await WaitForStateAsync(http, "sensor.hvo_eg4_gateway_health", "healthy");
        await WaitForAutomationAsync(http, sourceOnlineAutomation);
        projection.PublishCurrentState(SimulatorState(observedAt = observedAt.AddSeconds(1), "healthy", "stale", 0, 80));
        await WaitForAutomationAsync(http, sourceStaleAutomation);

        projection.PublishCurrentState(SimulatorState(observedAt = observedAt.AddSeconds(1), "healthy", "live", 0, 80));
        await WaitForAutomationAsync(http, sourceFreshAutomation);
        projection.PublishCurrentState(SimulatorState(observedAt = observedAt.AddSeconds(1), "critical", "live", 0, 80));
        await WaitForAutomationAsync(http, gatewayCriticalAutomation);

        projection.PublishCurrentState(SimulatorState(observedAt = observedAt.AddSeconds(1), "critical", "live", 0, 80, available: false));
        await WaitForStateAsync(http, "sensor.hvo_eg4_gateway_health", "unavailable");
        projection.PublishCurrentState(SimulatorState(observedAt = observedAt.AddSeconds(1), "healthy", "live", 0, 80));
        await WaitForAutomationAsync(http, gatewayRecoveredAutomation);
        projection.PublishCurrentState(SimulatorState(observedAt = observedAt.AddSeconds(1), "healthy", "live", 11, 80));
        await WaitForAutomationAsync(http, outboxBacklogAutomation);

        projection.PublishCurrentState(SimulatorState(observedAt = observedAt.AddSeconds(1), "healthy", "live", 0, 80));
        await WaitForAutomationAsync(http, outboxRecoveredAutomation);
        projection.PublishCurrentState(SimulatorState(observedAt = observedAt.AddSeconds(1), "healthy", "live", 0, 19));
        await WaitForAutomationAsync(http, batteryCriticalAutomation);
        projection.PublishCurrentState(SimulatorState(observedAt.AddSeconds(1), "healthy", "live", 0, 26));
        await WaitForAutomationAsync(http, batteryRecoveredAutomation);

        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await worker.StopAsync(cancellation.Token);
    }

    private static HomeAssistantDeviceDefinition SimulatorDefinition() => new(
        TestSupport.Key,
        "HVO Automation Simulator",
        [
            new HomeAssistantSensorDefinition("gateway_health", "EG4 gateway health", defaultEntityId: "sensor.hvo_eg4_gateway_health"),
            new HomeAssistantSensorDefinition("source_freshness", "EG4 source freshness", defaultEntityId: "sensor.hvo_eg4_source_freshness"),
            new HomeAssistantSensorDefinition("outbox_pending", "EG4 outbox pending", stateClass: "measurement", defaultEntityId: "sensor.hvo_eg4_outbox_pending"),
            new HomeAssistantSensorDefinition("battery_soc", "SmartShunt state of charge", "%", "battery", "measurement", defaultEntityId: "sensor.hvo_3xhvo_10xsmartshunt_21xsmartshunt_x2dlifepo4_21xstate_x5fof_x5fcharge")
        ],
        "HVO",
        "Automation simulator");

    private static HomeAssistantCurrentState SimulatorState(
        DateTimeOffset observedAt,
        string gatewayHealth,
        string sourceFreshness,
        int outboxPending,
        int batterySoc,
        bool available = true) => new(
        TestSupport.Key,
        observedAt,
        new Dictionary<string, JsonElement>
        {
            ["gateway_health"] = JsonSerializer.SerializeToElement(gatewayHealth),
            ["source_freshness"] = JsonSerializer.SerializeToElement(sourceFreshness),
            ["outbox_pending"] = JsonSerializer.SerializeToElement(outboxPending),
            ["battery_soc"] = JsonSerializer.SerializeToElement(batterySoc)
        },
        available);

    private static HttpClient CreateHaClient(string url, string token)
    {
        var http = new HttpClient { BaseAddress = new Uri(url) };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return http;
    }

    private static async Task WaitForStateAsync(HttpClient http, string entityId, string expected)
    {
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            using var response = await http.GetAsync($"/api/states/{entityId}");
            if (response.IsSuccessStatusCode)
            {
                var state = await response.Content.ReadFromJsonAsync<JsonElement>();
                if (state.GetProperty("state").GetString() == expected)
                    return;
            }
            await Task.Delay(500);
        }
        throw new TimeoutException($"Home Assistant entity {entityId} did not reach state {expected}.");
    }

    private static async Task WaitForAutomationAsync(HttpClient http, string entityId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            using var response = await http.GetAsync($"/api/states/{entityId}");
            if (response.IsSuccessStatusCode)
            {
                var state = await response.Content.ReadFromJsonAsync<JsonElement>();
                if (state.GetProperty("attributes").TryGetProperty("last_triggered", out var lastTriggered)
                    && lastTriggered.ValueKind == JsonValueKind.String)
                    return;
            }
            await Task.Delay(250);
        }
        throw new TimeoutException($"Home Assistant automation {entityId} was not triggered.");
    }

    private static async Task<string> WaitForAutomationEntityAsync(HttpClient http, string alias)
    {
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            var states = await http.GetFromJsonAsync<JsonElement>("/api/states");
            var automation = states.EnumerateArray().FirstOrDefault(state =>
                state.GetProperty("entity_id").GetString()?.StartsWith("automation.", StringComparison.Ordinal) == true
                && state.GetProperty("attributes").TryGetProperty("friendly_name", out var friendlyName)
                && friendlyName.GetString() == alias);
            if (automation.ValueKind != JsonValueKind.Undefined)
                return automation.GetProperty("entity_id").GetString()!;
            await Task.Delay(500);
        }
        throw new TimeoutException($"Home Assistant automation '{alias}' was not loaded.");
    }

    private static string Required(string name) =>
        Environment.GetEnvironmentVariable(name) ?? throw new InvalidOperationException($"{name} is required.");
}
