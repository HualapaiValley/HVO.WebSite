using FluentAssertions;
using HVO.Hardware.DavisVantagePro2.Protocol;

namespace HVO.Hardware.DavisVantagePro2.Tests.Protocol;

[TestClass]
public class DavisTimeZoneTableTests
{
    // ── GetName ───────────────────────────────────────────────────────────────

    [TestMethod]
    public void GetName_Code0_ReturnsDateline()
    {
        DavisTimeZoneTable.GetName(0).Should().Be("Dateline");
    }

    [TestMethod]
    public void GetName_Code4_ReturnsPacific()
    {
        DavisTimeZoneTable.GetName(4).Should().Be("Pacific");
    }

    [TestMethod]
    public void GetName_Code5_ReturnsMountain()
    {
        DavisTimeZoneTable.GetName(5).Should().Be("Mountain");
    }

    [TestMethod]
    public void GetName_Code6_ReturnsCentral()
    {
        DavisTimeZoneTable.GetName(6).Should().Be("Central");
    }

    [TestMethod]
    public void GetName_Code7_ReturnsEastern()
    {
        DavisTimeZoneTable.GetName(7).Should().Be("Eastern");
    }

    [TestMethod]
    public void GetName_Code13_ReturnsGmt()
    {
        DavisTimeZoneTable.GetName(13).Should().Be("GMT");
    }

    [TestMethod]
    public void GetName_Code31_ReturnsTonga()
    {
        DavisTimeZoneTable.GetName(31).Should().Be("Tonga");
    }

    [TestMethod]
    public void GetName_InvalidCodeNegative_ReturnsNull()
    {
        DavisTimeZoneTable.GetName(-1).Should().BeNull();
    }

    [TestMethod]
    public void GetName_InvalidCodeTooHigh_ReturnsNull()
    {
        DavisTimeZoneTable.GetName(DavisTimeZoneTable.Count).Should().BeNull();
    }

    // ── GetOffset ─────────────────────────────────────────────────────────────

    [TestMethod]
    public void GetOffset_Code0_ReturnsMinus12Hours()
    {
        DavisTimeZoneTable.GetOffset(0).Should().Be(TimeSpan.FromHours(-12));
    }

    [TestMethod]
    public void GetOffset_Code4_ReturnsMinus8Hours()
    {
        DavisTimeZoneTable.GetOffset(4).Should().Be(TimeSpan.FromHours(-8));
    }

    [TestMethod]
    public void GetOffset_Code5_ReturnsMinus7Hours()
    {
        DavisTimeZoneTable.GetOffset(5).Should().Be(TimeSpan.FromHours(-7));
    }

    [TestMethod]
    public void GetOffset_Code13_ReturnsZero()
    {
        DavisTimeZoneTable.GetOffset(13).Should().Be(TimeSpan.Zero);
    }

    [TestMethod]
    public void GetOffset_Code21_Returns5Hours30Minutes()
    {
        DavisTimeZoneTable.GetOffset(21).Should().Be(TimeSpan.FromMinutes(330)); // India UTC+5:30
    }

    [TestMethod]
    public void GetOffset_Code31_Returns13Hours()
    {
        DavisTimeZoneTable.GetOffset(31).Should().Be(TimeSpan.FromHours(13));
    }

    [TestMethod]
    public void GetOffset_InvalidCode_ReturnsNull()
    {
        DavisTimeZoneTable.GetOffset(-1).Should().BeNull();
        DavisTimeZoneTable.GetOffset(DavisTimeZoneTable.Count).Should().BeNull();
    }

    // ── GetLabel ──────────────────────────────────────────────────────────────

    [TestMethod]
    public void GetLabel_Code5_ContainsMountainAndUtcMinus7()
    {
        string label = DavisTimeZoneTable.GetLabel(5);
        label.Should().Contain("Mountain");
        label.Should().Contain("UTC-7");
    }

    [TestMethod]
    public void GetLabel_Code7_ContainsEasternAndUtcMinus5()
    {
        string label = DavisTimeZoneTable.GetLabel(7);
        label.Should().Contain("Eastern");
        label.Should().Contain("UTC-5");
    }

    [TestMethod]
    public void GetLabel_Code9_ContainsNewfoundlandAndHalfHourOffset()
    {
        string label = DavisTimeZoneTable.GetLabel(9); // Newfoundland UTC-3:30
        label.Should().Contain("Newfoundland");
        label.Should().Contain("UTC-3:30");
    }

    [TestMethod]
    public void GetLabel_Code13_ContainsGmtAndUtc0()
    {
        string label = DavisTimeZoneTable.GetLabel(13);
        label.Should().Contain("GMT");
        label.Should().Contain("UTC");
        // No minus/plus sign for UTC+0
    }

    [TestMethod]
    public void GetLabel_UnknownCode_ReturnsCodeFallback()
    {
        DavisTimeZoneTable.GetLabel(99).Should().Be("Code 99");
    }

    // ── UTC conversion roundtrip (core use case) ──────────────────────────────

    [TestMethod]
    public void UtcConversion_MountainTime_Code5_ConvertsCorrectly()
    {
        // Console reports 10:00 AM Mountain Standard Time (UTC-7).
        // Kind must be Unspecified — Local is rejected when host timezone != console timezone.
        var consoleLocal = DateTime.SpecifyKind(
            new DateTime(2026, 3, 1, 10, 0, 0), DateTimeKind.Unspecified);
        var offset = DavisTimeZoneTable.GetOffset(5)!.Value;  // UTC-7

        var utc = new DateTimeOffset(consoleLocal, offset).UtcDateTime;

        utc.Should().Be(new DateTime(2026, 3, 1, 17, 0, 0, DateTimeKind.Utc));
    }
}
