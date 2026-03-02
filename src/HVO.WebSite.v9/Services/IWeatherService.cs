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
    /// <param name="startDate">Start date for the range (defaults to today if null)</param>
    /// <param name="endDate">End date for the range (defaults to tomorrow if null)</param>
    /// <returns>Result containing weather highs/lows data or error information</returns>
    Task<Result<WeatherHighsLowsResponse>> GetWeatherHighsLowsAsync(DateTimeOffset? startDate, DateTimeOffset? endDate);

    /// <summary>
    /// Gets current weather conditions including today's highs and lows
    /// </summary>
    /// <returns>Result containing comprehensive current weather data or error information</returns>
    Task<Result<CurrentWeatherResponse>> GetCurrentWeatherConditionsAsync();
}

