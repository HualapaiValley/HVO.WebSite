using System.Text.Json;
using Bunit;
using FluentAssertions;
using HVO.Edge.Outbox;
using HVO.Gateway.TplinkKasa.Components.Pages;
using HVO.Gateway.TplinkKasa.Configuration;
using HVO.Gateway.TplinkKasa.Devices;
using HVO.Gateway.TplinkKasa.Hosting;
using HVO.Gateway.TplinkKasa.Protocol;
using HVO.Gateway.TplinkKasa.Telemetry;
using HVO.Gateway.TplinkKasa.Workers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Components.Rendering;
using MudBlazor;
using MudBlazor.Services;

namespace HVO.Gateway.TplinkKasa.Tests.Components;

[TestClass]
public sealed class KasaSettingsBunitTests : BunitContext
{
    public KasaSettingsBunitTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        RegisterServices();
    }

    [TestMethod]
    public void RendersConfiguredDevicesAndScanControls()
    {
        var component = Render(BuildSettings);

        component.WaitForAssertion(() => component.Markup.Should().Contain("Desk Lamp"));
        component.Markup.Should().Contain("Device Settings");
        component.Markup.Should().Contain("Scan Network");
        component.Markup.Should().Contain("Configured network");
        component.Markup.Should().Contain("Configured Devices");
    }

    [TestMethod]
    public void RendersAddDeviceForm()
    {
        var component = Render(BuildSettings);

        component.Markup.Should().Contain("Add Device");
        component.Markup.Should().Contain("IP or host");
        component.Markup.Should().Contain("Display name override");
        component.Markup.Should().Contain("MAC address");
        component.Markup.Should().Contain("Add by IP");
    }

    private void RegisterServices()
    {
        var options = Options.Create(new KasaGatewayOptions
        {
            DeviceRegistryPath = Path.Combine(Path.GetTempPath(), $"kasa-{Guid.NewGuid():N}.json"),
            Networks = [new KasaNetworkConfig { Name = "Lab", Cidr = "192.0.2.0/29", DiscoveryEnabled = true }],
            Devices = [Config()],
            DisplayTimeZoneId = "UTC"
        });
        var registry = new KasaDeviceRegistry(options, new TestEnvironment());
        var state = new KasaGatewayState(options, registry);
        state.ApplyResult(Config(), new KasaPollResult(Snapshot(), null));
        Services.AddSingleton<IOptions<KasaGatewayOptions>>(options);
        Services.AddSingleton(state);
        Services.AddSingleton(BuildAdmin(options, registry, state));
        Services.AddHttpClient();
        Services.AddSingleton(sp =>
        {
            var tempServices = new ServiceCollection();
            tempServices.AddHttpClient();
            tempServices.AddLogging();
            var tempProvider = tempServices.BuildServiceProvider();
            return new KasaOutboxForwarder(
                tempProvider.GetRequiredService<IServiceScopeFactory>(),
                tempProvider.GetRequiredService<IHttpClientFactory>(),
                Options.Create(new KasaGatewayOptions.OutboxSection()),
                new RuntimeOutboxSettings(),
                NullLogger<KasaOutboxForwarder>.Instance);
        });
    }

    private static void BuildSettings(RenderTreeBuilder builder)
    {
        builder.OpenComponent<MudPopoverProvider>(0);
        builder.CloseComponent();
        builder.OpenComponent<Settings>(1);
        builder.CloseComponent();
    }

    private static KasaAdminService BuildAdmin(IOptions<KasaGatewayOptions> options, KasaDeviceRegistry registry, KasaGatewayState state)
    {
        var client = new ThrowingKasaClient();
        var parser = new KasaSystemInfoParser();
        var energy = new KasaEnergyParser();
        var metadata = new KasaReadMetadataParser();
        var capability = new KasaCapabilityDetector();
        var identity = new KasaIdentityValidator();
        var poller = new KasaDevicePoller(client, parser, energy, metadata, capability, identity);
        var services = new ServiceCollection().BuildServiceProvider();
        var worker = new KasaGatewayWorker(options, Options.Create(new KasaGatewayOptions.OutboxSection()), registry, poller, new KasaDeviceLocator(client, parser, identity), new KasaDeviceInteractionState(), state, new KasaGatewayTelemetry(), services.GetRequiredService<IServiceScopeFactory>(), NullLogger<KasaGatewayWorker>.Instance);
        return new KasaAdminService(options, new KasaReadOnlyProbe(client, parser, energy, capability), registry, poller, state, worker, NullLogger<KasaAdminService>.Instance);
    }

    private static KasaDeviceConfig Config() => new()
    {
        DeviceId = "desk-lamp",
        SourceId = "tplink-kasa:desk-lamp",
        DisplayName = "Desk Lamp",
        GroupName = "Office",
        IsFavorite = true,
        Host = "192.0.2.10",
        DeviceKind = KasaDeviceKind.Plug,
        Capabilities = [KasaCapability.SwitchState],
        CommandCapabilities = [KasaCommandCapability.SwitchPower]
    };

    private static KasaDeviceSnapshot Snapshot()
    {
        using var json = JsonDocument.Parse("{}");
        return new KasaDeviceSnapshot("desk-lamp", "tplink-kasa:desk-lamp", "192.0.2.10", DateTimeOffset.UtcNow, true, true, null, "Desk Lamp", "HS110", "1.0", "1.0", "AA:BB:CC:DD:EE:FF", KasaDeviceKind.Plug, new HashSet<KasaCapability> { KasaCapability.SwitchState }, new HashSet<KasaMetadataCapability>(), new HashSet<KasaCommandCapability> { KasaCommandCapability.SwitchPower }, true, [], null, new KasaEnergyReading(12.5, 120, 0.1, 0.42, json.RootElement.Clone()), null, null, json.RootElement.Clone());
    }

    private sealed class ThrowingKasaClient : IKasaLegacyClient
    {
        public Task<JsonDocument> SendReadOnlyAsync(string host, int port, string commandJson, CancellationToken cancellationToken) => throw new IOException("No device in bUnit test.");
    }

    private sealed class TestEnvironment : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "Tests";
        public string WebRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
