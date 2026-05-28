using System.ComponentModel.DataAnnotations;

namespace HVO.WebSite.v9.Models;

/// <summary>
/// Request body for ingesting a raw weather observation from a station device.
/// </summary>
public class IngestWeatherRawRequest
{
    /// <summary>Station identifier (e.g. "hvo-davis-01")</summary>
    [Required]
    [MaxLength(64)]
    public required string StationId { get; init; }

    /// <summary>UTC timestamp of the observation. Defaults to server time if omitted.</summary>
    public DateTime? RecordedAt { get; init; }

    public double? TemperatureF { get; init; }

    [Range(0, 100)]
    public double? HumidityPercent { get; init; }

    public double? DewPointF { get; init; }

    [Range(20, 35)]
    public double? BarometricPressureInHg { get; init; }

    [Range(0, 300)]
    public double? WindSpeedMph { get; init; }

    [Range(0, 300)]
    public double? WindGustMph { get; init; }

    [Range(0, 300)]
    public double? WindGust10MinMph { get; init; }

    [Range(0, 359)]
    public int? WindDirectionDegrees { get; init; }

    [Range(0, 100)]
    public double? RainfallInches { get; init; }

    [Range(0, 2000)]
    public double? SolarRadiationWm2 { get; init; }

    [Range(0, 20)]
    public double? UvIndex { get; init; }
}

/// <summary>Response body for a batch ingest request.</summary>
public class WeatherRawBatchResponse
{
    /// <summary>Number of records successfully inserted (new records only).</summary>
    public int Inserted { get; init; }

    /// <summary>Number of records skipped because they already existed (duplicate StationId+RecordedAt).</summary>
    public int Skipped { get; init; }

    /// <summary>Records that could not be inserted due to permanent validation errors.</summary>
    public IReadOnlyList<WeatherRawBatchFailure> Failed { get; init; } = [];
}

/// <summary>Describes a single record that failed validation within a batch ingest.</summary>
public class WeatherRawBatchFailure
{
    public DateTime RecordedAt { get; init; }
    public string Error { get; init; } = string.Empty;
}

/// <summary>
/// A single raw weather observation returned from the API.
/// </summary>
public class WeatherRawResponse
{
    public long Id { get; init; }
    public string? StationId { get; init; }
    public DateTime RecordedAt { get; init; }
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

/// <summary>
/// An hourly weather summary returned from the API.
/// </summary>
public class WeatherHourlyResponse
{
    public long Id { get; init; }
    public string? StationId { get; init; }
    public DateTime PeriodStart { get; init; }
    public double? AvgTemperatureF { get; init; }
    public double? MinTemperatureF { get; init; }
    public double? MaxTemperatureF { get; init; }
    public double? AvgHumidityPercent { get; init; }
    public double? AvgDewPointF { get; init; }
    public double? AvgBarometricPressureInHg { get; init; }
    public double? AvgWindSpeedMph { get; init; }
    public double? MaxWindGustMph { get; init; }
    public int? DominantWindDirectionDegrees { get; init; }
    public double? TotalRainfallInches { get; init; }
    public double? AvgSolarRadiationWm2 { get; init; }
    public double? MaxUvIndex { get; init; }
}
