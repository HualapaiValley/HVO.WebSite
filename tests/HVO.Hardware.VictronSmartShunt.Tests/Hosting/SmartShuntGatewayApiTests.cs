using System.Net;
using System.Text.Json;
using FluentAssertions;
using HVO.Hardware.VictronSmartShunt.Configuration;
using HVO.WebSite.Themes.Components.Format;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace HVO.Hardware.VictronSmartShunt.Tests.Hosting;

[TestClass]
public sealed class SmartShuntGatewayApiTests
{
    [TestMethod]
    [DataRow(null)]
    [DataRow("wrong-key")]
    public async Task DiagnosticsEndpoints_RejectMissingOrInvalidApiKey(string? apiKey)
    {
        await using var factory = new SmartShuntGatewayApiFactory();
        using var client = factory.CreateClient();
        if (apiKey is not null)
        {
            client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
        }

        using var response = await client.GetAsync("/diagnostics/status");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [TestMethod]
    public async Task DiagnosticsStatus_ReturnsStandardShapeWithValidKey()
    {
        await using var factory = new SmartShuntGatewayApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "local-test-key");

        using var response = await client.GetAsync("/diagnostics/status");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        document.RootElement.GetProperty("contractVersion").GetString().Should().Be("1.0");
        document.RootElement.GetProperty("identity").GetProperty("gatewayId").GetString().Should().Be("smartshunt");
        document.RootElement.TryGetProperty("health", out _).Should().BeTrue();
        document.RootElement.TryGetProperty("outbox", out _).Should().BeTrue();
    }

    [TestMethod]
    public async Task DiagnosticsStatus_DoesNotExposeApiKey()
    {
        await using var factory = new SmartShuntGatewayApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "local-test-key");

        using var response = await client.GetAsync("/diagnostics/status");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        json.Should().NotContain("local-test-key");
    }

    [TestMethod]
    public void ValidDisplayTimeZoneOverride_ReachesInjectedResolver()
    {
        using var factory = new SmartShuntGatewayApiFactory("America/New_York");

        factory.Services.GetRequiredService<HvoDisplayTimeZone>().Label.Should().Be("America/New_York");
    }

    [TestMethod]
    public void InvalidDisplayTimeZone_FailsStartup()
    {
        using var factory = new SmartShuntGatewayApiFactory("not-a-time-zone");

        var act = () => factory.Services.GetRequiredService<IOptions<SmartShuntOptions>>().Value;

        act.Should().Throw<OptionsValidationException>();
    }

    private sealed class SmartShuntGatewayApiFactory(string displayTimeZoneId = "America/Phoenix") : WebApplicationFactory<SmartShuntOptions>
    {
        private readonly string outboxPath = Path.Combine(Path.GetTempPath(), "hvo-smartshunt-api-tests", Guid.NewGuid().ToString("N"), "outbox.db");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["SmartShunt:Address"] = string.Empty,
                    ["SmartShunt:SourceId"] = "smartshunt-test-source",
                    ["SmartShunt:DeviceId"] = "smartshunt-test-device",
                    ["SmartShunt:DisplayTimeZoneId"] = displayTimeZoneId,
                    ["Outbox:ApiEndpoint"] = "http://localhost:5001/api/v1/power/readings",
                    ["Outbox:ApiKey"] = "local-test-key",
                    ["Outbox:DbPath"] = outboxPath,
                });
            });
        }

        protected override IHost CreateHost(IHostBuilder builder)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outboxPath)!);
            builder.ConfigureServices(services =>
            {
                foreach (var hostedService in services.Where(service => service.ServiceType == typeof(IHostedService)
                    && service.ImplementationFactory is not null).ToArray())
                {
                    services.Remove(hostedService);
                }
            });

            return base.CreateHost(builder);
        }
    }
}
