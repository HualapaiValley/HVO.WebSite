using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using HVO.Edge.Contracts;
using HVO.Edge.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace HVO.Edge.Exporter.HomeAssistant.Tests;

[TestClass]
[TestCategory("Integration")]
[TestCategory("HomeAssistantIntegration")]
public sealed class HomeAssistantExporterIntegrationTests
{
    [TestMethod]
    public async Task PinnedHomeAssistant_ReconcilesAndExportsTypedStateChanges()
    {
        var haUrl = Environment.GetEnvironmentVariable("HVO_HA_TEST_URL");
        if (haUrl is null)
            Assert.Inconclusive("Run tools/run-home-assistant-integration-tests.sh to provide the disposable environment.");
        var token = Required("HVO_HA_TEST_TOKEN");
        using var http = new HttpClient { BaseAddress = new Uri(haUrl) };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        await SetValueAsync(http, "input_number.hvo_test_kasa_power_input", 125);
        await SetValueAsync(http, "input_number.hvo_test_kasa_voltage_input", 121.5);
        await SetValueAsync(http, "input_number.hvo_test_govee_temperature_input", 20);
        await SetValueAsync(http, "input_number.hvo_test_govee_humidity_input", 42);

        var options = Options.Create(CreateOptions(haUrl.Replace("http://", "ws://", StringComparison.Ordinal)));
        new HomeAssistantExporterOptionsValidator().Validate(null, options.Value).Succeeded.Should().BeTrue();
        var projector = new HomeAssistantStateProjector(options);
        var source = new HomeAssistantWebSocketClient(options, new HomeAssistantExporterCredential { AccessToken = token });
        var directory = Path.Combine(Path.GetTempPath(), "hvo-ha-exporter-integration", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var hostBuilder = Host.CreateApplicationBuilder();
        hostBuilder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Outbox:DatabasePath"] = Path.Combine(directory, "outbox.db"),
            ["Outbox:PayloadType"] = EdgePayloadTypes.HomeAssistantObservation,
            ["Outbox:PayloadVersion"] = "1",
            ["Outbox:SweepIntervalSeconds"] = "1",
            ["Outbox:MaxRetryAttempts"] = "1",
            ["Outbox:MaxBackoffSeconds"] = "10"
        });
        hostBuilder.Services.AddHvoEdgeOutbox(hostBuilder.Configuration);
        hostBuilder.Services.AddHttpClient("HvoEdge");
        hostBuilder.Services.AddSingleton<IOptions<HomeAssistantExporterOptions>>(options);
        hostBuilder.Services.AddSingleton(new HomeAssistantExporterCredential
        {
            AccessToken = token,
            CentralApiKey = "integration-test-key"
        });
        hostBuilder.Services.AddSingleton<IEdgeOutboxBatchSender, HomeAssistantOutboxBatchSender>();
        hostBuilder.Services.AddHostedService<HomeAssistantRetryRequeueWorker>();
        using var host = hostBuilder.Build();
        await SetIngestStateAsync("unavailable");
        await host.StartAsync();
        var writer = new HomeAssistantObservationWriter(host.Services.GetRequiredService<IServiceScopeFactory>());
        var observations = new List<HomeAssistantMappedObservation>();
        var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var session = source.RunSessionAsync(
            async (snapshot, cancellationToken) =>
            {
                foreach (var observation in projector.Reconcile(snapshot))
                {
                    await writer.EnqueueAsync(observation, cancellationToken);
                    projector.Acknowledge(observation);
                    observations.Add(observation);
                }
            },
            async (state, cancellationToken) =>
            {
                if (projector.Apply(state) is { } observation)
                {
                    await writer.EnqueueAsync(observation, cancellationToken);
                    projector.Acknowledge(observation);
                    observations.Add(observation);
                    if (observation.Contract == HomeAssistantExportContract.PowerReading)
                        changed.TrySetResult();
                }
            },
            cancellation.Token);

        await WaitUntilAsync(() => observations.Count >= 2, cancellation.Token);
        await WaitForRetryExhaustedCountAsync(host.Services, minimum: 2, cancellation.Token);
        await SetIngestStateAsync("available");
        var requeueWorker = host.Services.GetServices<IHostedService>().OfType<HomeAssistantRetryRequeueWorker>().Single();
        (await requeueWorker.RequeueAsync(cancellation.Token)).Should().BeGreaterThanOrEqualTo(2);
        await WaitForDrainAsync(host.Services, sentMinimum: 2, cancellation.Token);
        await SetValueAsync(http, "input_number.hvo_test_kasa_power_input", 321);
        await changed.Task.WaitAsync(cancellation.Token);
        await WaitForDrainAsync(host.Services, sentMinimum: 3, cancellation.Token);
        await AssertCanonicalRequestsAsync();
        await cancellation.CancelAsync();
        await session.Invoking(task => task).Should().ThrowAsync<OperationCanceledException>();
        await host.StopAsync();

