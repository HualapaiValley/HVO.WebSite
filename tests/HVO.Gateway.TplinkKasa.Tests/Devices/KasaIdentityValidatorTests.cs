using FluentAssertions;
using HVO.Gateway.TplinkKasa.Configuration;
using HVO.Gateway.TplinkKasa.Devices;
using System.Text.Json;

namespace HVO.Gateway.TplinkKasa.Tests.Devices;

[TestClass]
public sealed class KasaIdentityValidatorTests
{
    private readonly KasaIdentityValidator _validator = new();
    private readonly KasaSystemInfo _info;

    public KasaIdentityValidatorTests()
    {
        using var document = JsonDocument.Parse(FixtureLoader.Read("ep25-sysinfo.json"));
        _info = new KasaSystemInfoParser().Parse(document);
    }

    [TestMethod]
    public void Validate_MatchingDeviceIdAndMac_Succeeds()
    {
        var config = new KasaDeviceConfig
        {
            DeviceId = "EP25_DEVICE_ID_SANITIZED",
            Host = "192.0.2.10",
            MacAddress = "aa-bb-cc-dd-ee-01",
            ExpectedModel = "EP25(US)"
        };

        var result = _validator.Validate(config, _info);

        result.IsValid.Should().BeTrue();
        result.DeviceIdMatched.Should().BeTrue();
        result.MacMatched.Should().BeTrue();
        result.ModelMatched.Should().BeTrue();
    }

    [TestMethod]
    public void Validate_WrongDeviceId_FailsClosed()
    {
        var config = new KasaDeviceConfig { DeviceId = "OTHER_DEVICE", Host = "192.0.2.10" };

        var result = _validator.Validate(config, _info);

        result.IsValid.Should().BeFalse();
        result.Reason.Should().Contain("deviceId");
    }

    [TestMethod]
    public void Validate_WrongMac_FailsClosed()
    {
        var config = new KasaDeviceConfig
        {
            DeviceId = "EP25_DEVICE_ID_SANITIZED",
            Host = "192.0.2.10",
            MacAddress = "AA:BB:CC:DD:EE:99"
        };

        var result = _validator.Validate(config, _info);

        result.IsValid.Should().BeFalse();
        result.Reason.Should().Contain("MAC");
    }
}
