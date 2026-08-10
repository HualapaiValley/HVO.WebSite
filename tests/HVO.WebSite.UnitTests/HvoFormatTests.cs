using FluentAssertions;
using HVO.WebSite.Themes.Components.Format;
using System.Globalization;

namespace HVO.WebSite.UnitTests;

[TestClass]
public sealed class HvoFormatTests
{
    [TestMethod]
    public void DisplayTimeZone_ConvertsUtcAndExposesConfiguredLabel()
    {
        var displayTimeZone = new HvoDisplayTimeZone("America/Phoenix");

        var local = displayTimeZone.ConvertFromUtc(new DateTime(2026, 6, 17, 12, 0, 0, DateTimeKind.Utc));

        local.Should().Be(new DateTime(2026, 6, 17, 5, 0, 0));
        displayTimeZone.ConvertFromUtc(new DateTime(2026, 6, 17, 12, 0, 0, DateTimeKind.Unspecified))
            .Should().Be(new DateTime(2026, 6, 17, 5, 0, 0));
        displayTimeZone.Label.Should().Be("America/Phoenix");
        HvoDisplayTimeZone.IsValid("America/Phoenix").Should().BeTrue();
    }

    [TestMethod]
    public void DisplayTimeZone_RejectsLocalDateTime()
    {
        var displayTimeZone = new HvoDisplayTimeZone("America/Phoenix");

        var act = () => displayTimeZone.ConvertFromUtc(new DateTime(2026, 6, 17, 12, 0, 0, DateTimeKind.Local));

        act.Should().Throw<ArgumentException>().WithParameterName("utc");
    }

    [TestMethod]
    public void DisplayTimeZone_InvalidIdFallsBackToExplicitUtc()
    {
        var displayTimeZone = new HvoDisplayTimeZone("not-a-time-zone");

        displayTimeZone.TimeZone.Should().Be(TimeZoneInfo.Utc);
        displayTimeZone.Label.Should().Be("UTC");
        HvoDisplayTimeZone.IsValid("not-a-time-zone").Should().BeFalse();
    }