        observations.Should().Contain(observation => observation.Contract == HomeAssistantExportContract.PowerReading);
        observations.Should().Contain(observation => observation.Contract == HomeAssistantExportContract.WeatherRaw);
        observations.Count(observation => observation.Contract == HomeAssistantExportContract.PowerReading).Should().Be(2);
        Directory.Delete(directory, recursive: true);
    }

    private static HomeAssistantExporterOptions CreateOptions(string websocketBase) => new()
    {
        Enabled = true,
        Endpoint = $"{websocketBase}/api/websocket",
        CentralIngestEndpoint = Required("HVO_HA_TEST_INGEST_URL") + "/",
        AllowInsecureCentralIngest = true,
        AllowTestPlatforms = true,
        Mappings = [
            new()
            {
                Id = "kasa-test",
                Contract = HomeAssistantExportContract.PowerReading,
                SourceId = "kasa:test",
                DeviceId = "kasa-test",
                ExpectedPlatform = "template",
                Entities = [
                    new() { EntityId = "sensor.fixture_test_kasa_power", Metric = HomeAssistantMetric.LoadPowerW },
                    new() { EntityId = "sensor.fixture_test_kasa_voltage", Metric = HomeAssistantMetric.GridVoltageV }
                ]
            },
            new()
            {
                Id = "govee-test",
                Contract = HomeAssistantExportContract.WeatherRaw,
                SourceId = "govee:test",
                DeviceId = "govee-test",
                ExpectedPlatform = "template",
                Entities = [
                    new() { EntityId = "sensor.fixture_test_govee_temperature", Metric = HomeAssistantMetric.Temperature },
                    new() { EntityId = "sensor.fixture_test_govee_humidity", Metric = HomeAssistantMetric.HumidityPercent }
                ]
            }
        ]
    };

    private static async Task SetValueAsync(HttpClient http, string entityId, double value)
    {
        using var response = await http.PostAsJsonAsync("/api/services/input_number/set_value", new { entity_id = entityId, value });
        response.EnsureSuccessStatusCode();
    }

    private static async Task SetIngestStateAsync(string state)
    {
        using var response = await new HttpClient().PostAsync(
            $"{Required("HVO_HA_TEST_INGEST_URL")}/__test/central-ingest/{state}", null);
        response.EnsureSuccessStatusCode();
    }

    private static async Task WaitForRetryExhaustedCountAsync(
        IServiceProvider services,
        int minimum,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            await using var scope = services.CreateAsyncScope();
            var count = await scope.ServiceProvider.GetRequiredService<DefaultEdgeOutboxDbContext>()
                .OutboxRecords.CountAsync(
                    record => record.Status == EdgeOutboxStatus.Failed
                        && record.FailureKind == EdgeOutboxFailureKind.RetryExhausted,
                    cancellationToken);
            if (count >= minimum)
                return;
            await Task.Delay(100, cancellationToken);
        }
    }

    private static async Task WaitForDrainAsync(
        IServiceProvider services,
        int sentMinimum,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            await using var scope = services.CreateAsyncScope();
            var records = await scope.ServiceProvider.GetRequiredService<DefaultEdgeOutboxDbContext>()
                .OutboxRecords.AsNoTracking().ToListAsync(cancellationToken);
            records.Count(record => record.Status == EdgeOutboxStatus.Failed).Should().Be(0);
            if (records.Count(record => record.Status == EdgeOutboxStatus.Sent) >= sentMinimum
                && records.All(record => record.Status == EdgeOutboxStatus.Sent))
                return;
            await Task.Delay(100, cancellationToken);
        }
    }

    private static async Task AssertCanonicalRequestsAsync()
    {
        using var response = await new HttpClient().GetAsync($"{Required("HVO_HA_TEST_INGEST_URL")}/__admin/requests");
        response.EnsureSuccessStatusCode();
        using var document = await System.Text.Json.JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var requests = document.RootElement.GetProperty("requests").EnumerateArray()
            .Select(request => request.GetProperty("request")).ToArray();
        var power = requests.First(request => request.GetProperty("url").GetString() == "/api/v1/power/readings");
        var weather = requests.First(request => request.GetProperty("url").GetString() == "/api/v1/weather/raw/batch");
        HeaderValue(power, "X-Api-Key").Should().Be("integration-test-key");
        HeaderValue(weather, "X-Api-Key").Should().Be("integration-test-key");
        power.GetProperty("body").GetString().Should().Contain("homeassistant-tplink");
        weather.GetProperty("body").GetString().Should().Contain("homeassistant-govee-ble");
    }

    private static string? HeaderValue(System.Text.Json.JsonElement request, string name)
    {
        var value = request.GetProperty("headers").GetProperty(name);
        return value.ValueKind == System.Text.Json.JsonValueKind.Array ? value[0].GetString() : value.GetString();
    }

    private static async Task WaitUntilAsync(Func<bool> condition, CancellationToken cancellationToken)
    {
        while (!condition())
            await Task.Delay(100, cancellationToken);
    }

    private static string Required(string name) =>
        Environment.GetEnvironmentVariable(name) ?? throw new InvalidOperationException($"{name} is required.");
}
