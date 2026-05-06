namespace HVO.Astronomy;

/// <summary>
/// Provides additional solar calculations not yet in the HVO.Astronomy NuGet package.
/// </summary>
/// <remarks>
/// Uses the same Paul Schlyter algorithm as <c>SunCalculations</c> in the NuGet package.
/// Amateur astronomical twilight begins/ends when the Sun is 15° below the horizon —
/// midway between nautical (-12°) and full astronomical (-18°) twilight.
/// This is the threshold commonly used by visual and imaging astronomers.
/// </remarks>
public static class SunExtensions
{
    private const double AmateurTwilightAngleDegrees = -15.0;

    /// <summary>
    /// Computes the UTC start and end of amateur astronomical twilight (Sun at -15°).
    /// </summary>
    /// <param name="date">The calendar date (time of day is ignored).</param>
    /// <param name="siteLatitude">Observer latitude in degrees (positive north).</param>
    /// <param name="siteLongitude">Observer longitude in degrees (positive east).</param>
    /// <param name="twilightStart">UTC time when the Sun descends to -15° (evening).</param>
    /// <param name="twilightEnd">UTC time when the Sun rises to -15° (morning).</param>
    /// <returns>
    /// <c>0</c> — normal twilight start/end;
    /// <c>-1</c> — Sun stays below -15° all day (no twilight boundary);
    /// <c>+1</c> — Sun never reaches -15° (perpetual daylight at this latitude/date).
    /// </returns>
    public static int AmateurAstronomicalTwilight(
        DateTime date,
        double siteLatitude,
        double siteLongitude,
        out DateTimeOffset twilightStart,
        out DateTimeOffset twilightEnd)
    {
        return TwilightAtAngle(
            date, siteLatitude, siteLongitude,
            AmateurTwilightAngleDegrees,
            out twilightStart, out twilightEnd);
    }

    // ────────────────────────────────────────────────────────────────
    //  Private helpers
    // ────────────────────────────────────────────────────────────────

    private static int TwilightAtAngle(
        DateTime date,
        double siteLatitude,
        double siteLongitude,
        double horizonAltitudeDegrees,
        out DateTimeOffset start,
        out DateTimeOffset end)
    {
        // Reference epoch: local noon expressed as J2000_UT day number.
        // Subtracting longitude/360 converts UT noon to local solar noon.
        double dayNumber = AstronomyMath.J2000_UT(
            new DateTime(date.Year, date.Month, date.Day, 12, 0, 0))
            - (siteLongitude / 360.0);

        // GMST0 = Greenwich Mean Sidereal Time at 0h UT (Schlyter formula).
        // AstronomyMath.GMST0 is internal, so we inline the identical expression.
        double gmst0 = AstronomyMath.NormalizeDegrees(
            (180.0 + 356.0470 + 282.9404) + (0.9856002585 + 4.70935E-5) * dayNumber);

        double localSiderealTime = AstronomyMath.NormalizeDegrees(
            gmst0 + 180.0 + siteLongitude);

        SunCalculations.SunRightAscensionDeclination(dayNumber, out double sunRa, out double sunDec);

        double sunInSouthUT = 12.0
            - AstronomyMath.NormalizeDegrees180(localSiderealTime - sunRa) / 15.0;

        double cosArc =
            (Math.Sin(DegreesToRadians(horizonAltitudeDegrees))
                - Math.Sin(DegreesToRadians(siteLatitude))
                    * Math.Sin(DegreesToRadians(sunDec)))
            / (Math.Cos(DegreesToRadians(siteLatitude))
                * Math.Cos(DegreesToRadians(sunDec)));

        var midnight = new DateTimeOffset(date.Year, date.Month, date.Day, 0, 0, 0, TimeSpan.Zero);

        if (cosArc >= 1.0)
        {
            // Sun always below the twilight threshold — permanent darkness at this angle
            start = midnight.Add(TimeSpan.FromHours(sunInSouthUT));
            end   = midnight.Add(TimeSpan.FromHours(sunInSouthUT));
            return -1;
        }

        if (cosArc <= -1.0)
        {
            // Sun never reaches this threshold — perpetual daylight
            start = midnight.Add(TimeSpan.FromHours(sunInSouthUT - 12));
            end   = midnight.Add(TimeSpan.FromHours(sunInSouthUT + 12));
            return 1;
        }

        double offsetHours = RadiansToDegrees(Math.Acos(cosArc)) / 15.0;
        start = midnight.Add(TimeSpan.FromHours(sunInSouthUT - offsetHours));
        end   = midnight.Add(TimeSpan.FromHours(sunInSouthUT + offsetHours));
        return 0;
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180.0;
    private static double RadiansToDegrees(double radians) => radians * 180.0 / Math.PI;
}
