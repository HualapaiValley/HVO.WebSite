using System.Net;
using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using HVO.Hardware.DavisVantagePro2.Protocol.Packets;
using HVO.Hardware.DavisVantagePro2.Workers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace HVO.Hardware.DavisVantagePro2.Tests.Hosting;

[TestClass]
public sealed class DavisGatewayApiTests
{
    [TestMethod]
    [DataRow("/api/weather/current")]
    [DataRow("/diagnostics/health")]
    [DataRow("/diagnostics/status")]
    [DataRow("/diagnostics/outbox")]
    public async Task DiagnosticsEndpoints_RejectMissingApiKey(string path)
    {
        await using var factory = new DavisGatewayApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [TestMethod]
    public async Task WeatherCurrent_ReturnsNoContentWhenNoLatestReading()
    {
        await using var factory = new DavisGatewayApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "local-test-key");

        using var response = await client.GetAsync("/api/weather/current");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [TestMethod]
    public async Task WeatherCurrent_ReturnsCurrentConditionsWhenReadingExists()
    {
        await using var factory = new DavisGatewayApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "local-test-key");
        SetLatestReading(factory.Services.GetRequiredService<WeatherStationWorker>());

        using var response = await client.GetAsync("/api/weather/current");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        json.Should().Contain("outsideTemperatureF");
        json.Should().Contain("72.5");
        json.Should().Contain("display");
    }

    [TestMethod]
    public async Task DiagnosticsStatus_DoesNotExposeApiKey()
    {
        await using var factory = new DavisGatewayApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "local-test-key");

        using var response = await client.GetAsync("/diagnostics/status");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        json.Should().Contain("contractVersion");
        json.Should().Contain("identity");
        json.Should().Contain("outbox");
        json.Should().NotContain("local-test-key");
        using var document = JsonDocument.Parse(json);
        document.RootElement.GetProperty("identity").GetProperty("gatewayId").GetString().Should().Be("davis");
    }

    private static void SetLatestReading(WeatherStationWorker worker)
    {
        var reading = new Loop2Packet
        {
            RecordedAtUtc = DateTime.UtcNow,
            OutsideTemperatureF = 72.5,
            OutsideHumidityPercent = 45,
            BarometricPressureInHg = 29.91,
            WindSpeedMph = 3,
            WindDirectionDegrees = 180,
            DailyRainInches = 0.01,
            SolarRadiationWm2 = 500,
            UvIndex = 4,
        };

        typeof(WeatherStationWorker).GetProperty(nameof(WeatherStationWorker.LatestReading))!
            .SetValue(worker, reading);
        typeof(WeatherStationWorker).GetProperty(nameof(WeatherStationWorker.LastReadingAt))!
            .SetValue(worker, reading.RecordedAtUtc);
    }

    private sealed class DavisGatewayApiFactory : WebApplicationFactory<Program>
    {
        private readonly string outboxPath = Path.Combine(Path.GetTempPath(), "hvo-davis-api-tests", Guid.NewGuid().ToString("N"), "outbox.db");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Station:Host"] = "127.0.0.1",
                    ["Station:StationId"] = "davis-test-station",
                    ["Outbox:ApiEndpoint"] = "http://localhost:5001/api/v1/weather/raw",
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
