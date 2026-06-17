using Bunit;
using FluentAssertions;
using HVO.Gateway.TplinkKasa.Components;
using HVO.Gateway.TplinkKasa.Configuration;
using HVO.Gateway.TplinkKasa.Devices;
using HVO.Gateway.TplinkKasa.Hosting;
using HVO.Gateway.TplinkKasa.Protocol;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MudBlazor.Services;

namespace HVO.Gateway.TplinkKasa.Tests.Components;

[TestClass]
public sealed class KasaDeviceCardsBunitTests : BunitContext
{
    public KasaDeviceCardsBunitTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        RegisterServices();
    }

    [TestMethod]
    public void KasaDeviceCard_RendersBaseDeviceIdentityAndStatus()
    {
        var component = Render<KasaDeviceCard>(parameters => parameters.Add(p => p.Device, Device(KasaDeviceKind.Plug)));

        component.Markup.Should().Contain("Desk Lamp");
        component.Markup.Should().Contain("Office");
        component.Markup.Should().Contain("192.0.2.10");
        component.Markup.Should().Contain("Online");
        component.Markup.Should().Contain("Last observed");
    }

    [TestMethod]
    public void KasaOutletDeviceCard_RendersOutletStateAndEnergy()
    {
        var component = Render<KasaOutletDeviceCard>(parameters => parameters.Add(p => p.Device, Device(KasaDeviceKind.PowerStrip, outlets: true)));

        component.Markup.Should().Contain("Device outlets");
        component.Markup.Should().Contain("Outlet A");
        component.Markup.Should().Contain("12.5 W");
        component.Markup.Should().Contain("On");
    }

    [TestMethod]
    public void KasaLightDeviceCard_RendersLightControlsAndUsesLightCommandMode()
    {
        var component = Render<KasaLightDeviceCard>(parameters => parameters.Add(p => p.Device, Device(KasaDeviceKind.Bulb, light: true)));

        component.Markup.Should().Contain("Light controls");
        component.Markup.Should().Contain("42%");
        ReadRepositoryFile("src/HVO.Gateway.TplinkKasa/Components/KasaCards/KasaLightDeviceCard.razor")
            .Should().Contain("CommandMode=\"KasaOutletCommandMode.LightState\"");
    }

    [TestMethod]
    public void KasaDimmerDeviceCard_RendersBrightnessControls()
    {
        var component = Render<KasaDimmerDeviceCard>(parameters => parameters.Add(p => p.Device, Device(KasaDeviceKind.Dimmer, light: true)));

        component.Markup.Should().Contain("kasa-port-slider");
        component.Markup.Should().Contain("42%");
    }

    [TestMethod]
    public void KasaSwitchDeviceCard_RendersSwitchState()
    {
        var component = Render<KasaSwitchDeviceCard>(parameters => parameters.Add(p => p.Device, Device(KasaDeviceKind.Switch)));

        component.Markup.Should().Contain("On");
        component.Find("button").TextContent.Should().Contain("On");
    }

    private void RegisterServices()
    {
        var options = Options.Create(new KasaGatewayOptions { DeviceRegistryPath = Path.Combine(Path.GetTempPath(), $"kasa-{Guid.NewGuid():N}.json") });
        var registry = new KasaDeviceRegistry(options, new TestEnvironment());
        var parser = new KasaSystemInfoParser();
        var identity = new KasaIdentityValidator();
        var admin = (KasaAdminService)null!;
        Services.AddSingleton<IOptions<KasaGatewayOptions>>(options);
        Services.AddSingleton(new KasaDisplayTimeZoneResolver(options));
        Services.AddSingleton(new KasaDeviceInteractionState());
        Services.AddSingleton(new KasaDeviceCommandService(new KasaLegacyLabClient(TimeSpan.FromMilliseconds(10)), registry, parser, identity, admin, options, NullLogger<KasaDeviceCommandService>.Instance));
    }

    private static KasaDeviceStatus Device(KasaDeviceKind kind, bool outlets = false, bool light = false) => new(
        true,
        "tplink-kasa:desk-lamp",
        "Desk Lamp",
        "Office",
        true,
        "192.0.2.10",
        "AA:BB:CC:DD:EE:FF",
        true,
        new DateTimeOffset(2026, 6, 17, 12, 0, 0, TimeSpan.Zero),
        "UTC",
        true,
        true,
        false,
        null,
        null,
        kind == KasaDeviceKind.Bulb ? "KL130" : "HS110",
        "1.0",
        "1.0",
        kind,
        new HashSet<KasaCapability> { KasaCapability.SwitchState, KasaCapability.EnergyRealtime },
        new HashSet<KasaMetadataCapability>(),
        new HashSet<KasaCommandCapability> { KasaCommandCapability.SwitchPower, KasaCommandCapability.LightColor, KasaCommandCapability.DimLevel },
        true,
        outlets || light ? [new KasaOutletStatus(1, "Outlet A", true, 60, new KasaEnergyStatus(12.5, 120, 0.1, 0.42))] : [],
        light ? new KasaLightStatus(true, 42, 180, 55, 2700, "normal", [], null, null) : null,
        new KasaEnergyStatus(12.5, 120, 0.1, 0.42),
        null,
        null);

    private static string ReadRepositoryFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);
            directory = directory.Parent;
        }

        throw new FileNotFoundException(relativePath);
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
