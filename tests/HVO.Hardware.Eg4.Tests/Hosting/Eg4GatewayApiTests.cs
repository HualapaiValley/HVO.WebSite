using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using HVO.Edge.Contracts;
using HVO.Edge.Outbox;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace HVO.Hardware.Eg4.Tests.Hosting;

[TestClass]
[DoNotParallelize]
public sealed class Eg4GatewayApiTests
{
    [TestMethod]
    public async Task StandardHeadlessEndpoints_ProtectDiagnosticsAndRemainRedacted()
    {
        var root = Path.Combine(Path.GetTempPath(), $"eg4-api-{Guid.NewGuid():N}");
        var config = Path.Combine(root, "config");
        var data = Path.Combine(root, "data");
        var secrets = Path.Combine(root, "secrets");
        Directory.CreateDirectory(config);
        Directory.CreateDirectory(data);
        Directory.CreateDirectory(secrets);
        await File.WriteAllTextAsync(Path.Combine(secrets, "diagnostics-api-key"), "diagnostic-test-key");
        await File.WriteAllTextAsync(Path.Combine(secrets, "central-ingest-api-key"), "central-test-key");

        try
        {
            using var environment = new EnvironmentVariableScope(Configuration(config, data, secrets));
            await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
            });
            using var client = factory.CreateClient();

            (await client.GetAsync("/health/live")).StatusCode.Should().Be(HttpStatusCode.OK);
            (await client.GetAsync("/health")).StatusCode.Should().Be(HttpStatusCode.OK);
            foreach (var path in new[] { "/diagnostics/health", "/diagnostics/status", "/diagnostics/outbox" })
                (await client.GetAsync(path)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

            client.DefaultRequestHeaders.Add("X-Api-Key", "diagnostic-test-key");
            var response = await client.GetAsync("/diagnostics/status");
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var json = await response.Content.ReadAsStringAsync();
            json.Should().Contain("\"gatewayId\":\"eg4\"")
                .And.Contain("\"sourceId\":\"eg4-fleet\"")
                .And.NotContain("diagnostic-test-key")
                .And.NotContain("central-test-key")
                .And.NotContain("/dev/")
                .And.NotContain("hidraw");

            var update = await client.PutAsJsonAsync(
                "/diagnostics/outbox/settings",
                new OutboxSettingsUpdate { BatchSize = 25, SweepIntervalSeconds = 3 });
            update.StatusCode.Should().Be(HttpStatusCode.OK);
            var result = await update.Content.ReadFromJsonAsync<OutboxSettingsResponse>();
            result.Should().NotBeNull();
            result!.BatchSize.Should().Be(25);
            result.SweepIntervalSeconds.Should().Be(3);
            result.IsOverride.Should().BeTrue();
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    private static Dictionary<string, string?> Configuration(
        string config,
        string data,
        string secrets) => new()
    {
        ["HVO_EDGE_CONFIG_FILE"] = Path.Combine(config, "gateway.json"),
        ["Edge__Paths__ConfigurationFile"] = Path.Combine(config, "gateway.json"),
        ["Edge__Paths__ConfigDirectory"] = config,
        ["Edge__Paths__DataDirectory"] = data,
        ["Edge__Paths__SecretsDirectory"] = secrets,
        ["Edge__Runtime__ServiceName"] = "hvo-eg4",
        ["Edge__Runtime__GatewayId"] = "eg4",
        ["Edge__Runtime__GatewayType"] = "eg4-direct",
        ["Edge__Runtime__Domain"] = "Power",
        ["Edge__Runtime__SourceId"] = "eg4-fleet",
        ["Edge__Runtime__SiteId"] = "hvo",
        ["Edge__Runtime__DiagnosticsApiKeySecret"] = "diagnostics-api-key",
        ["Eg4__SimulationEnabled"] = "true",
        ["Eg4__CentralIngestEndpoint"] = "http://127.0.0.1/",
        ["Eg4__AllowInsecureCentralIngest"] = "true",
        ["Eg4__CentralApiKeySecret"] = "central-ingest-api-key",
        ["HomeAssistant__Mqtt__Enabled"] = "false",
        ["Outbox__DatabasePath"] = Path.Combine(data, "outbox.db"),
        ["Outbox__PayloadType"] = "com.hvo.eg4.observation.v1",
        ["Outbox__PayloadVersion"] = "1",
        ["Outbox__MaxBackoffSeconds"] = "10",
        ["ASPNETCORE_ENVIRONMENT"] = "Testing"
    };

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly Dictionary<string, string?> previous = [];

        public EnvironmentVariableScope(IReadOnlyDictionary<string, string?> values)
        {
            foreach (var pair in values)
            {
                previous[pair.Key] = Environment.GetEnvironmentVariable(pair.Key);
                Environment.SetEnvironmentVariable(pair.Key, pair.Value);
            }
        }

        public void Dispose()
        {
            foreach (var pair in previous)
                Environment.SetEnvironmentVariable(pair.Key, pair.Value);
        }
    }
}
