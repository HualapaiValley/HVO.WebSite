using FluentAssertions;
using HVO.Hardware.DavisVantagePro2.Protocol;

namespace HVO.Hardware.DavisVantagePro2.Tests.Protocol;

[TestClass]
public class DavisForecastTableTests
{
    // ── GetForecastString ─────────────────────────────────────────────────────

    [TestMethod]
    public void GetForecastString_Rule0_ReturnsNonEmpty()
    {
        DavisForecastTable.GetForecastString(0).Should().NotBeNullOrWhiteSpace();
    }

    [TestMethod]
    public void GetForecastString_Rule195_ReturnsNonEmpty()
    {
        DavisForecastTable.GetForecastString(195).Should().NotBeNullOrWhiteSpace();
    }

    [TestMethod]
    public void GetForecastString_OutOfRange_ReturnsUnknown()
    {
        DavisForecastTable.GetForecastString(196).Should().Contain("196");
        DavisForecastTable.GetForecastString(-1).Should().Contain("-1");
    }

    // ── GetIconNames ──────────────────────────────────────────────────────────

    [TestMethod]
    public void GetIconNames_ZeroByte_ReturnsEmpty()
    {
        DavisForecastTable.GetIconNames(0).Should().BeEmpty();
    }

    [TestMethod]
    public void GetIconNames_RainBit_ContainsRain()
    {
        // bit 0 = Rain
        DavisForecastTable.GetIconNames(0x01).Should().ContainSingle().Which.Should().Be("Rain");
    }

    [TestMethod]
    public void GetIconNames_SunnyBit_ContainsSunny()
    {
        // bit 3 = Sunny
        DavisForecastTable.GetIconNames(0x08).Should().ContainSingle().Which.Should().Be("Sunny");
    }

    [TestMethod]
    public void GetIconNames_AllFiveBits_ReturnsFiveNames()
    {
        // bits 0-4 set
        DavisForecastTable.GetIconNames(0x1F).Should().HaveCount(5);
    }

    [TestMethod]
    public void GetIconNames_RainAndSunny_ContainsBoth()
    {
        // bit 0 = Rain, bit 3 = Sunny
        var names = DavisForecastTable.GetIconNames(0x09);
        names.Should().Contain("Rain").And.Contain("Sunny");
    }
}
