using FluentAssertions;
using HVO.Gateway.TplinkKasa.Devices;
using System.Text.Json;

namespace HVO.Gateway.TplinkKasa.Tests.Devices;

[TestClass]
public sealed class KasaCapabilityDetectorTests
{
    private readonly KasaSystemInfoParser _systemInfoParser = new();
    private readonly KasaCapabilityDetector _detector = new();

    [TestMethod]
    [DataRow("ep25-sysinfo.json", KasaDeviceKind.Plug, KasaCapability.SwitchState)]
    [DataRow("hs300-sysinfo.json", KasaDeviceKind.PowerStrip, KasaCapability.ChildOutlets)]
    [DataRow("kp200-sysinfo.json", KasaDeviceKind.DualOutlet, KasaCapability.ChildOutlets)]
    [DataRow("hs200-sysinfo.json", KasaDeviceKind.Switch, KasaCapability.SwitchState)]
    [DataRow("hs210-sysinfo.json", KasaDeviceKind.ThreeWaySwitch, KasaCapability.SwitchState)]
    [DataRow("hs220-sysinfo.json", KasaDeviceKind.Dimmer, KasaCapability.Dimming)]
    [DataRow("kl130-sysinfo.json", KasaDeviceKind.Bulb, KasaCapability.LightState)]
    [DataRow("lb230-sysinfo.json", KasaDeviceKind.Bulb, KasaCapability.LightState)]
    public void Detect_SanitizedFixture_InfersDeviceKindAndCapability(string fixture, KasaDeviceKind expectedKind, KasaCapability expectedCapability)
    {
        using var document = JsonDocument.Parse(FixtureLoader.Read(fixture));
        var info = _systemInfoParser.Parse(document);

        var profile = _detector.Detect(info);

        profile.DeviceKind.Should().Be(expectedKind);
        profile.Capabilities.Should().Contain(expectedCapability);
        profile.MetadataCapabilities.Should().Contain(KasaMetadataCapability.FirmwareInfo);
    }
}
