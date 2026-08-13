using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace HVO.Hardware.VictronSmartShunt.Tests.Hosting;

[TestClass]
[DoNotParallelize]
public sealed class SmartShuntGatewayApiTests
{
    [TestMethod]
    public async Task StandardHeadlessEndpoints_StartWithoutHardwareAndProtectDiagnostics()
    {
        var root = Path.Combine(Path.GetTempPath(), $"smartshunt-api-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "config"));
        Directory.CreateDirectory(Path.Combine(root, "data"));
        Directory.CreateDirectory(Path.Combine(root, "secrets"));
        await File.WriteAllTextAsync(Path.Combine(root, "secrets", "diagnostics-api-key"), "diagnostic-key");
        await File.WriteAllTextAsync(Path.Combine(root, "secrets", "central-ingest-api-key"), "central-key");
        try
        {
            using var environment = new EnvironmentScope(Configuration(root));
            await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.UseEnvironment("Testing"));
            using var client = factory.CreateClient();
            (await client.GetAsync("/health/live")).StatusCode.Should().Be(HttpStatusCode.OK);
            (await client.GetAsync("/diagnostics/status")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            client.DefaultRequestHeaders.Add("X-Api-Key", "diagnostic-key");
            var response = await client.GetAsync("/diagnostics/status");
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var json = await response.Content.ReadAsStringAsync();
            json.Should().Contain("\"gatewayId\":\"smartshunt\"")
                .And.Contain("\"state\":3")
                .And.NotContain("central-key")
                .And.NotContain(root);
        }
        finally { try { Directory.Delete(root, true); } catch (IOException) { } }
    }

    private static Dictionary<string, string?> Configuration(string root) => new()
    {
        ["HVO_EDGE_CONFIG_FILE"] = Path.Combine(root, "config", "gateway.json"),
        ["Edge__Paths__ConfigurationFile"] = Path.Combine(root, "config", "gateway.json"),
        ["Edge__Paths__ConfigDirectory"] = Path.Combine(root, "config"),
        ["Edge__Paths__DataDirectory"] = Path.Combine(root, "data"),
        ["Edge__Paths__SecretsDirectory"] = Path.Combine(root, "secrets"),
        ["Edge__Runtime__ServiceName"] = "hvo-smartshunt", ["Edge__Runtime__GatewayId"] = "smartshunt",
        ["Edge__Runtime__GatewayType"] = "victron-smartshunt-public-gatt", ["Edge__Runtime__Domain"] = "Power",
        ["Edge__Runtime__SourceId"] = "smartshunt-main", ["Edge__Runtime__SiteId"] = "hvo",
        ["Edge__Runtime__DiagnosticsApiKeySecret"] = "diagnostics-api-key",
        ["SmartShunt__Address"] = "AA:BB:CC:DD:EE:FF", ["SmartShunt__CentralIngestBaseEndpoint"] = "http://127.0.0.1/",
        ["SmartShunt__AllowInsecureCentralIngest"] = "true", ["SmartShunt__CentralApiKeySecret"] = "central-ingest-api-key",
        ["HomeAssistant__Mqtt__Enabled"] = "false", ["Outbox__DatabasePath"] = Path.Combine(root, "data", "outbox.db"),
        ["Outbox__PayloadType"] = "com.hvo.smartshunt.observation.v1", ["Outbox__PayloadVersion"] = "1", ["Outbox__MaxBackoffSeconds"] = "10",
    };

    private sealed class EnvironmentScope : IDisposable
    {
        private readonly Dictionary<string, string?> previous = [];
        public EnvironmentScope(IReadOnlyDictionary<string, string?> values) { foreach (var item in values) { previous[item.Key] = Environment.GetEnvironmentVariable(item.Key); Environment.SetEnvironmentVariable(item.Key, item.Value); } }
        public void Dispose() { foreach (var item in previous) Environment.SetEnvironmentVariable(item.Key, item.Value); }
    }
}
