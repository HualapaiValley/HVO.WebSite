using FluentAssertions;
using HVO.Gateway.TplinkKasa.Configuration;
using HVO.Gateway.TplinkKasa.Devices;
using HVO.Gateway.TplinkKasa.Hosting;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;

namespace HVO.Gateway.TplinkKasa.Tests.Hosting;

[TestClass]
public sealed class KasaGatewayHealthCheckTests
{
    [TestMethod]
    public async Task CheckHealthAsync_ReturnsHealthyWhenAllDevicesOnline()
    {
        var options = CreateConfig(deviceCount: 2);
        var state = CreateState(options);
        foreach (var config in options.Devices)
        {
            state.ApplyResult(config, new KasaPollResult(CreateSnapshot(config), null));
        }
        var healthCheck = new KasaGatewayHealthCheck(state);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        result.Status.Should().Be(HealthStatus.Healthy);
    }

    [TestMethod]
    public async Task CheckHealthAsync_ReturnsDegradedWhenSomeDevicesOffline()
    {
        var options = CreateConfig(deviceCount: 2);
        var state = CreateState(options);
        state.ApplyResult(options.Devices[0], new KasaPollResult(CreateSnapshot(options.Devices[0]), null));
        state.ApplyResult(options.Devices[1], new KasaPollResult(null, "Polling failed."));
        var healthCheck = new KasaGatewayHealthCheck(state);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        result.Status.Should().Be(HealthStatus.Degraded);
        result.Description.Should().Be("1 of 2 configured TP-Link/Kasa device(s) are online.");
    }

    [TestMethod]
    public async Task CheckHealthAsync_ReturnsUnhealthyWhenNoDevicesOnline()
    {
        var options = CreateConfig(deviceCount: 2);
        var state = CreateState(options);
        foreach (var config in options.Devices)
        {
            state.ApplyResult(config, new KasaPollResult(null, "Polling failed."));
        }
        var healthCheck = new KasaGatewayHealthCheck(state);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Be("No configured TP-Link/Kasa devices are online.");
    }

    [TestMethod]
    public async Task CheckHealthAsync_ReturnsUnhealthyWhenPollFailureLeavesNoDevicesOnline()
    {
        var options = CreateConfig(deviceCount: 2);
        var state = CreateState(options);
        state.MarkPollFailed("Gateway poll failed.");
        var healthCheck = new KasaGatewayHealthCheck(state);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Be("No configured TP-Link/Kasa devices are online.");
    }

    private static KasaGatewayState CreateState(KasaGatewayOptions options)
    {
        var root = Path.Combine(Path.GetTempPath(), "hvo-kasa-health-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        options.DeviceRegistryPath = "kasa-devices.json";
        var registry = new KasaDeviceRegistry(Options.Create(options), new TestWebHostEnvironment(root));
        return new KasaGatewayState(Options.Create(options), registry);
    }

    private static KasaDeviceSnapshot CreateSnapshot(KasaDeviceConfig config) => new(
        config.DeviceId,
        config.EffectiveSourceId,
        config.Host,
        DateTimeOffset.UtcNow,
        true,
        true,
        null,
        config.DisplayName,
        "EP25(US)",
        "2.0",
        "1.0.0",
        "AA:BB:CC:DD:EE:01",
        KasaDeviceKind.Plug,
        new HashSet<KasaCapability> { KasaCapability.SwitchState },
        new HashSet<KasaMetadataCapability> { KasaMetadataCapability.Diagnostics },
        new HashSet<KasaCommandCapability> { KasaCommandCapability.SwitchPower },
        true,
        [],
        null,
        null,
        null,
        null,
        System.Text.Json.JsonDocument.Parse("{}").RootElement.Clone());

    private static KasaGatewayOptions CreateConfig(int deviceCount)
    {
        var options = new KasaGatewayOptions { GatewayId = "test-gateway" };
        for (var i = 1; i <= deviceCount; i++)
        {
            options.Devices.Add(new KasaDeviceConfig
            {
                Enabled = true,
                DeviceId = $"device-{i}",
                SourceId = $"tplink-kasa:device-{i}",
                Host = $"device-{i}.example",
                DisplayName = $"Device {i}",
                ExpectedModel = "EP25(US)",
                Capabilities = [KasaCapability.SwitchState]
            });
        }

        return options;
    }

    private sealed class TestWebHostEnvironment(string contentRootPath) : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";

        public string ApplicationName { get; set; } = "HVO.Gateway.TplinkKasa.Tests";

        public string WebRootPath { get; set; } = contentRootPath;

        public IFileProvider WebRootFileProvider { get; set; } = new PhysicalFileProvider(contentRootPath);

        public string ContentRootPath { get; set; } = contentRootPath;

        public IFileProvider ContentRootFileProvider { get; set; } = new PhysicalFileProvider(contentRootPath);
    }
}
