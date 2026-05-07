using System.Globalization;

namespace HVO.Astronomy;

public static class CelestialArcCalculations
{
    private const double ArcLeft = 48d;
    private const double ArcRight = 184d;
    private const double ArcBaseline = 116d;
    private const double ArcHeight = 54d;

    public static CelestialMarker BuildSunMarker(DateTimeOffset observedLocal, string? sunriseDisplay, string? sunsetDisplay)
    {
        if (!TryParseDisplayTime(sunriseDisplay, out var sunrise) ||
            !TryParseDisplayTime(sunsetDisplay, out var sunset))
        {
            return CelestialMarker.Hidden;
        }

        var start = observedLocal.Date + sunrise.ToTimeSpan();
        var end = observedLocal.Date + sunset.ToTimeSpan();
        return BuildMarker(observedLocal.DateTime, start, end, 28d);
    }

    public static MoonSnapshot BuildMoonSnapshot(DateTimeOffset observedLocal, TimeSpan consoleOffset, double? latitude, double? longitude)
    {
        if (latitude is null || longitude is null)
        {
            return MoonSnapshot.Empty;
        }

        int result = MoonRiseSet.MoonriseMoonset(observedLocal.Date, latitude.Value, longitude.Value, out var moonriseUtc, out var moonsetUtc);
        DateTimeOffset? moonriseLocal = result == -1 ? null : moonriseUtc.ToOffset(consoleOffset);
        DateTimeOffset? moonsetLocal = result == -1 ? null : moonsetUtc.ToOffset(consoleOffset);

        CelestialMarker marker = result switch
        {
            -1 => CelestialMarker.Hidden,
            1 => BuildMarkerFromFraction(observedLocal.TimeOfDay.TotalHours / 24d, 16d, true),
            _ when moonriseLocal.HasValue && moonsetLocal.HasValue => BuildMarker(observedLocal.DateTime, moonriseLocal.Value.DateTime, moonsetLocal.Value.DateTime, 16d),
            _ => CelestialMarker.Hidden
        };

        double illumination = MoonExtensions.CalculateIlluminationPercent(observedLocal.UtcDateTime);
    bool waxing = IsWaxing(observedLocal.UtcDateTime, illumination);
        string phaseName = DescribeMoonPhase(observedLocal.UtcDateTime, illumination);

        return new MoonSnapshot(
            marker,
            phaseName,
            illumination,
            waxing,
            $"{illumination:F0}%",
            FormatEventTime(moonriseLocal),
            FormatEventTime(moonsetLocal));
    }

    private static CelestialMarker BuildMarker(DateTime moment, DateTime start, DateTime end, double glowRadius)
    {
        if (end <= start)
        {
            end = end.AddDays(1);
        }

        var adjustedMoment = moment;
        if (adjustedMoment < start && adjustedMoment.AddDays(1) <= end)
        {
            adjustedMoment = adjustedMoment.AddDays(1);
        }
        else if (adjustedMoment > end && adjustedMoment.AddDays(-1) >= start)
        {
            adjustedMoment = adjustedMoment.AddDays(-1);
        }

        if (adjustedMoment < start || adjustedMoment > end)
        {
            return CelestialMarker.Hidden;
        }

        double durationSeconds = Math.Max(1d, (end - start).TotalSeconds);
        double progress = Math.Clamp((adjustedMoment - start).TotalSeconds / durationSeconds, 0d, 1d);
        return BuildMarkerFromFraction(progress, glowRadius, true);
    }

    private static CelestialMarker BuildMarkerFromFraction(double progress, double glowRadius, bool isVisible)
    {
        progress = Math.Clamp(progress, 0d, 1d);
        double x = ArcLeft + ((ArcRight - ArcLeft) * progress);
        double y = ArcBaseline - (Math.Sin(progress * Math.PI) * ArcHeight);
        return new CelestialMarker(x, y, glowRadius, isVisible);
    }

    private static bool TryParseDisplayTime(string? value, out TimeOnly time) =>
        TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out time);

    private static string FormatEventTime(DateTimeOffset? value) => value?.ToString("h:mm tt", CultureInfo.InvariantCulture) ?? "--";

    private static string DescribeMoonPhase(DateTime utcDateTime, double illuminationPercent)
    {
        bool waxing = IsWaxing(utcDateTime, illuminationPercent);

        return illuminationPercent switch
        {
            < 2d => "New moon",
            > 98d => "Full moon",
            >= 47d and <= 53d => waxing ? "First quarter" : "Last quarter",
            < 50d => waxing ? "Waxing crescent" : "Waning crescent",
            _ => waxing ? "Waxing gibbous" : "Waning gibbous"
        };
    }

    private static bool IsWaxing(DateTime utcDateTime, double illuminationPercent) =>
        MoonExtensions.CalculateIlluminationPercent(utcDateTime.AddHours(12)) >= illuminationPercent;
}

public readonly record struct CelestialMarker(double X, double Y, double GlowRadius, bool IsVisible)
{
    public static CelestialMarker Hidden => new(48d, 116d, 0d, false);
}

public sealed record MoonSnapshot(
    CelestialMarker Marker,
    string PhaseName,
    double IlluminationPercent,
    bool IsWaxing,
    string IlluminationText,
    string MoonriseText,
    string MoonsetText)
{
    public static MoonSnapshot Empty { get; } = new(CelestialMarker.Hidden, "Pending local astronomy", 0d, true, "--", "--", "--");
}