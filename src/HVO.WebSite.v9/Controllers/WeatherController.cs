using Microsoft.AspNetCore.Mvc;
using HVO.Core.Results;
using Asp.Versioning;
using HVO.WebSite.v9.Models;
using HVO.WebSite.v9.Services;
using Microsoft.AspNetCore.Authorization;

namespace HVO.WebSite.v9.Controllers
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
        private readonly TimeProvider _clock;

        /// <summary>
        /// Initializes a new instance of the WeatherController
        /// </summary>
        /// <param name="weatherService">Service for weather data operations</param>
        /// <param name="logger">Logger for tracking weather API operations</param>
        public WeatherController(IWeatherService weatherService, ILogger<WeatherController> logger, TimeProvider? clock = null)
        {
            _weatherService = weatherService;
            _logger = logger;
            _clock = clock ?? TimeProvider.System;
        }

        /// <summary>
        /// Gets the latest weather record
        /// </summary>
        /// <returns>The most recent weather data</returns>
        /// <response code="200">Returns the latest weather record with timestamp and machine info</response>
        /// <response code="401">Missing or invalid API key.</response>
        /// <response code="403">API key does not have the read:weather or read:api scope.</response>
        /// <response code="404">No weather records found in the database</response>
        /// <response code="500">Internal server error occurred while retrieving weather data</response>
        [HttpGet("latest")]
        [Authorize(Policy = "WeatherRead")]
        [ProducesResponseType(typeof(LatestWeatherResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
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
        /// <param name="startDate">Compatible alias for start.</param>
        /// <param name="endDate">Compatible alias for end.</param>
        /// <param name="start">Inclusive start; defaults to 24 hours before end.</param>
        /// <param name="end">Exclusive end; defaults to now or 24 hours after start.</param>
        /// <param name="stationId">Station identifier; defaults to the latest station in the range.</param>
        /// <param name="cancellationToken">Request cancellation.</param>
        /// <returns>Weather highs and lows summary for the specified date range</returns>
        /// <remarks>
        /// Sample request:
        /// 
        ///     GET /api/v1.0/weather/highs-lows?startDate=2025-07-13T00:00:00Z&amp;endDate=2025-07-14T00:00:00Z
        /// 
        /// If no dates are provided, defaults to the preceding 24 hours. Longer ranges return 400.
        /// </remarks>
        /// <response code="200">Returns weather highs and lows for the specified date range</response>
        /// <response code="401">Missing or invalid API key.</response>
        /// <response code="404">No weather data found for the specified date range</response>
        /// <response code="500">Internal server error occurred while retrieving weather data</response>
        [HttpGet("highs-lows")]
        [AllowAnonymous]
        [ProducesResponseType(typeof(WeatherHighsLowsResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
        [Produces("application/json")]
        public async Task<ActionResult<WeatherHighsLowsResponse>> GetWeatherHighsLows(
            [FromQuery] DateTimeOffset? startDate = null,
            [FromQuery] DateTimeOffset? endDate = null,
            [FromQuery] DateTimeOffset? start = null,
            [FromQuery] DateTimeOffset? end = null,
            [FromQuery][System.ComponentModel.DataAnnotations.MaxLength(64)] string? stationId = null,
            CancellationToken cancellationToken = default)
        {
            WeatherHistoryRange range;
            try
            {
                if (start.HasValue && startDate.HasValue && start != startDate
                    || end.HasValue && endDate.HasValue && end != endDate)
                    throw new ArgumentException("Use matching start/startDate and end/endDate values, or only one spelling.");
                range = WeatherHistoryRange.Create(start ?? startDate, end ?? endDate, _clock);
            }
            catch (ArgumentException error)
            {
                return Problem(title: "Invalid Weather Range", detail: error.Message, statusCode: StatusCodes.Status400BadRequest);
            }
            var result = await _weatherService.GetWeatherHighsLowsAsync(range.Start, range.End, stationId, cancellationToken);
            
            return result.Match(
                success: data => Ok(data),
                failure: error => error switch
                {
                    ArgumentException => Problem(title: "Invalid Weather Range", detail: error.Message, statusCode: StatusCodes.Status400BadRequest),
                    WeatherDataNotFoundException => Problem(
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
        /// Gets public current weather conditions including today's highs and lows
        /// </summary>
        /// <returns>Current weather conditions combined with today's extremes in a single response</returns>
        /// <remarks>
        /// This endpoint provides the most comprehensive weather information by combining:
        /// - Current weather conditions (temperature, humidity, wind, pressure, etc.)
        /// - Today's high and low values with timestamps
        /// - Available live weather parameters, including dew point
        /// - Solar radiation and UV index data
        /// 
        /// Reads live gateway observations from v9.WeatherRaw. Parameters not persisted
        /// there are null. Today's extremes use the server's local day and the same station
        /// as the latest observation. No API key is required.
        /// </remarks>
        /// <response code="200">Returns current weather conditions with today's highs and lows</response>
        /// <response code="401">An optional API key was supplied but is invalid.</response>
        /// <response code="404">No current weather data available</response>
        /// <response code="500">Internal server error occurred while retrieving weather data</response>
        [HttpGet("current")]
        [AllowAnonymous]
        [ProducesResponseType(typeof(CurrentWeatherResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
        [Produces("application/json")]
        public async Task<ActionResult<CurrentWeatherResponse>> GetCurrentWeatherConditions(CancellationToken cancellationToken = default)
        {
            var result = await _weatherService.GetCurrentWeatherConditionsAsync(cancellationToken);
            
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
