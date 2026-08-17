using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HVO.Edge.HomeAssistant.Mqtt.Tests;

[TestClass]
[TestCategory("Integration")]
[TestCategory("HomeAssistantIntegration")]
public sealed class HomeAssistantMqttIntegrationTests
{
    [TestMethod]
    public async Task RealBrokerAndHomeAssistant_PreserveIdentityAcrossRestartsAndRemoval()
    {
        if (Environment.GetEnvironmentVariable("HVO_HA_TEST_URL") is null)
            Assert.Inconclusive("Run tools/run-home-assistant-integration-tests.sh to provide the disposable environment.");

        var haUrl = Required("HVO_HA_TEST_URL");
        var haToken = Required("HVO_HA_TEST_TOKEN");
        using var http = CreateHaClient(haUrl, haToken);
        var configured = TestSupport.Options();
        configured.Host = Required("HVO_HA_TEST_BROKER_HOST");
        configured.Port = int.Parse(Required("HVO_HA_TEST_BROKER_PORT"), System.Globalization.CultureInfo.InvariantCulture);
        var options = Options.Create(configured);
        var projection = new HomeAssistantMqttProjection(TestSupport.Identity(), options);
        await using var session = new MqttNetSession();
        var credentials = new MqttRuntimeCredential
        {
            Settings = new(
                options.Value.Host!, options.Value.Port, "hvo_integration_test", "hvo-test", Guid.NewGuid().ToString("N"),
                TestSupport.Topics.GatewayAvailability(TestSupport.Key))
        };
        using var worker = new HomeAssistantMqttWorker(
            projection,
            new HomeAssistantMqttCommandRouter(options),
            session,
            credentials,
            TestSupport.Identity(),
            options,
            new CapturedLogger());

        projection.UpsertDevice(TestSupport.Device());
        projection.PublishCurrentState(TestSupport.State(DateTimeOffset.UtcNow, 52.4));
        await worker.StartAsync(CancellationToken.None);
        var entityId = $"sensor.{HomeAssistantMqttIdentity.ReadableEntityId(TestSupport.Key, "voltage")}";
        await WaitForStateAsync(http, entityId, "52.4");
        await AssertSingleEntityAsync(http, entityId);

        projection.PublishCurrentState(TestSupport.State(DateTimeOffset.UtcNow, 52.4, available: false));
        await WaitForStateAsync(http, entityId, "unavailable");
        await RestartBrokerAsync(projection, session, configured, credentials);
        projection.PublishCurrentState(TestSupport.State(DateTimeOffset.UtcNow, 53.1));
        await WaitForStateAsync(http, entityId, "53.1");
        await ClearRetainedAsync(TestSupport.Topics.Discovery(TestSupport.Key));
        await RestartAsync("home-assistant");
        var restartedHaAddress = (await RunComposeAsync("port", "home-assistant", "8123")).Trim();
        using var restartedHttp = CreateHaClient($"http://127.0.0.1:{PublishedPort(restartedHaAddress)}", haToken);
        await WaitForHaAsync(restartedHttp);
        var discovery = await WaitForRetainedAsync(TestSupport.Topics.Discovery(TestSupport.Key));
        discovery.Should().Contain(HomeAssistantMqttIdentity.EntityUniqueId(TestSupport.Key, "voltage"));
        await WaitForStateAsync(restartedHttp, entityId, "53.1");
        await AssertSingleEntityAsync(restartedHttp, entityId);
        projection.PublishCurrentState(TestSupport.State(DateTimeOffset.UtcNow, 54.2));
        await WaitForStateAsync(restartedHttp, entityId, "54.2");

        projection.RemoveDevice(TestSupport.Key).Should().BeTrue();
        await WaitForMissingAsync(restartedHttp, entityId);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await worker.StopAsync(cancellation.Token);
        (await ReadRetainedAsync(TestSupport.Topics.GatewayAvailability(TestSupport.Key))).Should().Be("offline");
    }

