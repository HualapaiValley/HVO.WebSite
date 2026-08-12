using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Asp.Versioning;
using HVO.DataModels.Data;
using HVO.DataModels.Models.V9;
using HVO.Edge.Contracts.Weather;
using HVO.WebSite.v9.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace HVO.WebSite.v9.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/weather/archive")]
[Tags("Weather")]
public sealed class WeatherArchiveIngestController(
    HvoV9DbContext db,
    ILogger<WeatherArchiveIngestController> logger) : ControllerBase
{
    [HttpPost("batch")]
    [Authorize(Policy = "WeatherIngest")]
    [ProducesResponseType(typeof(WeatherArchiveBatchResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<WeatherArchiveBatchResponse>> IngestBatch(
        [FromBody] JsonElement batch,
        CancellationToken cancellationToken)
    {
        if (batch.ValueKind != JsonValueKind.Array)
            return ValidationProblem(detail: "Request body must be a JSON array.");
        var elements = batch.EnumerateArray().Select(static item => item.Clone()).ToArray();
        if (elements.Length == 0)
            return ValidationProblem(detail: "Batch must contain at least one record.");
        if (elements.Length > WeatherIngestController.MaxBatchSize)
            return ValidationProblem(detail: $"Batch size {elements.Length} exceeds the maximum of {WeatherIngestController.MaxBatchSize} records.");

        var failures = new List<WeatherArchiveBatchFailure>();
        var valid = new List<DavisWeatherArchivePayload>(elements.Length);
        var seen = new HashSet<(string StationId, DateTime RecordedAtUtc)>();
        var skipped = 0;
        foreach (var element in elements)
        {
            DavisWeatherArchivePayload? item;
            try
            {
                item = element.Deserialize<DavisWeatherArchivePayload>(JsonSerializerOptions.Web);
            }
            catch (JsonException exception)
            {
                failures.Add(new(string.Empty, default, $"Invalid archive payload: {exception.Message}"));
                continue;
            }

            var validation = new List<ValidationResult>();
            if (item is null || !Validator.TryValidateObject(item, new ValidationContext(item), validation, true)
                || item.RecordedAtUtc.Kind != DateTimeKind.Utc
                || item.ConsoleRecordedAtLocal.Kind != DateTimeKind.Unspecified)
            {
                failures.Add(new(
                    item?.StationId ?? string.Empty,
                    item?.RecordedAtUtc ?? default,
                    validation.Count == 0
                        ? "RecordedAtUtc must be UTC and ConsoleRecordedAtLocal must have Unspecified kind."
                        : string.Join("; ", validation.Select(static result => result.ErrorMessage))));
                continue;
            }

            var key = (item.StationId, item.RecordedAtUtc);
            if (!seen.Add(key))
            {
                skipped++;
                continue;
            }
            valid.Add(item);
        }

        if (!await IngestSourceAuthority.CanWriteAllAsync(
            db, User, valid.Select(static item => ((string?)item.StationId, (string?)"davis-vantage-pro2")), cancellationToken))
            return Forbid();

        var stationIds = valid.Select(static item => item.StationId).Distinct().ToArray();
        var timestamps = valid.Select(static item => item.RecordedAtUtc).Distinct().ToArray();
        var existing = await db.WeatherArchive.AsNoTracking()
            .Where(row => stationIds.Contains(row.StationId) && timestamps.Contains(row.RecordedAtUtc))
            .Select(static row => new { row.StationId, row.RecordedAtUtc })
            .ToListAsync(cancellationToken);
        var existingKeys = existing.Select(static row => (row.StationId, row.RecordedAtUtc)).ToHashSet();
        var toInsert = valid.Where(item => !existingKeys.Contains((item.StationId, item.RecordedAtUtc)))
            .Select(Map).ToArray();
        skipped += valid.Count - toInsert.Length;

        if (toInsert.Length > 0)
        {
            db.WeatherArchive.AddRange(toInsert);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
            {
                db.ChangeTracker.Clear();
                var insertedKeys = await db.WeatherArchive.AsNoTracking()
                    .Where(row => stationIds.Contains(row.StationId) && timestamps.Contains(row.RecordedAtUtc))
                    .Select(static row => new { row.StationId, row.RecordedAtUtc })
                    .ToListAsync(cancellationToken);
                var insertedKeySet = insertedKeys.Select(static row => (row.StationId, row.RecordedAtUtc)).ToHashSet();
                if (toInsert.All(row => insertedKeySet.Contains((row.StationId, row.RecordedAtUtc))))
                {
                    skipped += toInsert.Length;
                    toInsert = [];
                }
                else
                {
                    logger.LogError(exception, "Davis archive batch had an unresolved uniqueness race");
                    return Problem(title: "Archive Batch Ingest Failed", detail: "The archive batch could not be fully accounted. Retry is safe.", statusCode: StatusCodes.Status500InternalServerError);
                }
            }
            catch (DbUpdateException exception)
            {
                logger.LogError(exception, "Davis archive batch persistence failed for {RecordCount} records", toInsert.Length);
                return Problem(
                    title: "Archive Batch Ingest Failed",
                    detail: "The archive batch could not be persisted. Retry is safe.",
                    statusCode: StatusCodes.Status500InternalServerError);
            }
        }

        logger.LogInformation(
            "Davis archive batch: {Inserted} inserted, {Skipped} skipped, {Failed} failed",
            toInsert.Length, skipped, failures.Count);
        return StatusCode(StatusCodes.Status201Created, new WeatherArchiveBatchResponse(toInsert.Length, skipped, failures));
    }

    private static WeatherArchive Map(DavisWeatherArchivePayload item) => new()
    {
        StationId = item.StationId,
        RecordedAtUtc = item.RecordedAtUtc,
        ConsoleRecordedAtLocal = item.ConsoleRecordedAtLocal,
        ArchiveIntervalMinutes = item.ArchiveIntervalMinutes,
        TemperatureF = item.TemperatureF,
        HighTemperatureF = item.HighTemperatureF,
        LowTemperatureF = item.LowTemperatureF,
        InsideTemperatureF = item.InsideTemperatureF,
        HumidityPercent = item.HumidityPercent,
        InsideHumidityPercent = item.InsideHumidityPercent,
        BarometricPressureInHg = item.BarometricPressureInHg,
        WindSpeedMph = item.WindSpeedMph,
        WindGustMph = item.WindGustMph,
        WindDirectionDegrees = item.WindDirectionDegrees,
        WindGustDirectionDegrees = item.WindGustDirectionDegrees,
        WindSamples = item.WindSamples,
        RainfallInches = item.RainfallInches,
        RainRateInchesPerHour = item.RainRateInchesPerHour,
        SolarRadiationWm2 = item.SolarRadiationWm2,
        HighSolarRadiationWm2 = item.HighSolarRadiationWm2,
        UvIndex = item.UvIndex,
        HighUvIndex = item.HighUvIndex,
        EtInches = item.EtInches,
        ForecastRule = item.ForecastRule,
        ForecastString = item.ForecastString,
        DownloadRecordType = item.DownloadRecordType,
        LeafTemp1F = item.LeafTemp1F,
        LeafTemp2F = item.LeafTemp2F,
        LeafWetnessJson = JsonSerializer.Serialize(item.LeafWetnessScaled),
        SoilTemperaturesJson = JsonSerializer.Serialize(item.SoilTemperaturesF),
        ExtraHumiditiesJson = JsonSerializer.Serialize(item.ExtraHumiditiesPercent),
        ExtraTemperaturesJson = JsonSerializer.Serialize(item.ExtraTemperaturesF),
        SoilMoisturesJson = JsonSerializer.Serialize(item.SoilMoisturesCb),
    };

    private static bool IsUniqueConstraintViolation(DbUpdateException exception) =>
        exception.InnerException is SqlException sqlException && sqlException.Number is 2601 or 2627;
}

public sealed record WeatherArchiveBatchResponse(
    int Inserted,
    int Skipped,
    IReadOnlyList<WeatherArchiveBatchFailure> Failed);

public sealed record WeatherArchiveBatchFailure(
    string StationId,
    DateTime RecordedAtUtc,
    string Error);
