using Microsoft.AspNetCore.Mvc;
using HVO.Core.Results;
using Asp.Versioning;
using HVO.WebSite.Playground.Models;
using HVO.WebSite.Playground.Services;
using System.Text.RegularExpressions;

namespace HVO.WebSite.Playground.Controllers
{
    /// <summary>
    /// Weather API controller for retrieving current weather conditions and daily highs/lows
    /// </summary>
    /// <remarks>
    /// This controller provides access to weather data from the Hualapai Valley Observatory
    /// Davis Vantage Pro weather console. All temperature values are in Fahrenheit,
    /// wind speeds in mph, pressure in inches of mercury, and rainfall in inches.
    /// </remarks>
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/weather")]
    [Tags("Weather")]
    public class WeatherController : ControllerBase
    {
        private readonly IWeatherService _weatherService;
        private readonly ILogger<WeatherController> _logger;

        /// <summary>
        /// Initializes a new instance of the WeatherController
        /// </summary>
        /// <param name="weatherService">Service for weather data operations</param>
        /// <param name="logger">Logger for tracking weather API operations</param>
        public WeatherController(IWeatherService weatherService, ILogger<WeatherController> logger)
        {
            _weatherService = weatherService;
            _logger = logger;
        }

        /// <summary>
        /// Gets the latest weather record
        /// </summary>
        /// <returns>The most recent weather data</returns>
        /// <response code="200">Returns the latest weather record with timestamp and machine info</response>
        /// <response code="404">No weather records found in the database</response>
        /// <response code="500">Internal server error occurred while retrieving weather data</response>
        [HttpGet("latest")]
        [ProducesResponseType(typeof(LatestWeatherResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
        [Produces("application/json")]
        public async Task<ActionResult<LatestWeatherResponse>> GetLatestWeatherRecord()
        {
            var result = await _weatherService.GetLatestWeatherRecordAsync();

            return result.Match(
                success: data => Ok(data),
                failure: error => error switch
                {
                    InvalidOperationException => Problem(
                        title: "Weather Data Not Found",
                        detail: error.Message,
                        statusCode: StatusCodes.Status404NotFound
                    ),
                    _ => Problem(
                        title: "Internal Server Error",
                        detail: "An error occurred while retrieving weather data",
                        statusCode: StatusCodes.Status500InternalServerError
                    )
                }
            );
        }

        /// <summary>
        /// Gets weather highs and lows for a specified date range
        /// </summary>
        /// <param name="startDate">Start date in ISO format (defaults to today if not provided)</param>
        /// <param name="endDate">End date in ISO format (defaults to tomorrow if not provided)</param>
        /// <returns>Weather highs and lows summary for the specified date range</returns>
        /// <remarks>
        /// Sample request:
        /// 
        ///     GET /api/v1.0/weather/highs-lows?startDate=2025-07-13T00:00:00Z&amp;endDate=2025-07-14T00:00:00Z
        /// 
        /// If no dates are provided, defaults to today's date range.
        /// </remarks>
        /// <response code="200">Returns weather highs and lows for the specified date range</response>
        /// <response code="404">No weather data found for the specified date range</response>
        /// <response code="500">Internal server error occurred while retrieving weather data</response>
        [HttpGet("highs-lows")]
        [ProducesResponseType(typeof(WeatherHighsLowsResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
        [Produces("application/json")]
        public async Task<ActionResult<WeatherHighsLowsResponse>> GetWeatherHighsLows(
            [FromQuery] DateTimeOffset? startDate = null,
            [FromQuery] DateTimeOffset? endDate = null)
        {
            var result = await _weatherService.GetWeatherHighsLowsAsync(startDate, endDate);

            return result.Match(
                success: data => Ok(data),
                failure: error => error switch
                {
                    InvalidOperationException => Problem(
                        title: "Weather Data Not Found",
                        detail: error.Message,
                        statusCode: StatusCodes.Status404NotFound
                    ),
                    _ => Problem(
                        title: "Internal Server Error",
                        detail: "An error occurred while retrieving weather highs and lows data",
                        statusCode: StatusCodes.Status500InternalServerError
                    )
                }
            );
        }

        /// <summary>
        /// Gets current weather conditions including today's highs and lows
        /// </summary>
        /// <returns>Current weather conditions combined with today's extremes in a single response</returns>
        /// <remarks>
        /// This endpoint provides the most comprehensive weather information by combining:
        /// - Current weather conditions (temperature, humidity, wind, pressure, etc.)
        /// - Today's high and low values with timestamps
        /// - All weather parameters including heat index, wind chill, dew point
        /// - Solar radiation and UV index data
        /// 
        /// This is the recommended endpoint for most weather applications.
        /// </remarks>
        /// <response code="200">Returns current weather conditions with today's highs and lows</response>
        /// <response code="404">No current weather data available</response>
        /// <response code="500">Internal server error occurred while retrieving weather data</response>
        [HttpGet("current")]
        [ProducesResponseType(typeof(CurrentWeatherResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
        [Produces("application/json")]
        public async Task<ActionResult<CurrentWeatherResponse>> GetCurrentWeatherConditions()
        {
            var result = await _weatherService.GetCurrentWeatherConditionsAsync();

            return result.Match(
                success: data => Ok(data),
                failure: error => error switch
                {
                    InvalidOperationException => Problem(
                        title: "Current Weather Data Not Found",
                        detail: error.Message,
                        statusCode: StatusCodes.Status404NotFound
                    ),
                    _ => Problem(
                        title: "Internal Server Error",
                        detail: "An error occurred while retrieving current weather conditions",
                        statusCode: StatusCodes.Status500InternalServerError
                    )
                }
            );
        }
    }
}
