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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MudBlazor.Services;

namespace HVO.Gateway.TplinkKasa.Tests.Components;

[TestClass]
public sealed class KasaOutletRowBunitTests : BunitContext
{
    public KasaOutletRowBunitTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        RegisterServices();
    }

    [TestMethod]
    public void RendersOutletNameEnergyAndState()
    {
        var component = Render<KasaOutletRow>(parameters => parameters
            .Add(p => p.Device, Device())
            .Add(p => p.Outlet, Outlet())
            .Add(p => p.ShowEnergy, true));

        component.Markup.Should().Contain("Outlet A");
        component.Markup.Should().Contain("12.5 W");
        component.Markup.Should().Contain("On");
    }

    [TestMethod]
    public void DisablesToggleWhenOfflineOrMissingCapability()
    {
        var offline = Device(isOnline: false);
        var component = Render<KasaOutletRow>(parameters => parameters
            .Add(p => p.Device, offline)
            .Add(p => p.Outlet, Outlet()));

        component.Find("button").HasAttribute("disabled").Should().BeTrue();
    }

    [TestMethod]
    public void LightModeCallsSetLightAsync()
    {
        var source = ReadRepositoryFile("src/HVO.Gateway.TplinkKasa/Components/KasaCards/KasaOutletRow.razor");

        source.Should().Contain("CommandMode == KasaOutletCommandMode.LightState");
        source.Should().Contain("CommandService.SetLightAsync");
        source.Should().Contain("KasaOutletCommandFactory.BuildLightCommand");
    }

    [TestMethod]
    public void PowerModeCallsSetPowerAsync()
    {
        var source = ReadRepositoryFile("src/HVO.Gateway.TplinkKasa/Components/KasaCards/KasaOutletRow.razor");

        source.Should().Contain("CommandService.SetPowerAsync");
        source.Should().Contain("UseOutletIndex ? Outlet.Index : null");
    }

    private void RegisterServices()
    {
        var options = Options.Create(new KasaGatewayOptions { DeviceRegistryPath = Path.Combine(Path.GetTempPath(), $"kasa-{Guid.NewGuid():N}.json") });
        var registry = new KasaDeviceRegistry(options, new TestEnvironment());
        Services.AddSingleton(new KasaDeviceInteractionState());
        Services.AddSingleton(new KasaDeviceCommandService(new KasaLegacyLabClient(TimeSpan.FromMilliseconds(10)), registry, new KasaSystemInfoParser(), new KasaIdentityValidator(), null!, options, NullLogger<KasaDeviceCommandService>.Instance));
        Services.AddSingleton<ILogger<KasaOutletRow>>(NullLogger<KasaOutletRow>.Instance);
    }

    private static KasaDeviceStatus Device(bool isOnline = true) => new(
        true,
        "tplink-kasa:desk-lamp",
        "Desk Lamp",
        "Office",
        false,
        "192.0.2.10",
        "AA:BB:CC:DD:EE:FF",
        true,
        DateTimeOffset.UtcNow,
        "UTC",
        isOnline,
        true,
        false,
        isOnline ? null : "Offline",
        null,
        "HS110",
        "1.0",
        "1.0",
        KasaDeviceKind.Plug,
        new HashSet<KasaCapability> { KasaCapability.SwitchState, KasaCapability.EnergyRealtime },
        new HashSet<KasaMetadataCapability>(),
        new HashSet<KasaCommandCapability> { KasaCommandCapability.SwitchPower, KasaCommandCapability.LightColor },
        true,
        [Outlet()],
        new KasaLightStatus(true, 50, 0, 0, 2700, "normal", [], null, null),
        new KasaEnergyStatus(12.5, 120, 0.1, 0.42),
        null,
        null);

    private static KasaOutletStatus Outlet() => new(1, "Outlet A", true, 60, new KasaEnergyStatus(12.5, 120, 0.1, 0.42));

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
