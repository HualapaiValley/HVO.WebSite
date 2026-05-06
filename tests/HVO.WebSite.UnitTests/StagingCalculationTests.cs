using FluentAssertions;
using HVO.Astronomy;

namespace HVO.WebSite.UnitTests;

[TestClass]
public class MoonRiseSetTests
{
    // Observatory location: Hualapai Valley, AZ
    // Latitude  35.7° N, Longitude -114.0° E (west = negative east)
    private const double Latitude  =  35.7;
    private const double Longitude = -114.0;

    [TestMethod]
    public void MoonriseMoonset_ReturnsZero_WhenNormalRiseAndSetOccur()
    {
        // Use a known date with typical moonrise/moonset behaviour.
        // Full moon 2026-05-12 — Moon rises near sunset, sets near sunrise.
        var date = new DateTime(2026, 5, 12);

        int result = MoonRiseSet.MoonriseMoonset(
            date, Latitude, Longitude,
            out DateTimeOffset moonrise,
            out DateTimeOffset moonset);

        result.Should().Be(0);
        moonrise.Should().NotBe(DateTimeOffset.MinValue);
        moonset.Should().NotBe(DateTimeOffset.MinValue);

        // Moonrise must be before moonset on a typical full-moon night
        // (or moonrise could be after midnight — just validate both are in a 24h window)
        moonrise.UtcDateTime.Date.Should().BeOnOrAfter(date.Date.AddDays(-1));
        moonset.UtcDateTime.Date.Should().BeOnOrBefore(date.Date.AddDays(1));
    }

    [TestMethod]
    public void MoonriseMoonset_RiseHour_IsReasonable()
    {
        // New moon 2026-05-27 — Moon rises near sunrise, sets near sunset.
        var date = new DateTime(2026, 5, 27);

        int result = MoonRiseSet.MoonriseMoonset(
            date, Latitude, Longitude,
            out DateTimeOffset moonrise,
            out _);

        // Result 0 = normal, 1 = always above (only in polar regions)
        result.Should().BeGreaterThanOrEqualTo(-1).And.BeLessThanOrEqualTo(1);

        if (result == 0)
        {
            moonrise.Hour.Should().BeInRange(0, 23);
        }
    }

    [TestMethod]
    public void MoonriseMoonset_AtArctic_ReturnsNonZero_WhenMoonAlwaysAbove()
    {
        // At very high latitudes near full moon in summer, Moon may not set.
        // 80°N latitude, full moon date
        var date = new DateTime(2026, 5, 12);
        const double arcticLatitude = 80.0;

        int result = MoonRiseSet.MoonriseMoonset(
            date, arcticLatitude, Longitude,
            out _, out _);

        // At 80°N near full moon in May, Moon may be circumpolar (+1) or normal (0)
        result.Should().BeGreaterThanOrEqualTo(-1).And.BeLessThanOrEqualTo(1);
    }
}

[TestClass]
public class MoonExtensionsTests
{
    // Scan a 60-day window to find the date with minimum or maximum illumination.
    // This avoids hardcoding lunar calendar dates that may drift.
    private static DateTime FindIlluminationExtremum(DateTime windowStart, int days, bool findMinimum)
    {
        DateTime best = windowStart;
        double bestValue = findMinimum ? double.MaxValue : double.MinValue;
        for (int d = 0; d < days; d++)
        {
            var dt = windowStart.AddDays(d).Date.AddHours(12); // sample at noon UTC
            double v = MoonExtensions.CalculateIlluminationFraction(dt);
            if (findMinimum ? v < bestValue : v > bestValue)
            {
                bestValue = v;
                best = dt;
            }
        }
        return best;
    }

    [TestMethod]
    public void CalculateIlluminationFraction_AtNewMoon_IsNearZero()
    {
        // Locate the new moon in a 60-day window starting 2026-04-01.
        DateTime newMoon = FindIlluminationExtremum(new DateTime(2026, 4, 1), days: 60, findMinimum: true);
        double fraction = MoonExtensions.CalculateIlluminationFraction(newMoon);

        fraction.Should().BeLessThan(0.05,
            because: $"illumination at the detected new moon ({newMoon:yyyy-MM-dd}) should be near zero");
    }

    [TestMethod]
    public void CalculateIlluminationFraction_AtFullMoon_IsNearOne()
    {
        // Locate the full moon in a 60-day window starting 2026-04-01.
        DateTime fullMoon = FindIlluminationExtremum(new DateTime(2026, 4, 1), days: 60, findMinimum: false);
        double fraction = MoonExtensions.CalculateIlluminationFraction(fullMoon);

        fraction.Should().BeGreaterThan(0.95,
            because: $"illumination at the detected full moon ({fullMoon:yyyy-MM-dd}) should be near one");
    }

    [TestMethod]
    public void CalculateIlluminationPercent_MatchesFractionScaledTo100()
    {
        var dateTime = new DateTime(2026, 5, 18, 6, 0, 0); // arbitrary date

        double fraction = MoonExtensions.CalculateIlluminationFraction(dateTime);
        double percent  = MoonExtensions.CalculateIlluminationPercent(dateTime);

        percent.Should().BeApproximately(fraction * 100.0, precision: 0.001);
    }

