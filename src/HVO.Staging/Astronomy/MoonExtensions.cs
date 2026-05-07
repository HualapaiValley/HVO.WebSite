namespace HVO.Astronomy;

/// <summary>
/// Provides additional lunar calculations not yet in the HVO.Astronomy NuGet package.
/// </summary>
public static class MoonExtensions
{
    /// <summary>
    /// Computes the fraction of the Moon's disk that is illuminated (0.0 – 1.0).
    /// </summary>
    /// <param name="utcDateTime">The UTC date and time.</param>
    /// <returns>
    /// Illuminated fraction: 0.0 = new moon, 0.5 = quarter moon, 1.0 = full moon.
    /// </returns>
    public static double CalculateIlluminationFraction(DateTime utcDateTime)
    {
        double dayNumber = AstronomyMath.J2000_UT(utcDateTime);

        SunCalculations.SunRightAscensionDeclination(dayNumber, out double sunRa, out double sunDec);
        MoonCalculations.CalculateMoonRightAscensionDeclination(dayNumber, out double moonRa, out double moonDec);

        double sunDecRad  = DegreesToRadians(sunDec);
        double moonDecRad = DegreesToRadians(moonDec);
        double dRaRad     = DegreesToRadians(moonRa - sunRa);

        // Angular separation (elongation) between Moon and Sun
        double cosElongation = Math.Sin(sunDecRad) * Math.Sin(moonDecRad)
                             + Math.Cos(sunDecRad) * Math.Cos(moonDecRad) * Math.Cos(dRaRad);

        double elongation = Math.Acos(Math.Clamp(cosElongation, -1.0, 1.0));

        // Illuminated fraction: 0 at new moon (elongation = 0), 1 at full moon (elongation = π)
        return (1.0 - Math.Cos(elongation)) / 2.0;
    }

    /// <summary>
    /// Computes the Moon's illumination percentage (0 – 100).
    /// </summary>
    /// <param name="utcDateTime">The UTC date and time.</param>
    public static double CalculateIlluminationPercent(DateTime utcDateTime) =>
        CalculateIlluminationFraction(utcDateTime) * 100.0;

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180.0;
}
