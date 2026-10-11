using HVO.WebSite.v9.Models;
using HVO.Core.Results;

namespace HVO.WebSite.v9.Services;

/// <summary>
/// Service interface for weather data operations
/// </summary>
public interface IWeatherService
{
    /// <summary>
    /// Gets the latest weather record from the database
    /// </summary>
    /// <returns>Result containing the latest weather data or error information</returns>
    Task<Result<LatestWeatherResponse>> GetLatestWeatherRecordAsync();

    /// <summary>
    /// Gets weather highs and lows for a specified date range
    /// </summary>
    /// <param name="startDate">Inclusive start; defaults to 24 hours before end.</param>
    /// <param name="endDate">Exclusive end; defaults to now, or 24 hours after start.</param>
    /// <param name="stationId">Station filter; otherwise uses the latest station in the range.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    /// <returns>Result containing weather highs/lows data or error information</returns>
    Task<Result<WeatherHighsLowsResponse>> GetWeatherHighsLowsAsync(DateTimeOffset? startDate, DateTimeOffset? endDate,
        string? stationId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets current weather conditions including today's highs and lows
    /// </summary>
    /// <returns>Result containing comprehensive current weather data or error information</returns>
    Task<Result<CurrentWeatherResponse>> GetCurrentWeatherConditionsAsync(CancellationToken cancellationToken = default);
}