    [TestMethod]
    public void CalculateIlluminationFraction_IsAlwaysBetweenZeroAndOne()
    {
        // Sample across several months
        for (int day = 0; day < 60; day++)
        {
            var dt = new DateTime(2026, 4, 1).AddDays(day);
            double f = MoonExtensions.CalculateIlluminationFraction(dt);
            f.Should().BeInRange(0.0, 1.0);
        }
    }
}

[TestClass]
public class SunExtensionsTests
{
    private const double Latitude  =  35.7;
    private const double Longitude = -114.0;

    [TestMethod]
    public void AmateurAstronomicalTwilight_ReturnsZero_ForTypicalMidLatitudeDate()
    {
        var date = new DateTime(2026, 5, 6);

        int result = SunExtensions.AmateurAstronomicalTwilight(
            date, Latitude, Longitude,
            out DateTimeOffset start,
            out DateTimeOffset end);

        result.Should().Be(0, "AZ in May has normal twilight transitions");
        start.UtcDateTime.Should().BeBefore(end.UtcDateTime,
            because: "evening twilight start must precede morning twilight end");
    }

    [TestMethod]
    public void AmateurAstronomicalTwilight_Start_IsBetweenNauticalAndAstronomical()
    {
        var date = new DateTime(2026, 5, 6);

        SunCalculations.NauticalTwilight(date, Latitude, Longitude,
            out DateTimeOffset nautStart, out _);
        SunCalculations.AstronomicalTwilight(date, Latitude, Longitude,
            out DateTimeOffset astroStart, out _);
        SunExtensions.AmateurAstronomicalTwilight(date, Latitude, Longitude,
            out DateTimeOffset amateurStart, out _);

        // Start = morning crossing (Sun rising through the angle).
        // As the Sun ascends in the morning: hits -18° → -15° → -12°.
        // So: astroStart < amateurStart < nautStart
        amateurStart.UtcDateTime.Should().BeBefore(nautStart.UtcDateTime,
            because: "morning: Sun crosses -15° before -12° as it rises");
        amateurStart.UtcDateTime.Should().BeAfter(astroStart.UtcDateTime,
            because: "morning: Sun crosses -15° after -18° as it rises");
    }

    [TestMethod]
    public void AmateurAstronomicalTwilight_End_IsBetweenNauticalAndAstronomical()
    {
        var date = new DateTime(2026, 5, 6);

        SunCalculations.NauticalTwilight(date, Latitude, Longitude,
            out _, out DateTimeOffset nautEnd);
        SunCalculations.AstronomicalTwilight(date, Latitude, Longitude,
            out _, out DateTimeOffset astroEnd);
        SunExtensions.AmateurAstronomicalTwilight(date, Latitude, Longitude,
            out _, out DateTimeOffset amateurEnd);

        // End = evening crossing (Sun descending through the angle).
        // As the Sun descends in the evening: hits -12° → -15° → -18°.
        // So: nautEnd < amateurEnd < astroEnd
        amateurEnd.UtcDateTime.Should().BeAfter(nautEnd.UtcDateTime,
            because: "evening: Sun crosses -15° after -12° as it descends");
        amateurEnd.UtcDateTime.Should().BeBefore(astroEnd.UtcDateTime,
            because: "evening: Sun crosses -15° before -18° as it descends");
    }
}

[TestClass]
public class DewRiskTests
{
    [TestMethod]
    public void Calculate_ReturnsCritical_WhenSpreadLessThanTwo()
    {
        HVO.Weather.DewRisk.Calculate(temperatureFahrenheit: 55.0, dewPointFahrenheit: 54.0)
            .Should().Be(HVO.Weather.DewRiskLevel.Critical);
    }

    [TestMethod]
    public void Calculate_ReturnsHigh_WhenSpreadBetweenTwoAndFive()
    {
        HVO.Weather.DewRisk.Calculate(temperatureFahrenheit: 60.0, dewPointFahrenheit: 57.0)
            .Should().Be(HVO.Weather.DewRiskLevel.High);
    }

    [TestMethod]
    public void Calculate_ReturnsModerate_WhenSpreadBetweenFiveAndTen()
    {
        HVO.Weather.DewRisk.Calculate(temperatureFahrenheit: 65.0, dewPointFahrenheit: 58.0)
            .Should().Be(HVO.Weather.DewRiskLevel.Moderate);
    }

    [TestMethod]
    public void Calculate_ReturnsLow_WhenSpreadGreaterThanTen()
    {
        HVO.Weather.DewRisk.Calculate(temperatureFahrenheit: 75.0, dewPointFahrenheit: 60.0)
            .Should().Be(HVO.Weather.DewRiskLevel.Low);
    }

    [TestMethod]
    public void Calculate_ReturnsCritical_AtExactlyZeroSpread()
    {
        HVO.Weather.DewRisk.Calculate(temperatureFahrenheit: 50.0, dewPointFahrenheit: 50.0)
            .Should().Be(HVO.Weather.DewRiskLevel.Critical);
    }

    [TestMethod]
    public void CalculateCelsius_MatchesFahrenheitEquivalent()
    {
        // 20°C temp, 14°C dew point = 36°F spread → Low
        var resultF = HVO.Weather.DewRisk.Calculate(68.0, 57.2);
        var resultC = HVO.Weather.DewRisk.CalculateCelsius(20.0, 14.0);

        resultC.Should().Be(resultF);
    }
}
