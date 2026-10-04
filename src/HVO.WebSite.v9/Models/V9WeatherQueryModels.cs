namespace HVO.WebSite.v9.Models;

/// <summary>Canonical live fields persisted in v9.WeatherRaw; units are explicit in field names.</summary>
public sealed record V9WeatherObservation
{
    public long Id { get; init; }
    public required string StationId { get; init; }
    public DateTime RecordedAtUtc { get; init; }
    public double? TemperatureF { get; init; }
    public double? HumidityPercent { get; init; }
    public double? DewPointF { get; init; }
    public double? BarometricPressureInHg { get; init; }
    public double? WindSpeedMph { get; init; }
    public double? WindGustMph { get; init; }
    public int? WindDirectionDegrees { get; init; }
    public double? RainfallInches { get; init; }
    public double? SolarRadiationWm2 { get; init; }
    public double? UvIndex { get; init; }
}

/// <summary>Bounded archive history fields; console local time is descriptive, never a UTC query key.</summary>
public sealed record V9WeatherArchiveObservation
{
    public long Id { get; init; }
    public required string StationId { get; init; }
    public DateTime RecordedAtUtc { get; init; }
    public DateTime ConsoleRecordedAtLocal { get; init; }
    public int ArchiveIntervalMinutes { get; init; }
    public double? TemperatureF { get; init; }
    public double? HighTemperatureF { get; init; }
    public double? LowTemperatureF { get; init; }
    public double? HumidityPercent { get; init; }
    public double? BarometricPressureInHg { get; init; }
    public double? WindSpeedMph { get; init; }
    public double? WindGustMph { get; init; }
    public double? WindDirectionDegrees { get; init; }
    public double? RainfallInches { get; init; }
    public double? RainRateInchesPerHour { get; init; }
    public double? SolarRadiationWm2 { get; init; }
    public double? UvIndex { get; init; }
}

public enum V9WeatherAvailability { NoData, Current, Stale }

public sealed record V9WeatherCurrent(
    string StationId, DateTime CheckedAtUtc, V9WeatherAvailability Availability,
    V9WeatherObservation? Observation, TimeSpan? Age, TimeSpan StaleAfter);

/// <summary>Half-open UTC range and ascending exclusive timestamp continuation; unique station/time is the identity.</summary>
public sealed record V9WeatherHistoryPage<T>(
    string StationId, DateTime StartUtc, DateTime EndUtc, DateTime QueriedAtUtc,
    IReadOnlyList<T> Records, DateTimeOffset? NextAfterUtc)
{
    public bool HasMore => NextAfterUtc.HasValue;
}
