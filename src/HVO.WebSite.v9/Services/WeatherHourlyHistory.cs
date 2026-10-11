using HVO.DataModels.Data;
using HVO.WebSite.v9.Models;
using Microsoft.EntityFrameworkCore;

namespace HVO.WebSite.v9.Services;

internal static class WeatherHourlyHistory
{
    public static async Task<IReadOnlyList<WeatherHourlyResponse>> ReadAsync(HvoV9DbContext db,
        WeatherHistoryRange range, string? stationId, int limit, CancellationToken cancellationToken)
    {
        var start = range.Start.UtcDateTime;
        var end = range.End.UtcDateTime;
        var readings = db.WeatherRaw.AsNoTracking().Where(row => row.RecordedAt >= start && row.RecordedAt < end);
        if (!string.IsNullOrWhiteSpace(stationId))
            readings = readings.Where(row => row.StationId == stationId);

        // SQL aggregates only observations inside the requested window, including partial hours.
        var hours = await readings.GroupBy(row => new
            { row.StationId, row.RecordedAt.Year, row.RecordedAt.Month, row.RecordedAt.Day, row.RecordedAt.Hour })
            .Select(group => new
            {
                group.Key.StationId, group.Key.Year, group.Key.Month, group.Key.Day, group.Key.Hour,
                Id = group.Min(row => row.Id),
                Temperature = group.Average(row => row.TemperatureF),
                MinimumTemperature = group.Min(row => row.TemperatureF),
                MaximumTemperature = group.Max(row => row.TemperatureF),
                Humidity = group.Average(row => row.HumidityPercent),
                DewPoint = group.Average(row => row.DewPointF),
                Pressure = group.Average(row => row.BarometricPressureInHg),
                Wind = group.Average(row => row.WindSpeedMph),
                Gust = group.Max(row => row.WindGustMph),
                Rain = group.Count(row => row.RainfallInches != null) == group.Count()
                    ? group.Sum(row => row.RainfallInches) : (double?)null,
                Solar = group.Average(row => row.SolarRadiationWm2),
                Uv = group.Max(row => row.UvIndex)
            }).OrderByDescending(hour => hour.Year).ThenByDescending(hour => hour.Month)
            .ThenByDescending(hour => hour.Day).ThenByDescending(hour => hour.Hour).ThenBy(hour => hour.StationId)
            .Take(limit).ToListAsync(cancellationToken);

        if (hours.Count == 0)
            return [];

        // Keep compass directions categorical: choose the most frequent reported direction,
        // breaking ties by the smallest degree. Never average 359 degrees with 1 degree.
        var directions = await readings.Where(row => row.WindDirectionDegrees >= 0 && row.WindDirectionDegrees <= 360)
            .GroupBy(row => new
            {
                row.StationId, row.RecordedAt.Year, row.RecordedAt.Month, row.RecordedAt.Day,
                row.RecordedAt.Hour, row.WindDirectionDegrees
            }).Select(group => new
            {
                group.Key.StationId, group.Key.Year, group.Key.Month, group.Key.Day, group.Key.Hour,
                Direction = group.Key.WindDirectionDegrees, Count = group.Count()
            }).ToListAsync(cancellationToken);
        var modes = directions.GroupBy(row => (row.StationId, row.Year, row.Month, row.Day, row.Hour))
            .ToDictionary(group => group.Key, group => group.OrderByDescending(row => row.Count)
                .ThenBy(row => row.Direction).First().Direction);

        return hours.Select(hour => new WeatherHourlyResponse
        {
            Id = hour.Id, StationId = hour.StationId,
            PeriodStart = new DateTime(hour.Year, hour.Month, hour.Day, hour.Hour, 0, 0, DateTimeKind.Utc),
            AvgTemperatureF = hour.Temperature, MinTemperatureF = hour.MinimumTemperature, MaxTemperatureF = hour.MaximumTemperature,
            AvgHumidityPercent = hour.Humidity, AvgDewPointF = hour.DewPoint, AvgBarometricPressureInHg = hour.Pressure,
            AvgWindSpeedMph = hour.Wind, MaxWindGustMph = hour.Gust,
            DominantWindDirectionDegrees = modes.GetValueOrDefault((hour.StationId, hour.Year, hour.Month, hour.Day, hour.Hour)),
            TotalRainfallInches = hour.Rain, AvgSolarRadiationWm2 = hour.Solar, MaxUvIndex = hour.Uv
        }).ToArray();
    }
}
