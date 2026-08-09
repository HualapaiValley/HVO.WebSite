using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using HVO.Hardware.Eg4.Dashboard;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace HVO.Hardware.Eg4.Tests.Hosting;

[TestClass]
public sealed class Eg4GatewayApiTests
{
    [TestMethod]
    public async Task Diagnostics_RequireApiKeyAndReturnRedactedStandardContract()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"eg4-api-{Guid.NewGuid():N}.db");
        try
        {
            await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["Eg4:SimulationEnabled"] = "false",
                        ["Outbox:DbPath"] = dbPath,
                        ["Outbox:ApiKey"] = "diagnostic-test-key",
                        ["Outbox:ApiEndpoint"] = "https://localhost:5001/api/v1/power/readings",
                        ["Eg4:Devices:0:Type"] = "Inverter6500Ex",
                        ["Eg4:Devices:0:SourceId"] = "eg4-disabled",
                        ["Eg4:Devices:0:DeviceId"] = "disabled",
                        ["Eg4:Devices:0:Alias"] = "Disabled Inverter",
                        ["Eg4:Devices:0:Enabled"] = "false",
                        ["Eg4:Devices:0:Port"] = "/dev/hvo/secret-hidraw-path",
                        ["Eg4:Devices:0:UnitId"] = "0",
                    }));
            });
            using var client = factory.CreateClient();

            foreach (var path in new[] { "/diagnostics/health", "/diagnostics/status", "/diagnostics/devices", "/diagnostics/outbox" })
                (await client.GetAsync(path)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await client.PutAsJsonAsync("/diagnostics/outbox/settings", new Eg4OutboxSettingsUpdate(BatchSize: 10)))
                .StatusCode.Should().Be(HttpStatusCode.Forbidden);
            client.DefaultRequestHeaders.Add("X-Api-Key", "diagnostic-test-key");
            var response = await client.GetAsync("/diagnostics/status");
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var json = await response.Content.ReadAsStringAsync();
            json.Should().Contain("\"contractVersion\":\"1.0\"")
                .And.Contain("\"gatewayId\":\"eg4\"")
                .And.Contain("gateway.device.poll.attempt")
                .And.NotContain("diagnostic-test-key")
                .And.NotContain("/dev/")
                .And.NotContain("hidraw");

            var devices = await client.GetAsync("/diagnostics/devices");
            devices.StatusCode.Should().Be(HttpStatusCode.OK);
            (await devices.Content.ReadAsStringAsync()).Should().Contain("eg4-disabled").And.NotContain("/dev/").And.NotContain("hidraw");
            var update = await client.PutAsJsonAsync(
                "/diagnostics/outbox/settings",
                new Eg4OutboxSettingsUpdate(BatchSize: 25, SweepIntervalSeconds: 3));
            update.StatusCode.Should().Be(HttpStatusCode.OK);
            var result = await update.Content.ReadFromJsonAsync<Eg4OutboxSettingsResponse>();
            result.Should().NotBeNull();
            result!.BatchSize.Should().Be(25);
            result.SweepIntervalSeconds.Should().Be(3);
            result.IsOverride.Should().BeTrue();
        }
        finally
        {
            try { File.Delete(dbPath); } catch (IOException) { }
        }
    }
}
