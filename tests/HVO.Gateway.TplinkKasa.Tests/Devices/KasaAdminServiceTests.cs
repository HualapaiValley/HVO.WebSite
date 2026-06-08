using FluentAssertions;
using HVO.Gateway.TplinkKasa.Configuration;
using HVO.Gateway.TplinkKasa.Devices;
using HVO.Gateway.TplinkKasa.Hosting;
using Microsoft.Extensions.Options;
using System.Reflection;
using System.Text.Json;

namespace HVO.Gateway.TplinkKasa.Tests.Devices;

[TestClass]
public sealed class KasaAdminServiceTests
{
    [TestMethod]
    public void BuildDeviceConfig_UsesRealDeviceIdForIdentityValidationAndStablePublicSourceId()
    {
        using var document = JsonDocument.Parse(FixtureLoader.Read("kp200-sysinfo.json"));
        var sysinfo = new KasaSystemInfoParser().Parse(document);
        var profile = new KasaCapabilityDetector().Detect(sysinfo);
        var result = KasaProbeResult.Success(
            "192.0.2.25",
            9999,
            sysinfo,
            [],
            null,
            profile,
            new Dictionary<string, KasaReadOnlyModuleResult>());
        var service = new KasaAdminService(
            Options.Create(new KasaGatewayOptions { PollIntervalSeconds = 5 }),
            null!,
            null!,
            null!,
            null!,
            null!,
            null!);
        var method = typeof(KasaAdminService).GetMethod("BuildDeviceConfig", BindingFlags.Instance | BindingFlags.NonPublic);

        var config = (KasaDeviceConfig)method!.Invoke(service, [result, "observatory"])!;

        config.DeviceId.Should().Be("KP200_DEVICE_ID_SANITIZED");
        config.SourceId.Should().Be("tplink-kasa:kasa-aabbccddee03");
        config.ExpectedChildCount.Should().Be(2);
        config.DeviceKind.Should().Be(KasaDeviceKind.DualOutlet);
        new KasaIdentityValidator().Validate(config, sysinfo).IsValid.Should().BeTrue();
    }
}