    [TestMethod]
    public void Timestamp_ValidUtc_ReturnsLocalFormatted()
    {
        var result = HvoFormat.Timestamp(new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc));
        result.Should().NotBe("--");
        result.Should().Contain("2026-01-15");
    }

    [TestMethod]
    public void Timestamp_Null_ReturnsDash()
    {
        HvoFormat.Timestamp(null).Should().Be("--");
    }

    [TestMethod]
    public void FooterTimestamp_ValidUtc_ReturnsCustomFormat()
    {
        var result = HvoFormat.FooterTimestamp(new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc));
        result.Should().NotBe("--");
        result.Should().Contain("2026");
    }

    [TestMethod]
    public void Temperature_Metric_ReturnsCelsius()
    {
        HvoFormat.Temperature(22.5).Should().Be("22.5 °C");
    }

    [TestMethod]
    public void Temperature_Imperial_ReturnsFahrenheit()
    {
        HvoFormat.Temperature(22.5, UnitSystem.Imperial).Should().Be("72.5 °F");
    }

    [TestMethod]
    public void Temperature_Null_ReturnsDash()
    {
        HvoFormat.Temperature(null).Should().Be("--");
    }

    [TestMethod]
    public void Speed_Metric_ReturnsKph()
    {
        HvoFormat.Speed(5.2).Should().Be("18.7 km/h");
    }

    [TestMethod]
    public void Speed_Imperial_ReturnsMph()
    {
        HvoFormat.Speed(5.2, UnitSystem.Imperial).Should().Be("11.6 mph");
    }

    [TestMethod]
    public void PressureInHg_Metric_ReturnsHpa()
    {
        HvoFormat.PressureInHg(29.92).Should().Be("1013.2 hPa");
    }

    [TestMethod]
    public void PressureInHg_Imperial_ReturnsInHg()
    {
        HvoFormat.PressureInHg(29.92, UnitSystem.Imperial).Should().Be("29.92 inHg");
    }

    [TestMethod]
    public void Voltage_DefaultPrecision()
    {
        HvoFormat.Voltage(13.45).Should().Be("13.45 V");
    }

    [TestMethod]
    public void Voltage_CustomPrecision()
    {
        HvoFormat.Voltage(13.45, 1).Should().Match(v => v.StartsWith("13.") && v.EndsWith(" V"));
    }

    [TestMethod]
    public void Voltage_Null_ReturnsDash()
    {
        HvoFormat.Voltage(null).Should().Be("--");
    }

    [TestMethod]
    public void Current_Valid_ReturnsAmps()
    {
        HvoFormat.Current(2.75).Should().Be("2.8 A");
    }

    [TestMethod]
    public void Power_Valid_ReturnsWatts()
    {
        HvoFormat.Power(350.8).Should().Be("351 W");
    }

    [TestMethod]
    public void SignedElectricalValues_IncludeExplicitDirectionSign()
    {
        HvoFormat.SignedCurrent(2.75).Should().Be("+2.8 A");
        HvoFormat.SignedCurrent(-2.75).Should().Be("-2.8 A");
        HvoFormat.SignedPower(350.8).Should().Be("+351 W");
        HvoFormat.SignedPower(-350.8).Should().Be("-351 W");
    }

    [TestMethod]
    public void Power_Null_ReturnsDash()
    {
        HvoFormat.Power(null).Should().Be("--");
    }

    [TestMethod]
    public void EnergyAh_Valid_ReturnsAh()
    {
        HvoFormat.EnergyAh(125.5).Should().Be("125.5 Ah");
    }

    [TestMethod]
    public void Rain_Valid_ReturnsInches()
    {
        HvoFormat.Rain(0.25).Should().Be("0.25 in");
    }

    [TestMethod]
    public void Percent_Valid_ReturnsPercent()
    {
        HvoFormat.Percent(78.3).Should().Be("78 %");
    }

    [TestMethod]
    public void Percent_Null_ReturnsDash()
    {
        HvoFormat.Percent(null).Should().Be("--");
    }

    [TestMethod]
    public void Duration_Valid_ReturnsFormatted()
    {
        HvoFormat.Duration(TimeSpan.FromHours(2.5)).Should().Be("02:30:00");
    }

    [TestMethod]
    public void Duration_Null_ReturnsDash()
    {
        HvoFormat.Duration(null).Should().Be("--");
    }

    [TestMethod]
    public void Integer_Valid_ReturnsString()
    {
        HvoFormat.Integer(42).Should().Be("42");
    }

    [TestMethod]
    public void Integer_Null_ReturnsDash()
    {
        HvoFormat.Integer(null).Should().Be("--");
    }

    [TestMethod]
    public void Integer_Negative_ReturnsNegative()
    {
        HvoFormat.Integer(-5).Should().Be("-5");
    }

    [TestMethod]
    public void NumericFormatting_IsStableUnderCommaDecimalCulture()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;

        try
        {
            var commaDecimalCulture = CultureInfo.GetCultureInfo("fr-FR");
            CultureInfo.CurrentCulture = commaDecimalCulture;
            CultureInfo.CurrentUICulture = commaDecimalCulture;

            HvoFormat.Temperature(22.5).Should().Be("22.5 °C");
            HvoFormat.Speed(5.2).Should().Be("18.7 km/h");
            HvoFormat.PressureInHg(29.92).Should().Be("1013.2 hPa");
            HvoFormat.Voltage(13.45).Should().Be("13.45 V");
            HvoFormat.Current(2.75).Should().Be("2.8 A");
            HvoFormat.Power(350.8).Should().Be("351 W");
            HvoFormat.EnergyAh(125.5).Should().Be("125.5 Ah");
            HvoFormat.Rain(0.25).Should().Be("0.25 in");
            HvoFormat.Percent(78.3).Should().Be("78 %");
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    [TestMethod]
    public void AllNullables_ReturnDash()
    {
        HvoFormat.Temperature(null).Should().Be("--");
        HvoFormat.Speed(null).Should().Be("--");
        HvoFormat.PressureInHg(null).Should().Be("--");
        HvoFormat.Voltage(null).Should().Be("--");
        HvoFormat.Current(null).Should().Be("--");
        HvoFormat.Power(null).Should().Be("--");
        HvoFormat.EnergyAh(null).Should().Be("--");
        HvoFormat.Rain(null).Should().Be("--");
        HvoFormat.Percent(null).Should().Be("--");
        HvoFormat.Duration(null).Should().Be("--");
        HvoFormat.Integer(null).Should().Be("--");
    }
}
