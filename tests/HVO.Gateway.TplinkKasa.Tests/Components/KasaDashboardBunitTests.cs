using System.Text.Json;
using Bunit;
using FluentAssertions;
using HVO.Gateway.TplinkKasa.Components.Pages;
using HVO.Gateway.TplinkKasa.Configuration;
using HVO.Gateway.TplinkKasa.Devices;
using HVO.Gateway.TplinkKasa.Hosting;
using HVO.Gateway.TplinkKasa.Protocol;
using HVO.Gateway.TplinkKasa.Telemetry;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MudBlazor.Services;

namespace HVO.Gateway.TplinkKasa.Tests.Components;

[TestClass]
public sealed class KasaDashboardBunitTests : BunitContext
{
    public KasaDashboardBunitTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
    }

    [TestMethod]
    public void RendersLoadingState()
    {
        RegisterServices([]);

        var component = Render<Dashboard>();

        component.Markup.Should().Contain("Configured Devices");
    }

    [TestMethod]
    public void RendersEmptyConfiguredDevicesState()
    {
        RegisterServices([]);

        var component = Render<Dashboard>();

        component.WaitForAssertion(() => component.Markup.Should().Contain("No configured devices"));
    }

    [TestMethod]
    public void GroupsDevicesIntoAllFavoritesAndGroupTabs()
    {
        var state = RegisterServices([Config("tplink-kasa:desk-lamp", "Desk Lamp", "Office", true, KasaDeviceKind.Plug)]);
        state.ApplyResult(Config("tplink-kasa:desk-lamp", "Desk Lamp", "Office", true, KasaDeviceKind.Plug), new KasaPollResult(Snapshot("tplink-kasa:desk-lamp", "Desk Lamp", KasaDeviceKind.Plug), null));

        var component = Render<Dashboard>();

        component.WaitForAssertion(() => component.Markup.Should().Contain("Desk Lamp"));
        component.Markup.Should().Contain("Favorites");
        component.Markup.Should().Contain("Office");
        component.Markup.Should().Contain("Plug");
    }

    [TestMethod]
    public void RendersDeviceDetailsDialog()
    {
        var state = RegisterServices([Config("tplink-kasa:desk-lamp", "Desk Lamp", "Office", true, KasaDeviceKind.Plug)]);
        state.ApplyResult(Config("tplink-kasa:desk-lamp", "Desk Lamp", "Office", true, KasaDeviceKind.Plug), new KasaPollResult(Snapshot("tplink-kasa:desk-lamp", "Desk Lamp", KasaDeviceKind.Plug), null));
        var component = Render<Dashboard>();
        component.WaitForAssertion(() => component.Markup.Should().Contain("Desk Lamp"));

        component.Find("button[aria-label='Open device details']").Click();

        component.Markup.Should().Contain("Device Details");
        component.Markup.Should().Contain("Capabilities");
        component.Markup.Should().Contain("HS110");
    }

    private KasaGatewayState RegisterServices(IReadOnlyList<KasaDeviceConfig> devices)
    {
        var options = Options.Create(new KasaGatewayOptions
        {
            DeviceRegistryPath = Path.Combine(Path.GetTempPath(), $"kasa-{Guid.NewGuid():N}.json"),
            Devices = devices.ToList(),
            DashboardRefreshSeconds = 60,
            DisplayTimeZoneId = "UTC"
        });
        var registry = new KasaDeviceRegistry(options, new TestEnvironment());
        var state = new KasaGatewayState(options, registry);
        var admin = BuildAdmin(options, registry, state);
        Services.AddSingleton<IOptions<KasaGatewayOptions>>(options);
        Services.AddSingleton(state);
        Services.AddSingleton(new KasaDeviceInteractionState());
        Services.AddSingleton(admin);
        Services.AddSingleton(new KasaDisplayTimeZoneResolver(options));
        Services.AddSingleton(new KasaDeviceCommandService(new KasaLegacyLabClient(TimeSpan.FromMilliseconds(10)), registry, new KasaSystemInfoParser(), new KasaIdentityValidator(), admin, options, NullLogger<KasaDeviceCommandService>.Instance));
        return state;
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

    private static KasaDeviceConfig Config(string sourceId, string name, string group, bool favorite, KasaDeviceKind kind) => new()
    {
        DeviceId = sourceId.Replace("tplink-kasa:", string.Empty, StringComparison.Ordinal),
        SourceId = sourceId,
        DisplayName = name,
        GroupName = group,
        IsFavorite = favorite,
        Host = "192.0.2.10",
        DeviceKind = kind,
        Capabilities = [KasaCapability.SwitchState],
        CommandCapabilities = [KasaCommandCapability.SwitchPower]
    };

    private static KasaDeviceSnapshot Snapshot(string sourceId, string name, KasaDeviceKind kind)
    {
        using var json = JsonDocument.Parse("{}");
        return new KasaDeviceSnapshot(sourceId.Replace("tplink-kasa:", string.Empty, StringComparison.Ordinal), sourceId, "192.0.2.10", DateTimeOffset.UtcNow, true, true, null, name, "HS110", "1.0", "1.0", "AA:BB:CC:DD:EE:FF", kind, new HashSet<KasaCapability> { KasaCapability.SwitchState }, new HashSet<KasaMetadataCapability>(), new HashSet<KasaCommandCapability> { KasaCommandCapability.SwitchPower }, true, [], null, new KasaEnergyReading(12.5, 120, 0.1, 0.42, json.RootElement.Clone()), null, null, json.RootElement.Clone());
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