    private static async Task WaitForStateAsync(HttpClient http, string entityId, string expected)
    {
        var deadline = DateTime.UtcNow.AddSeconds(120);
        while (DateTime.UtcNow < deadline)
        {
            using var response = await http.GetAsync($"/api/states/{entityId}");
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
                if (json.GetProperty("state").GetString() == expected)
                    return;
            }
            await Task.Delay(500);
        }
        throw new TimeoutException($"Home Assistant entity {entityId} did not reach state {expected}.");
    }

    private static HttpClient CreateHaClient(string url, string token)
    {
        var http = new HttpClient { BaseAddress = new Uri(url) };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return http;
    }

    private static async Task WaitForMissingAsync(HttpClient http, string entityId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            using var response = await http.GetAsync($"/api/states/{entityId}");
            if (response.StatusCode == HttpStatusCode.NotFound)
                return;
            await Task.Delay(500);
        }
        throw new TimeoutException($"Home Assistant entity {entityId} was not removed.");
    }

    private static async Task AssertSingleEntityAsync(HttpClient http, string entityId)
    {
        using var response = await http.GetAsync("/api/states");
        response.EnsureSuccessStatusCode();
        var states = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        states.EnumerateArray()
            .Count(state => state.GetProperty("entity_id").GetString()?.StartsWith(entityId, StringComparison.Ordinal) == true)
            .Should().Be(1);
    }

    private static async Task WaitForHaAsync(HttpClient http)
    {
        var deadline = DateTime.UtcNow.AddSeconds(120);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var response = await http.GetAsync("/api/");
                if (response.IsSuccessStatusCode)
                    return;
            }
            catch (HttpRequestException)
            {
            }
            await Task.Delay(1_000);
        }
        throw new TimeoutException("Home Assistant did not recover after restart.");
    }

    private static async Task RestartAsync(string service)
    {
        await RunComposeAsync("restart", service);
    }

    private static async Task RestartBrokerAsync(
        HomeAssistantMqttProjection projection,
        MqttNetSession session,
        HomeAssistantMqttOptions options,
        MqttRuntimeCredential credential)
    {
        await RunComposeAsync("stop", "mosquitto");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await session.DisconnectAsync(timeout.Token);
        await TestSupport.WaitUntilAsync(() => !projection.GetStatus().Connected, TimeSpan.FromSeconds(30));
        await RunComposeAsync("start", "mosquitto");
        var brokerAddress = (await RunComposeAsync("port", "mosquitto", "1883")).Trim();
        options.Port = PublishedPort(brokerAddress);
        credential.Settings = credential.Settings! with { Port = options.Port };
        await TestSupport.WaitUntilAsync(() => projection.GetStatus().Connected, TimeSpan.FromSeconds(30));
    }

    private static async Task<string> ReadRetainedAsync(string topic, bool allowTimeout = false)
    {
        return (await RunComposeAsync(
            allowTimeout,
            "exec", "-T", "mosquitto", "mosquitto_sub", "-h", "127.0.0.1",
            "-t", topic, "-C", "1", "-W", "5")).Trim();
    }

    private static async Task<string> WaitForRetainedAsync(string topic)
    {
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            var payload = await ReadRetainedAsync(topic, allowTimeout: true);
            if (!string.IsNullOrWhiteSpace(payload))
                return payload;
            await Task.Delay(500);
        }
        throw new TimeoutException($"Retained MQTT topic {topic} was not republished.");
    }

    private static async Task ClearRetainedAsync(string topic)
    {
        await RunComposeAsync(
            "exec", "-T", "mosquitto", "mosquitto_pub", "-h", "127.0.0.1",
            "-t", topic, "-n", "-r", "-q", "1");
    }

    private static Task<string> RunComposeAsync(params string[] arguments) => RunComposeAsync(false, arguments);

    private static async Task<string> RunComposeAsync(bool allowSubscriberTimeout, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("docker")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        startInfo.ArgumentList.Add("compose");
        startInfo.ArgumentList.Add("-p");
        startInfo.ArgumentList.Add(Required("HVO_HA_TEST_COMPOSE_PROJECT"));
        startInfo.ArgumentList.Add("-f");
        startInfo.ArgumentList.Add(Required("HVO_HA_TEST_COMPOSE_FILE"));
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start Docker Compose.");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0 && !(allowSubscriberTimeout && process.ExitCode == 27))
            throw new InvalidOperationException(await standardError);
        return await standardOutput;
    }

    private static string Required(string name) =>
        Environment.GetEnvironmentVariable(name) ?? throw new InvalidOperationException($"{name} is required.");

    private static int PublishedPort(string address) =>
        int.Parse(address[(address.LastIndexOf(':') + 1)..], System.Globalization.CultureInfo.InvariantCulture);

    private sealed class CapturedLogger : ILogger<HomeAssistantMqttWorker>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Console.WriteLine("{0}: {1} {2}", logLevel, formatter(state, exception), exception);
    }
}
