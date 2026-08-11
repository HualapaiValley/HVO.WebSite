using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace HVO.Hardware.JkBms.Tests.Hosting;

[TestClass]
[DoNotParallelize]
public sealed class JkBmsGatewayApiTests
{
    [TestMethod]
    public async Task StandardHeadlessEndpoints_StartWithoutBluetoothAndProtectRedactedDiagnostics()
    {
        var root = Path.Combine(Path.GetTempPath(), $"jkbms-api-{Guid.NewGuid():N}");
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
                builder.UseEnvironment("Testing"));
            using var client = factory.CreateClient();

            (await client.GetAsync("/health/live")).StatusCode.Should().Be(HttpStatusCode.OK);
            (await client.GetAsync("/health")).StatusCode.Should().Be(HttpStatusCode.OK);
            foreach (var path in new[] { "/diagnostics/health", "/diagnostics/status", "/diagnostics/outbox" })
                (await client.GetAsync(path)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

            client.DefaultRequestHeaders.Add("X-Api-Key", "diagnostic-test-key");
            var response = await client.GetAsync("/diagnostics/status");
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var json = await response.Content.ReadAsStringAsync();
            json.Should().Contain("\"gatewayId\":\"jkbms\"")
                .And.Contain("\"sourceId\":\"jkbms-fleet\"")
                .And.NotContain("diagnostic-test-key")
                .And.NotContain("central-test-key")
                .And.NotContain(data)
                .And.NotContain(secrets);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    private static Dictionary<string, string?> Configuration(string config, string data, string secrets) => new()
    {
        ["HVO_EDGE_CONFIG_FILE"] = Path.Combine(config, "gateway.json"),
        ["Edge__Paths__ConfigurationFile"] = Path.Combine(config, "gateway.json"),
        ["Edge__Paths__ConfigDirectory"] = config,
        ["Edge__Paths__DataDirectory"] = data,
        ["Edge__Paths__SecretsDirectory"] = secrets,
        ["Edge__Runtime__ServiceName"] = "hvo-jkbms",
        ["Edge__Runtime__GatewayId"] = "jkbms",
        ["Edge__Runtime__GatewayType"] = "jk-bms-direct",
        ["Edge__Runtime__Domain"] = "Power",
        ["Edge__Runtime__SourceId"] = "jkbms-fleet",
        ["Edge__Runtime__SiteId"] = "hvo",
        ["Edge__Runtime__DiagnosticsApiKeySecret"] = "diagnostics-api-key",
        ["JkBms__CentralIngestEndpoint"] = "http://127.0.0.1/api/v1/bms/readings",
        ["JkBms__AllowInsecureCentralIngest"] = "true",
        ["JkBms__CentralApiKeySecret"] = "central-ingest-api-key",
        ["JkBms__Devices"] = null,
        ["HomeAssistant__Mqtt__Enabled"] = "false",
        ["Outbox__DatabasePath"] = Path.Combine(data, "outbox.db"),
        ["Outbox__PayloadType"] = "com.hvo.bms.reading.v1",
        ["Outbox__PayloadVersion"] = "1",
        ["Outbox__MaxBackoffSeconds"] = "10",
        ["ASPNETCORE_ENVIRONMENT"] = "Testing",
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
