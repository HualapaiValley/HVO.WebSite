using HVO.WebSite.Themes.Components.Charts;
using System.Globalization;

namespace HVO.Hardware.DavisVantagePro2.Components.Pages;

public static class DavisAstronomicalChartBuilder
{
    private const string SunColor = "#ffcf66"; // --hvo-series-3 derived accent
    private const string SunFillColor = "rgba(255,207,102,0.20)"; // --hvo-series-3 derived fill
    private const string MoonColor = "#9fb8d4"; // --shell-page-muted-text derived accent
    private const string MoonNowColor = "#c8d8ee"; // --shell-page-text derived accent

    public static DavisAstronomicalChartModel Build(
        DateTime? observationWindowEndLocal,
        string? sunriseDisplay,
        string? sunsetDisplay,
        string? moonriseText,
        string? moonsetText)
    {
        const int slots = 48;
        const int slotMinutes = 30;

        var labels = new string[slots];
        var sunData = new double?[slots];
        var moonData = new double?[slots];
        var sunNow = new double?[slots];
        var moonNow = new double?[slots];

        for (int i = 0; i < slots; i++)
        {
            labels[i] = new DateTime(2000, 1, 1)
                .AddMinutes(i * slotMinutes)
                .ToString("HH:mm", CultureInfo.InvariantCulture);
        }

        int currentSlot = observationWindowEndLocal.HasValue
            ? Math.Clamp((int)(observationWindowEndLocal.Value.TimeOfDay.TotalMinutes / slotMinutes), 0, slots - 1)
            : -1;

        var sunrise = ParseConsoleTime(sunriseDisplay);
        var sunset = ParseConsoleTime(sunsetDisplay);

        if (sunrise.HasValue && sunset.HasValue)
        {
            int riseMin = sunrise.Value.Hour * 60 + sunrise.Value.Minute;
            int setMin = sunset.Value.Hour * 60 + sunset.Value.Minute;
            int dayLen = setMin - riseMin;

            if (dayLen > 0)
            {
                for (int i = 0; i < slots; i++)
                {
                    int t = i * slotMinutes;
                    if (t >= riseMin && t <= setMin)
                    {
                        double progress = (double)(t - riseMin) / dayLen;
                        sunData[i] = Math.Round(Math.Sin(progress * Math.PI) * 100.0, 1);
                    }
                }

                if (currentSlot >= 0 && sunData[currentSlot].HasValue)
                    sunNow[currentSlot] = sunData[currentSlot];
            }
        }

        var moonrise = ParseMoonTime(moonriseText);
        var moonset = ParseMoonTime(moonsetText);

        if (moonrise.HasValue && moonset.HasValue)
        {
            int riseMin = moonrise.Value.Hour * 60 + moonrise.Value.Minute;
            int setMin = moonset.Value.Hour * 60 + moonset.Value.Minute;
            bool crosses = setMin < riseMin;
            int duration = crosses ? (24 * 60 - riseMin) + setMin : setMin - riseMin;

            if (duration > 0)
            {
                for (int i = 0; i < slots; i++)
                {
                    int t = i * slotMinutes;
                    bool above = crosses ? (t >= riseMin || t <= setMin) : (t >= riseMin && t <= setMin);
                    if (above)
                    {
                        int elapsed = crosses && t < riseMin ? (24 * 60 - riseMin) + t : t - riseMin;
                        double progress = (double)elapsed / duration;
                        moonData[i] = Math.Round(Math.Sin(progress * Math.PI) * 85.0, 1);
                    }
                }

                if (currentSlot >= 0 && moonData[currentSlot].HasValue)
                    moonNow[currentSlot] = moonData[currentSlot];
            }
        }

        bool hasMoon = moonData.Any(v => v.HasValue);

        var datasets = new List<HvoChartDataset>
        {
            new("", sunData, SunColor, SunFillColor,
                Fill: true, BorderWidth: 2, PointRadius: 0, Tension: 0.4),
            new("", sunNow, SunColor, SunColor,
                Fill: false, BorderWidth: 2, PointRadius: 8, Tension: 0),
        };

        if (hasMoon)
        {
            datasets.Add(new("", moonData, MoonColor, "rgba(159,184,212,0.12)",
                Fill: false, BorderWidth: 1.5, PointRadius: 0, Tension: 0.4)); // --shell-page-muted-text derived fill
            datasets.Add(new("", moonNow, MoonNowColor, MoonNowColor,
                Fill: false, BorderWidth: 2, PointRadius: 7, Tension: 0));
        }

        return new DavisAstronomicalChartModel(labels, datasets.ToArray());
    }

    private static TimeOnly? ParseConsoleTime(string? text) =>
        text is not null &&
        TimeOnly.TryParseExact(text, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)
            ? t : null;

    private static TimeOnly? ParseMoonTime(string? text) =>
        text is not null &&
        TimeOnly.TryParseExact(text, "h:mm tt", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)
            ? t : null;
}

public sealed record DavisAstronomicalChartModel(
    string[] Labels,
    HvoChartDataset[] Datasets);
