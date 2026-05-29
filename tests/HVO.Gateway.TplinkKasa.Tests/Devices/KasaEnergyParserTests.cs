using FluentAssertions;
using HVO.Gateway.TplinkKasa.Devices;
using System.Text.Json;

namespace HVO.Gateway.TplinkKasa.Tests.Devices;

[TestClass]
public sealed class KasaEnergyParserTests
{
    [TestMethod]
    public void Parse_MilliUnitEnergy_ConvertsToDisplayUnits()
    {
        using var document = JsonDocument.Parse(FixtureLoader.Read("ep25-emeter.json"));

        var reading = new KasaEnergyParser().Parse(document);

        reading.Should().NotBeNull();
        reading!.CurrentA.Should().Be(0.125);
        reading.PowerW.Should().Be(4.2);
        reading.VoltageV.Should().Be(120.456);
        reading.EnergyKWh.Should().Be(12.345);
    }

    [TestMethod]
    public void Parse_UnsupportedEmeter_ReturnsNull()
    {
        using var document = JsonDocument.Parse(FixtureLoader.Read("emeter-unsupported.json"));

        new KasaEnergyParser().Parse(document).Should().BeNull();
    }
}
