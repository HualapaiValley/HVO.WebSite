using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Asp.Versioning;
using HVO.DataModels.Data;
using HVO.DataModels.Models.V9;
using HVO.WebSite.v9.Infrastructure;
using HVO.WebSite.v9.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace HVO.WebSite.v9.Controllers;

/// <summary>
/// Weather ingest API — ingest raw readings from station devices and read recent data.
/// All ingest endpoints require the <c>ingest:weather</c> scope claim.
/// All read endpoints require the <c>read:weather</c> or <c>read:api</c> scope claim.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/weather")]
[Tags("Weather")]
public class WeatherIngestController : ControllerBase
{
    /// <summary>
    /// Maximum number of records per ingest batch. Values above this threshold
    /// risk exceeding SQL Server's 2100-parameter limit in IN-list deduplication
    /// queries and are rejected with HTTP 400.
    /// </summary>
    public const int MaxBatchSize = 500;

    private readonly HvoV9DbContext _db;
    private readonly ILogger<WeatherIngestController> _logger;

    public WeatherIngestController(HvoV9DbContext db, ILogger<WeatherIngestController> logger)
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
    ///     POST /api/v1/weather/raw
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
            WindGustMph = request.WindGustMph ?? request.WindGust10MinMph,
            WindDirectionDegrees = request.WindDirectionDegrees,
            RainfallInches = request.RainfallInches,
            SolarRadiationWm2 = request.SolarRadiationWm2,
            UvIndex = request.UvIndex
        };

        _db.WeatherRaw.Add(record);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            // Duplicate record — already in DB, treat as success (idempotent)
            _logger.LogDebug("Duplicate raw weather record from station {StationId} at {RecordedAt} — skipped",
                record.StationId, record.RecordedAt);
            return CreatedAtAction(nameof(GetRecentRaw), new { }, MapToResponse(record));
        }

        _logger.LogInformation(
            "Ingested raw weather record {Id} from station {StationId} at {RecordedAt}",
            record.Id, record.StationId, record.RecordedAt);

        var response = MapToResponse(record);
        return CreatedAtAction(nameof(GetRecentRaw), new { }, response);
    }

    /// <summary>
    /// Ingest a batch of raw weather observations from a station device.
    /// </summary>
    /// <remarks>
    /// Idempotent: duplicate records (same StationId + RecordedAt) are silently skipped.
    /// Per-record validation failures are returned in the response body rather than failing
    /// the entire request, so the caller can dead-letter those records and retry the rest.
    /// Requires an API key with the <c>ingest:weather</c> scope claim.
    /// </remarks>
    /// <response code="201">Batch processed. See Inserted/Skipped/Failed counts in response body.</response>
    /// <response code="400">Empty batch or malformed request body.</response>
    /// <response code="401">Missing or invalid API key.</response>
    /// <response code="403">API key does not have the ingest:weather scope.</response>
    [HttpPost("raw/batch")]
    [Authorize(Policy = "WeatherIngest")]
    [ProducesResponseType(typeof(WeatherRawBatchResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [Produces("application/json")]
    public async Task<ActionResult<WeatherRawBatchResponse>> IngestRawBatch(
        [FromBody] JsonElement batch,
        CancellationToken ct)
    {
        // Detect and unwrap CloudEvents 1.0 envelope format, or use raw payloads for backward compat
        List<JsonElement> rawPayloads;
        if (batch.ValueKind == JsonValueKind.Array)
        {
            var first = batch.EnumerateArray().FirstOrDefault();
            if (first.ValueKind == JsonValueKind.Object && CloudEventsBatchUnwrapper.IsCloudEventsBatch(first))
            {
                var (data, errors) = CloudEventsBatchUnwrapper.UnwrapBatch(batch, _logger);
                if (errors.Count > 0 && data.Count == 0)
                    return ValidationProblem(detail: $"Failed to unwrap CloudEvents batch: {string.Join("; ", errors.Take(3))}");
                rawPayloads = data;
            }
            else
            {
                // Legacy format — each element is a raw payload
                rawPayloads = batch.EnumerateArray().Select(e => e.Clone()).ToList();
            }
        }
        else
        {
            return ValidationProblem(detail: "Request body must be a JSON array.");
        }

        if (rawPayloads.Count == 0)
            return ValidationProblem(detail: "Batch must contain at least one valid record.");

        if (rawPayloads.Count > MaxBatchSize)
            return ValidationProblem(detail: $"Batch size {rawPayloads.Count} exceeds the maximum of {MaxBatchSize} records. Split the batch or reduce the outbox batch size on the gateway.");

        // Deserialize each (possibly unwrapped) payload
        var requests = new List<IngestWeatherRawRequest>();
        var deserErrors = new List<WeatherRawBatchFailure>();
        foreach (var (payload, index) in rawPayloads.Select((p, i) => (p, i)))
        {
            DateTime? fallbackTime = null;
            string fallbackStationId = string.Empty;
            if (payload.ValueKind == JsonValueKind.Object)
            {
                if (payload.TryGetProperty("stationId", out var stationId) && stationId.ValueKind == JsonValueKind.String)
                    fallbackStationId = stationId.GetString() ?? string.Empty;
                if (payload.TryGetProperty("recordedAt", out var ra) && ra.ValueKind == JsonValueKind.String)
                    fallbackTime = ra.GetDateTime();
                else if (payload.TryGetProperty("recordedAtUtc", out var rau) && rau.ValueKind == JsonValueKind.String)
                    fallbackTime = rau.GetDateTime();
            }

            try
            {
                var request = payload.Deserialize<IngestWeatherRawRequest>(
                    new JsonSerializerOptions(JsonSerializerDefaults.Web));
                if (request is not null)
                    requests.Add(request);
                else
                    deserErrors.Add(new WeatherRawBatchFailure
                    {
                        StationId = fallbackStationId,
                        RecordedAt = fallbackTime ?? DateTime.UtcNow,
                        Error = $"Record at index {index} deserialized to null."
                    });
            }
            catch (JsonException ex)
            {
                deserErrors.Add(new WeatherRawBatchFailure
                {
                    StationId = fallbackStationId,
                    RecordedAt = fallbackTime ?? DateTime.UtcNow,
                    Error = $"Record at index {index}: invalid JSON — {ex.Message}"
                });
            }
        }

        if (requests.Count == 0)
        {
            return CreatedAtAction(nameof(GetRecentRaw), new { },
                new WeatherRawBatchResponse { Inserted = 0, Skipped = 0, Failed = deserErrors });
        }

        if (!await IngestSourceAuthority.CanWriteAllAsync(
            _db, User, requests.Select(static request => ((string?)request.StationId, request.SourceSystem)), ct))
            return Forbid();

        // Resolve timestamps up-front (null RecordedAt → server time)
        var resolved = requests.Select(r => (Request: r, RecordedAt: r.RecordedAt?.ToUniversalTime() ?? DateTime.UtcNow)).ToList();

        // One query to find which (StationId, RecordedAt) pairs already exist
        var stationIds = resolved.Select(x => x.Request.StationId).Distinct().ToList();
        var timestamps = resolved.Select(x => x.RecordedAt).Distinct().ToList();
        var existing = await _db.WeatherRaw
            .Where(r => r.StationId != null && stationIds.Contains(r.StationId) && timestamps.Contains(r.RecordedAt))
            .Select(r => new { r.StationId, r.RecordedAt })
            .ToListAsync(ct);
        var existingKeys = existing.Select(x => (x.StationId, x.RecordedAt)).ToHashSet();

        var toInsert = new List<WeatherRaw>();
        var failures = new List<WeatherRawBatchFailure>(deserErrors);
        int skipped = 0;
        var seenInBatch = new HashSet<(string?, DateTime)>();

        foreach (var (request, recordedAt) in resolved)
        {
            // Skip duplicates — already in DB, treat as success
            if (existingKeys.Contains((request.StationId, recordedAt)))
            {
                skipped++;
                continue;
            }

            // Skip intra-batch duplicates by (StationId, RecordedAt)
            if (!seenInBatch.Add((request.StationId, recordedAt)))
            {
                skipped++;
                continue;
            }

            // Validate individual record (data annotations)
            var validationResults = new List<ValidationResult>();
            if (!Validator.TryValidateObject(request, new ValidationContext(request), validationResults, validateAllProperties: true))
            {
                failures.Add(new WeatherRawBatchFailure
                {
                    StationId = request.StationId,
                    RecordedAt = recordedAt,
                    Error = string.Join("; ", validationResults.Select(r => r.ErrorMessage))
                });
                continue;
            }

            toInsert.Add(new WeatherRaw
            {
                RecordedAt = recordedAt,
                StationId = request.StationId,
                TemperatureF = request.TemperatureF,
                HumidityPercent = request.HumidityPercent,
                DewPointF = request.DewPointF,
                BarometricPressureInHg = request.BarometricPressureInHg,
                WindSpeedMph = request.WindSpeedMph,
                WindGustMph = request.WindGustMph ?? request.WindGust10MinMph,
                WindDirectionDegrees = request.WindDirectionDegrees,
                RainfallInches = request.RainfallInches,
                SolarRadiationWm2 = request.SolarRadiationWm2,
                UvIndex = request.UvIndex
            });
        }

        if (toInsert.Count > 0)
        {
            _db.WeatherRaw.AddRange(toInsert);
            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
            {
                // Race condition — another process inserted the same record between our check and insert.
                // Treat as idempotent success; caller gets accurate counts on the next sweep.
                _logger.LogDebug(ex, "Unique constraint violation in batch ingest (race condition) — treating as success");
                skipped += toInsert.Count;
                toInsert.Clear();
            }
            catch (DbUpdateException ex)
            {
                var distinctStations = string.Join(", ", resolved.Select(x => x.Request.StationId).Distinct());
                _logger.LogError(ex, "Batch ingest failed during SaveChanges for stations {StationIds}", distinctStations);
                return Problem(
                    detail: "An error occurred while persisting the batch. Retry is safe — duplicate records will be skipped.",
                    statusCode: StatusCodes.Status500InternalServerError,
                    title: "Batch Ingest Failed");
            }
        }

        var logStations = string.Join(", ", resolved.Select(x => x.Request.StationId).Distinct());
        _logger.LogInformation(
            "Batch ingest: {Inserted} inserted, {Skipped} skipped, {Failed} failed. Stations: {StationIds}",
            toInsert.Count, skipped, failures.Count, logStations);

        return CreatedAtAction(nameof(GetRecentRaw), new { },
            new WeatherRawBatchResponse { Inserted = toInsert.Count, Skipped = skipped, Failed = failures });
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
    // Private helpers
    // -------------------------------------------------------------------------

    private static bool IsUniqueConstraintViolation(DbUpdateException ex) =>
        ex.InnerException is SqlException sqlEx &&
        (sqlEx.Number == 2601 || sqlEx.Number == 2627);

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
