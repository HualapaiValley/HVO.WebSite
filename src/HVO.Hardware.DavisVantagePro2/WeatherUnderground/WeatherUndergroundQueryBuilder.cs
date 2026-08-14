using System.Globalization;
using HVO.Hardware.DavisVantagePro2.Protocol.Packets;

namespace HVO.Hardware.DavisVantagePro2.WeatherUnderground;

internal static class WeatherUndergroundQueryBuilder
{
    public static readonly Uri Endpoint = new(
        "https://rtupdate.wunderground.com/weatherstation/updateweatherstation.php",
        UriKind.Absolute);

    public static Uri BuildRelativeUri(
        string stationId,
        string stationKey,
        Loop2Packet observation,
        int rapidFireFrequencySeconds)
    {
        var values = new List<KeyValuePair<string, string>>
        {
            Pair("ID", stationId),
            Pair("PASSWORD", stationKey),
            Pair("dateutc", DateTime.SpecifyKind(observation.RecordedAtUtc, DateTimeKind.Utc)
                .ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),
        };

        Add(values, "tempf", observation.OutsideTemperatureF);
        Add(values, "indoortempf", observation.InsideTemperatureF);
        Add(values, "dewptf", observation.DewPointF);
        Add(values, "humidity", observation.OutsideHumidityPercent);
        Add(values, "indoorhumidity", observation.InsideHumidityPercent);
        Add(values, "windspeedmph", observation.WindSpeedMph);
        Add(values, "winddir", observation.WindDirectionDegrees);
        Add(values, "windspdmph_avg2m", observation.WindSpeed2MinAvgMph);
        Add(values, "windgustmph_10m", observation.WindGust10MinMph);
        Add(values, "windgustdir_10m", observation.WindGust10MinDirectionDegrees);
        Add(values, "rainin", observation.HourRainInches);
        Add(values, "dailyrainin", observation.DailyRainInches);
        Add(values, "baromin", observation.BarometricPressureInHg);
        Add(values, "solarradiation", observation.SolarRadiationWm2);
        Add(values, "UV", observation.UvIndex);
        values.Add(Pair("action", "updateraw"));
        values.Add(Pair("realtime", "1"));
        values.Add(Pair("rtfreq", rapidFireFrequencySeconds.ToString(CultureInfo.InvariantCulture)));

        var query = string.Join('&', values.Select(static pair =>
            $"{pair.Key}={Uri.EscapeDataString(pair.Value)}"));
        return new Uri($"?{query}", UriKind.Relative);
    }

    private static KeyValuePair<string, string> Pair(string name, string value) => new(name, value);

    private static void Add(List<KeyValuePair<string, string>> values, string name, double? value)
    {
        if (value.HasValue && double.IsFinite(value.Value))
            values.Add(Pair(name, value.Value.ToString("0.###", CultureInfo.InvariantCulture)));
    }
}
