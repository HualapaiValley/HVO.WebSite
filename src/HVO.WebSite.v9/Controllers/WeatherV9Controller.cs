using System.ComponentModel.DataAnnotations;
using Asp.Versioning;
using HVO.DataModels.Data;
using HVO.DataModels.Models.V9;
using HVO.WebSite.v9.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HVO.WebSite.v9.Controllers;

/// <summary>
/// Weather v9 API — ingest raw readings from station devices and read recent data.
/// All ingest endpoints require the <c>ingest:weather</c> scope claim.
/// All read endpoints require the <c>read:weather</c> or <c>read:api</c> scope claim.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/weather/v9")]
[Tags("Weather v9")]
public class WeatherV9Controller : ControllerBase
{
    private readonly HvoV9DbContext _db;
    private readonly ILogger<WeatherV9Controller> _logger;

    public WeatherV9Controller(HvoV9DbContext db, ILogger<WeatherV9Controller> logger)
    {
        _db = db;
        _logger = logger;
    }

    // -------------------------------------------------------------------------
    // INGEST
    // -------------------------------------------------------------------------

    /// <summary>
    /// Ingest a raw weather observation from a station device.
    /// </summary>
    /// <remarks>
    /// Requires an API key with the <c>ingest:weather</c> scope claim.
    ///
    /// Sample request:
    ///
    ///     POST /api/v1/weather/v9/raw
    ///     X-Api-Key: &lt;your-key&gt;
    ///     {
    ///       "stationId": "hvo-davis-01",
    ///       "recordedAt": "2026-04-27T03:00:00Z",
    ///       "temperatureF": 68.2,
    ///       "humidityPercent": 42.0,
    ///       "barometricPressureInHg": 29.92,
    ///       "windSpeedMph": 8.5,
    ///       "windDirectionDegrees": 270
    ///     }
    /// </remarks>
    /// <response code="201">Record created. Returns the created record with its assigned ID.</response>
    /// <response code="400">Validation error — missing required fields or out-of-range values.</response>
    /// <response code="401">Missing or invalid API key.</response>
    /// <response code="403">API key does not have the ingest:weather scope.</response>
    [HttpPost("raw")]
    [Authorize(Policy = "WeatherIngest")]
    [ProducesResponseType(typeof(WeatherRawResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [Produces("application/json")]
    public async Task<ActionResult<WeatherRawResponse>> IngestRaw(
        [FromBody] IngestWeatherRawRequest request,
        CancellationToken ct)
    {
        var record = new WeatherRaw
        {
            RecordedAt = request.RecordedAt?.ToUniversalTime() ?? DateTime.UtcNow,
            StationId = request.StationId,
            TemperatureF = request.TemperatureF,
            HumidityPercent = request.HumidityPercent,
            DewPointF = request.DewPointF,
            BarometricPressureInHg = request.BarometricPressureInHg,
            WindSpeedMph = request.WindSpeedMph,
            WindGustMph = request.WindGustMph,
            WindDirectionDegrees = request.WindDirectionDegrees,
            RainfallInches = request.RainfallInches,
            SolarRadiationWm2 = request.SolarRadiationWm2,
            UvIndex = request.UvIndex
        };

        _db.WeatherRaw.Add(record);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Ingested raw weather record {Id} from station {StationId} at {RecordedAt}",
            record.Id, record.StationId, record.RecordedAt);

        var response = MapToResponse(record);
        return CreatedAtAction(nameof(GetRecentRaw), new { }, response);
    }

    // -------------------------------------------------------------------------
    // READ
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns the most recent raw weather observations.
    /// </summary>
    /// <param name="stationId">Filter by station ID (optional).</param>
    /// <param name="limit">Maximum number of records to return (1–500, default 100).</param>
    /// <response code="200">Returns a list of raw observations, newest first.</response>
    /// <response code="401">Missing or invalid API key.</response>
    /// <response code="403">API key does not have the read:weather scope.</response>
    [HttpGet("raw/recent")]
    [Authorize(Policy = "WeatherRead")]
    [ProducesResponseType(typeof(IReadOnlyList<WeatherRawResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [Produces("application/json")]
    public async Task<ActionResult<IReadOnlyList<WeatherRawResponse>>> GetRecentRaw(
        [FromQuery] string? stationId,
        [FromQuery][Range(1, 500)] int limit = 100,
        CancellationToken ct = default)
    {
        var query = _db.WeatherRaw.AsQueryable();

        if (!string.IsNullOrWhiteSpace(stationId))
            query = query.Where(r => r.StationId == stationId);

        var records = await query
            .OrderByDescending(r => r.RecordedAt)
            .Take(limit)
            .ToListAsync(ct);

        return Ok(records.Select(MapToResponse).ToList());
    }

    /// <summary>
    /// Returns recent hourly weather summaries.
    /// </summary>
    /// <param name="stationId">Filter by station ID (optional).</param>
    /// <param name="limit">Maximum number of records to return (1–168, default 24).</param>
    /// <response code="200">Returns a list of hourly summaries, newest first.</response>
    /// <response code="401">Missing or invalid API key.</response>
    /// <response code="403">API key does not have the read:weather scope.</response>
    [HttpGet("hourly/recent")]
    [Authorize(Policy = "WeatherRead")]
    [ProducesResponseType(typeof(IReadOnlyList<WeatherHourlyResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [Produces("application/json")]
    public async Task<ActionResult<IReadOnlyList<WeatherHourlyResponse>>> GetRecentHourly(
        [FromQuery] string? stationId,
        [FromQuery][Range(1, 168)] int limit = 24,
        CancellationToken ct = default)
    {
        var query = _db.WeatherHourly.AsQueryable();

        if (!string.IsNullOrWhiteSpace(stationId))
            query = query.Where(r => r.StationId == stationId);

        var records = await query
            .OrderByDescending(r => r.PeriodStart)
            .Take(limit)
            .ToListAsync(ct);

        return Ok(records.Select(MapToHourlyResponse).ToList());
    }

    // -------------------------------------------------------------------------
    // Mapping helpers
    // -------------------------------------------------------------------------

    private static WeatherRawResponse MapToResponse(WeatherRaw r) => new()
    {
        Id = r.Id,
        StationId = r.StationId,
        RecordedAt = r.RecordedAt,
        TemperatureF = r.TemperatureF,
        HumidityPercent = r.HumidityPercent,
        DewPointF = r.DewPointF,
        BarometricPressureInHg = r.BarometricPressureInHg,
        WindSpeedMph = r.WindSpeedMph,
        WindGustMph = r.WindGustMph,
        WindDirectionDegrees = r.WindDirectionDegrees,
        RainfallInches = r.RainfallInches,
        SolarRadiationWm2 = r.SolarRadiationWm2,
        UvIndex = r.UvIndex
    };

    private static WeatherHourlyResponse MapToHourlyResponse(WeatherHourly r) => new()
    {
        Id = r.Id,
        StationId = r.StationId,
        PeriodStart = r.PeriodStart,
        AvgTemperatureF = r.AvgTemperatureF,
        MinTemperatureF = r.MinTemperatureF,
        MaxTemperatureF = r.MaxTemperatureF,
        AvgHumidityPercent = r.AvgHumidityPercent,
        AvgDewPointF = r.AvgDewPointF,
        AvgBarometricPressureInHg = r.AvgBarometricPressureInHg,
        AvgWindSpeedMph = r.AvgWindSpeedMph,
        MaxWindGustMph = r.MaxWindGustMph,
        DominantWindDirectionDegrees = r.DominantWindDirectionDegrees,
        TotalRainfallInches = r.TotalRainfallInches,
        AvgSolarRadiationWm2 = r.AvgSolarRadiationWm2,
        MaxUvIndex = r.MaxUvIndex
    };
}
