namespace HVO.Astronomy;

/// <summary>
/// Provides moonrise and moonset calculations using an iterative hourly-sampling method.
/// </summary>
/// <remarks>
/// Algorithm: hourly altitude sampling with linear interpolation to find horizon crossings.
/// Moon standard altitude at rise/set ≈ +0.125° (accounts for mean parallax 57′,
/// semidiameter 16′, and refraction 34′: 57 − 16 − 34 = +7′ ≈ +0.125°).
/// </remarks>
public static class MoonRiseSet
{
    /// <summary>Standard altitude of Moon's centre at rise/set (degrees above mathematical horizon).</summary>
    private const double StandardAltitudeDegrees = 0.125;

    /// <summary>
    /// Computes UTC moonrise and moonset for the given date and observer location.
    /// </summary>
    /// <param name="date">The calendar date (time of day is ignored).</param>
    /// <param name="siteLatitude">Observer latitude in degrees (positive north).</param>
    /// <param name="siteLongitude">Observer longitude in degrees (positive east).</param>
    /// <param name="moonrise">UTC time of moonrise, or <see cref="DateTimeOffset.MinValue"/> when no rise occurs within the sampled UTC day.</param>
    /// <param name="moonset">UTC time of moonset, or <see cref="DateTimeOffset.MinValue"/> when no set occurs within the sampled UTC day.</param>
    /// <returns>
    /// <c>0</c> — normal rise and set occurred;
    /// <c>-1</c> — Moon stays below the horizon all day;
    /// <c>+1</c> — Moon stays above the horizon all day.
    /// </returns>
    public static int MoonriseMoonset(
        DateTime date,
        double siteLatitude,
        double siteLongitude,
        out DateTimeOffset moonrise,
        out DateTimeOffset moonset)
    {
        var midnight = new DateTimeOffset(date.Year, date.Month, date.Day, 0, 0, 0, TimeSpan.Zero);

        // Sample Moon altitude at each whole hour 0h–24h UTC.
        // 25 samples give 24 intervals; the Moon moves ~13°/day so linear
        // interpolation within any 1-hour window is accurate to ~1–2 minutes.
        var altitudes = new double[25];
        for (int h = 0; h <= 24; h++)
        {
            double dayNum = AstronomyMath.J2000_UT(midnight.UtcDateTime.AddHours(h));
            MoonCalculations.CalculateMoonAltitudeAzimuth(
                dayNum, siteLatitude, siteLongitude,
                out altitudes[h], out _);
        }

        double? riseHour = null;
        double? setHour = null;

        for (int h = 0; h < 24; h++)
        {
            double a0 = altitudes[h] - StandardAltitudeDegrees;
            double a1 = altitudes[h + 1] - StandardAltitudeDegrees;

            if (a0 < 0 && a1 >= 0)
            {
                // Rising crossing — interpolate to sub-hour precision
                riseHour = h + a0 / (a0 - a1);
            }
            else if (a0 >= 0 && a1 < 0)
            {
                // Setting crossing
                setHour = h + a0 / (a0 - a1);
            }
        }

        if (riseHour is null && setHour is null)
        {
            // No crossings — Moon is either always above or always below.
            moonrise = DateTimeOffset.MinValue;
            moonset = DateTimeOffset.MinValue;
            return altitudes[12] >= StandardAltitudeDegrees ? 1 : -1;
        }

        moonrise = riseHour.HasValue
            ? midnight.Add(TimeSpan.FromHours(riseHour.Value))
            : DateTimeOffset.MinValue; // Moon was already up at midnight; no rise this calendar day

        moonset = setHour.HasValue
            ? midnight.Add(TimeSpan.FromHours(setHour.Value))
            : DateTimeOffset.MinValue; // No set this calendar day

        return 0;
    }
}
