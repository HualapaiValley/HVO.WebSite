using FluentAssertions;
using HVO.Gateway.TplinkKasa.Devices;
using System.Text.Json;

namespace HVO.Gateway.TplinkKasa.Tests.Devices;

[TestClass]
public sealed class KasaSystemInfoParserTests
{
    private readonly KasaSystemInfoParser _parser = new();

    [TestMethod]
    [DataRow("ep25-sysinfo.json", "EP25(US)", 0, true, false)]
    [DataRow("hs300-sysinfo.json", "HS300(US)", 6, false, false)]
    [DataRow("kp200-sysinfo.json", "KP200(US)", 2, false, false)]
    [DataRow("hs200-sysinfo.json", "HS200(US)", 0, true, false)]
    [DataRow("hs210-sysinfo.json", "HS210(US)", 0, true, false)]
    [DataRow("hs220-sysinfo.json", "HS220(US)", 0, true, false)]
    [DataRow("kl130-sysinfo.json", "KL130(US)", 0, false, true)]
    [DataRow("lb230-sysinfo.json", "LB230(E26)", 0, false, true)]
    public void Parse_SanitizedFixture_ExtractsExpectedShape(string fixture, string model, int childCount, bool hasRelay, bool hasLight)
    {
        using var document = JsonDocument.Parse(FixtureLoader.Read(fixture));

        var info = _parser.Parse(document);

        info.Model.Should().Be(model);
        info.DeviceId.Should().EndWith("_DEVICE_ID_SANITIZED");
        info.Children.Should().HaveCount(childCount);
        (info.RelayState is not null).Should().Be(hasRelay);
        (info.LightState is not null).Should().Be(hasLight);
        info.MacAddress.Should().NotBeNullOrWhiteSpace();
    }
}
