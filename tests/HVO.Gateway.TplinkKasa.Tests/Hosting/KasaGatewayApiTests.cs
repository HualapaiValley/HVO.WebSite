using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using System.Net;
using System.Text.Json;

namespace HVO.Gateway.TplinkKasa.Tests.Hosting;

[TestClass]
public sealed class KasaGatewayApiTests
{
    [TestMethod]
    public async Task Status_RequiresApiKey()
    {
        await using var factory = new KasaGatewayApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/status");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [TestMethod]
    [DataRow("/gateway-health")]
    [DataRow("/status-review")]
    [DataRow("/status-review/current")]
    public async Task DiagnosticEndpoints_RequireApiKey(string path)
    {
        await using var factory = new KasaGatewayApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [TestMethod]
    [DataRow("/gateway-health")]
    [DataRow("/status-review")]
    [DataRow("/status-review/current")]
    public async Task DiagnosticEndpoints_WithApiKey_ReturnOk(string path)
    {
        await using var factory = new KasaGatewayApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "local-test-key");

        using var response = await client.GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [TestMethod]
    public async Task Inventory_WithApiKey_ReturnsConfiguredInventoryWithoutSecrets()
    {
        await using var factory = new KasaGatewayApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "local-test-key");

        using var response = await client.GetAsync("/inventory");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        json.Should().Contain("tplink-kasa:api-test");
        json.Should().Contain("deviceIdConfigured");
        json.Should().NotContain("RAW_DEVICE_ID_SANITIZED");
        json.Should().NotContain("configured-device-host.example");
        using var document = JsonDocument.Parse(json);
        document.RootElement.GetProperty("devices").GetArrayLength().Should().Be(1);
    }

    private sealed class KasaGatewayApiFactory : WebApplicationFactory<Program>
    {
        private readonly string registryPath = Path.Combine(Path.GetTempPath(), "hvo-kasa-api-tests", Guid.NewGuid().ToString("N"), "kasa-devices.json");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["KasaGateway:ApiKey"] = "local-test-key",
                    ["KasaGateway:PollIntervalSeconds"] = "3600",
                    ["KasaGateway:DeviceRegistryPath"] = registryPath,
                    ["KasaGateway:Devices:0:DeviceId"] = "RAW_DEVICE_ID_SANITIZED",
                    ["KasaGateway:Devices:0:SourceId"] = "tplink-kasa:api-test",
                    ["KasaGateway:Devices:0:Host"] = "configured-device-host.example",
                    ["KasaGateway:Devices:0:ExpectedModel"] = "EP25(US)"
                });
            });
        }

        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.ConfigureServices(services =>
            {
                var hostedService = services.SingleOrDefault(service => service.ImplementationFactory is not null
                    && service.ServiceType == typeof(IHostedService));
                if (hostedService is not null)
                {
                    services.Remove(hostedService);
                }
            });

            return base.CreateHost(builder);
        }
    }
}
