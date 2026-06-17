using FluentAssertions;
using HVO.Gateway.TplinkKasa.Components;
using HVO.Gateway.TplinkKasa.Devices;
using HVO.Gateway.TplinkKasa.Hosting;

namespace HVO.Gateway.TplinkKasa.Tests.Components;

[TestClass]
public sealed class KasaOutletCommandModeTests
{
    [TestMethod]
    public void KasaLightDeviceCard_ConfiguresOutletRowForLightCommands()
    {
        var source = ReadRepositoryFile("src/HVO.Gateway.TplinkKasa/Components/KasaCards/KasaLightDeviceCard.razor");

        source.Should().Contain("CommandMode=\"KasaOutletCommandMode.LightState\"");
        source.Should().NotContain("UseLightCommand=\"true\"");
    }

    [TestMethod]
    public void KasaOutletRow_LightModeDispatchesToSetLightAsync()
    {
        var source = ReadRepositoryFile("src/HVO.Gateway.TplinkKasa/Components/KasaCards/KasaOutletRow.razor");

        source.Should().Contain("CommandMode == KasaOutletCommandMode.LightState");
        source.Should().Contain("CommandService.SetLightAsync");
        source.Should().Contain("CommandService.SetPowerAsync");
        source.Should().Contain("KasaCommandCapability.LightColor");
    }

    [TestMethod]
    public void BuildLightCommand_PreservesKnownLightState_WhenTogglingPower()
    {
        var device = CreateLightDevice(new KasaLightStatus(
            IsOn: true,
            Brightness: 42,
            Hue: 180,
            Saturation: 55,
            ColorTemperature: 2700,
            Mode: "normal",
            PreferredStates: [],
            BulbDetails: null,
            DefaultBehavior: null));

        var command = KasaOutletCommandFactory.BuildLightCommand(device, targetIsOn: false);

        command.IsOn.Should().BeFalse();
        command.Brightness.Should().Be(42);
        command.Hue.Should().Be(180);
        command.Saturation.Should().Be(55);
        command.ColorTemperature.Should().Be(2700);
    }

    [TestMethod]
    public void BuildLightCommand_ClampsInvalidOrMissingLightState_ToCommandSafeValues()
    {
        var device = CreateLightDevice(new KasaLightStatus(
            IsOn: false,
            Brightness: 0,
            Hue: 999,
            Saturation: -20,
            ColorTemperature: 10000,
            Mode: "normal",
            PreferredStates: [],
            BulbDetails: null,
            DefaultBehavior: null));

        var command = KasaOutletCommandFactory.BuildLightCommand(device, targetIsOn: true);

        command.IsOn.Should().BeTrue();
        command.Brightness.Should().Be(1);
        command.Hue.Should().Be(360);
        command.Saturation.Should().Be(0);
        command.ColorTemperature.Should().Be(9000);
    }

    private static KasaDeviceStatus CreateLightDevice(KasaLightStatus light) => new(
        DeviceIdConfigured: true,
        SourceId: "tplink-kasa:test-light",
        DisplayName: "Test Light",
        GroupName: null,
        IsFavorite: false,
        Host: "192.0.2.1",
        MacAddress: "AA:BB:CC:DD:EE:FF",
        HostConfigured: true,
        ObservedAtUtc: DateTimeOffset.UtcNow,
        DisplayTimeZoneId: null,
        IsOnline: true,
        IdentityValidated: true,
        IsDegraded: false,
        FailureReason: null,
        DegradedReason: null,
        Model: "KL130(US)",
        HardwareVersion: "1.0",
        SoftwareVersion: "1.0",
        DeviceKind: KasaDeviceKind.Bulb,
        Capabilities: new HashSet<KasaCapability> { KasaCapability.LightState },
        MetadataCapabilities: new HashSet<KasaMetadataCapability>(),
        CommandCapabilities: new HashSet<KasaCommandCapability> { KasaCommandCapability.LightColor },
        IsOn: light.IsOn,
        Outlets: [],
        Light: light,
        Energy: null,
        DeviceInfo: null,
        ReadMetadata: null);

    private static string ReadRepositoryFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate repository file '{relativePath}'.");
    }
}
