using HVO.Core.Results;
using HVO.DataModels.Data;
using HVO.DataModels.Models.V9;
using HVO.WebSite.v9.Models;
using Microsoft.EntityFrameworkCore;

namespace HVO.WebSite.v9.Services;

public class WeatherService : IWeatherService
{
    private readonly HvoDbContext _context;
    private readonly HvoV9DbContext _liveContext;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<WeatherService> _logger;

    /// <summary>
    /// Initializes a new instance of the WeatherService
    /// </summary>
    /// <param name="context">Database context for weather data access</param>
    /// <param name="liveContext">Database context for live gateway observations</param>
    /// <param name="timeProvider">Clock and server timezone for daily extremes</param>
    /// <param name="logger">Logger for tracking weather service operations</param>
    public WeatherService(
        HvoDbContext context,
        HvoV9DbContext liveContext,
        TimeProvider timeProvider,
        ILogger<WeatherService> logger)
    {
        _context = context;
        _liveContext = liveContext;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Result<LatestWeatherResponse>> GetLatestWeatherRecordAsync()
    {
        try
        {
            _logger.LogInformation("Retrieving latest weather record");

            var latestRecord = await _context.DavisVantageProConsoleRecordsNews
                .OrderByDescending(x => x.RecordDateTime)
                .FirstOrDefaultAsync();

            if (latestRecord == null)
            {
                _logger.LogWarning("No weather records found in database");
                return new InvalidOperationException("No weather records found in the database");
            }

            _logger.LogInformation("Successfully retrieved latest weather record from {DateTime}", 
                latestRecord.RecordDateTime);

            var response = new LatestWeatherResponse
            {
                Timestamp = DateTime.UtcNow,
                MachineName = Environment.MachineName,
                Data = latestRecord
            };

            return Result<LatestWeatherResponse>.Success(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving latest weather record");
            return ex;
        }
    }

    /// <inheritdoc />
    public async Task<Result<WeatherHighsLowsResponse>> GetWeatherHighsLowsAsync(DateTimeOffset? startDate, DateTimeOffset? endDate)
    {
        try
        {
            // Default to today if no dates provided
            var start = startDate ?? DateTimeOffset.Now.Date;
            var end = endDate ?? start.AddDays(1);

            _logger.LogInformation("Retrieving weather highs/lows from {StartDate} to {EndDate}", start, end);

            var summary = await _context.GetWeatherRecordHighLowSummary(start, end);

            if (summary == null)
            {
                _logger.LogWarning("No weather summary found for date range {StartDate} to {EndDate}", start, end);
                return new InvalidOperationException($"No weather data found for the specified date range: {start:yyyy-MM-dd} to {end:yyyy-MM-dd}");
            }

            _logger.LogInformation("Successfully retrieved weather highs/lows summary");

            var response = new WeatherHighsLowsResponse
            {
                Timestamp = DateTime.UtcNow,
                MachineName = Environment.MachineName,
                DateRange = new DateRangeInfo { Start = start, End = end },
                Data = summary
            };

            return Result<WeatherHighsLowsResponse>.Success(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving weather highs/lows");
            return ex;
        }
    }

    /// <inheritdoc />
    public async Task<Result<CurrentWeatherResponse>> GetCurrentWeatherConditionsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Retrieving current weather conditions with today's highs/lows");

            var latestRecord = await _liveContext.WeatherRaw
                .AsNoTracking()
                .OrderByDescending(x => x.RecordedAt)
                .ThenByDescending(x => x.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (latestRecord == null)
            {
                _logger.LogWarning("No current weather records found");
                return new InvalidOperationException("No current weather data is available in the database");
            }

            // Preserve the server-local definition of today; WeatherRaw stores UTC timestamps.
            var today = _timeProvider.GetLocalNow().Date;
            var startUtc = TimeZoneInfo.ConvertTimeToUtc(today, _timeProvider.LocalTimeZone);
            var endUtc = TimeZoneInfo.ConvertTimeToUtc(today.AddDays(1), _timeProvider.LocalTimeZone);
            var todaysReadings = _liveContext.WeatherRaw.AsNoTracking().Where(record =>
                record.StationId == latestRecord.StationId
                && record.RecordedAt >= startUtc && record.RecordedAt < endUtc);
            var todaysExtremes = await ReadTodaysExtremesAsync(todaysReadings, cancellationToken);

            var response = new CurrentWeatherResponse
            {
                Timestamp = _timeProvider.GetUtcNow().UtcDateTime,
                MachineName = Environment.MachineName,
                Current = MapToCurrentWeatherData(latestRecord),
                TodaysExtremes = todaysExtremes
            };

            _logger.LogInformation("Successfully retrieved current weather conditions with today's extremes");
            return Result<CurrentWeatherResponse>.Success(response);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving current weather conditions");
            return ex;
        }
    }

    private static CurrentWeatherData MapToCurrentWeatherData(WeatherRaw record)
    {
        return new CurrentWeatherData
        {
            RecordDateTime = AsUtc(record.RecordedAt),
            OutsideTemperature = (decimal?)record.TemperatureF,
            OutsideHumidity = (decimal?)record.HumidityPercent,
            WindSpeed = (decimal?)record.WindSpeedMph,
            WindDirection = record.WindDirectionDegrees,
            Barometer = (decimal?)record.BarometricPressureInHg,
            UvIndex = (decimal?)record.UvIndex,
            SolarRadiation = (decimal?)record.SolarRadiationWm2,
            OutsideDewpoint = (decimal?)record.DewPointF
        };
    }

    private static async Task<TodaysExtremesData?> ReadTodaysExtremesAsync(
        IQueryable<WeatherRaw> readings, CancellationToken cancellationToken)
    {
        // UNION ALL of TOP(1) queries keeps the result bounded to twelve observations.
        // Each extreme retains its acquisition time, ignores missing values, and picks
        // the earliest observation when a value occurs more than once.
        var extremes = await SelectExtremes(readings.Select(record => new ExtremeReading
            { Value = record.TemperatureF, RecordedAt = record.RecordedAt, Direction = record.WindDirectionDegrees }), WeatherMetric.Temperature)
            .Concat(SelectExtremes(readings.Select(record => new ExtremeReading
                { Value = record.HumidityPercent, RecordedAt = record.RecordedAt, Direction = record.WindDirectionDegrees }), WeatherMetric.Humidity))
            .Concat(SelectExtremes(readings.Select(record => new ExtremeReading
                { Value = record.WindSpeedMph, RecordedAt = record.RecordedAt, Direction = record.WindDirectionDegrees }), WeatherMetric.WindSpeed))
            .Concat(SelectExtremes(readings.Select(record => new ExtremeReading
                { Value = record.BarometricPressureInHg, RecordedAt = record.RecordedAt, Direction = record.WindDirectionDegrees }), WeatherMetric.Barometer))
            .Concat(SelectExtremes(readings.Select(record => new ExtremeReading
                { Value = record.DewPointF, RecordedAt = record.RecordedAt, Direction = record.WindDirectionDegrees }), WeatherMetric.DewPoint))
            .Concat(SelectExtremes(readings.Select(record => new ExtremeReading
                { Value = record.SolarRadiationWm2, RecordedAt = record.RecordedAt, Direction = record.WindDirectionDegrees }), WeatherMetric.SolarRadiation, highOnly: true))
            .Concat(SelectExtremes(readings.Select(record => new ExtremeReading
                { Value = record.UvIndex, RecordedAt = record.RecordedAt, Direction = record.WindDirectionDegrees }), WeatherMetric.UvIndex, highOnly: true))
            .ToListAsync(cancellationToken);

        if (extremes.Count == 0)
            return null;

        ExtremeReading? Find(WeatherMetric metric, bool isHigh) =>
            extremes.FirstOrDefault(reading => reading.Metric == metric && reading.IsHigh == isHigh);
        decimal? Value(WeatherMetric metric, bool isHigh) => (decimal?)Find(metric, isHigh)?.Value;
        DateTimeOffset? At(WeatherMetric metric, bool isHigh) =>
            Find(metric, isHigh) is { } reading ? AsUtc(reading.RecordedAt) : null;

        return new TodaysExtremesData
        {
            OutsideTemperature = new TemperatureExtremes
            {
                High = Value(WeatherMetric.Temperature, true),
                HighTime = At(WeatherMetric.Temperature, true),
                Low = Value(WeatherMetric.Temperature, false),
                LowTime = At(WeatherMetric.Temperature, false)
            },
            OutsideHumidity = new HumidityExtremes
            {
                High = Value(WeatherMetric.Humidity, true),
                HighTime = At(WeatherMetric.Humidity, true),
                Low = Value(WeatherMetric.Humidity, false),
                LowTime = At(WeatherMetric.Humidity, false)
            },
            WindSpeed = new WindSpeedExtremes
            {
                High = Value(WeatherMetric.WindSpeed, true),
                HighTime = At(WeatherMetric.WindSpeed, true),
                HighDirection = Find(WeatherMetric.WindSpeed, true)?.Direction,
                Low = Value(WeatherMetric.WindSpeed, false),
                LowTime = At(WeatherMetric.WindSpeed, false),
                LowDirection = Find(WeatherMetric.WindSpeed, false)?.Direction
            },
            Barometer = new BarometerExtremes
            {
                High = Value(WeatherMetric.Barometer, true),
                HighTime = At(WeatherMetric.Barometer, true),
                Low = Value(WeatherMetric.Barometer, false),
                LowTime = At(WeatherMetric.Barometer, false)
            },
            DewPoint = new TemperatureExtremes
            {
                High = Value(WeatherMetric.DewPoint, true),
                HighTime = At(WeatherMetric.DewPoint, true),
                Low = Value(WeatherMetric.DewPoint, false),
                LowTime = At(WeatherMetric.DewPoint, false)
            },
            SolarRadiation = new SolarRadiationExtremes
            {
                High = Value(WeatherMetric.SolarRadiation, true),
                HighTime = At(WeatherMetric.SolarRadiation, true)
            },
            UvIndex = new UVIndexExtremes
            {
                High = Value(WeatherMetric.UvIndex, true),
                HighTime = At(WeatherMetric.UvIndex, true)
            }
        };
    }

    private static IQueryable<ExtremeReading> SelectExtremes(
        IQueryable<ExtremeReading> readings, WeatherMetric metric, bool highOnly = false)
    {
        var available = readings.Where(reading => reading.Value != null);
        var high = available.OrderByDescending(reading => reading.Value).ThenBy(reading => reading.RecordedAt)
            .Take(1).Select(reading => new ExtremeReading
            {
                Metric = metric, IsHigh = true, Value = reading.Value,
                RecordedAt = reading.RecordedAt, Direction = reading.Direction
            });
        if (highOnly)
            return high;

        return high.Concat(available.OrderBy(reading => reading.Value).ThenBy(reading => reading.RecordedAt)
                .Take(1).Select(reading => new ExtremeReading
                {
                    Metric = metric, IsHigh = false, Value = reading.Value,
                    RecordedAt = reading.RecordedAt, Direction = reading.Direction
                }));
    }

    private static DateTimeOffset AsUtc(DateTime recordedAt) =>
        new(DateTime.SpecifyKind(recordedAt, DateTimeKind.Utc));

    private enum WeatherMetric { Temperature, Humidity, WindSpeed, Barometer, DewPoint, SolarRadiation, UvIndex }

    private sealed class ExtremeReading
    {
        public WeatherMetric Metric { get; init; }
        public bool IsHigh { get; init; }
        public double? Value { get; init; }
        public DateTime RecordedAt { get; init; }
        public int? Direction { get; init; }
    }
}

