using HVO.Core.Results;
using HVO.DataModels.Data;
using HVO.DataModels.Models;
using HVO.DataModels.RawModels;
using HVO.WebSite.Playground.Models;
using Microsoft.EntityFrameworkCore;

namespace HVO.WebSite.Playground.Services;

/// <summary>
/// Service for weather data operations and business logic
/// </summary>
public class WeatherService : IWeatherService
{
    private readonly HvoDbContext _context;
    private readonly ILogger<WeatherService> _logger;

    /// <summary>
    /// Initializes a new instance of the WeatherService
    /// </summary>
    /// <param name="context">Database context for weather data access</param>
    /// <param name="logger">Logger for tracking weather service operations</param>
    public WeatherService(HvoDbContext context, ILogger<WeatherService> logger)
    {
        _context = context;
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
    public async Task<Result<CurrentWeatherResponse>> GetCurrentWeatherConditionsAsync()
    {
        try
        {
            _logger.LogInformation("Retrieving current weather conditions with today's highs/lows");

            // Get latest weather record
            var latestRecord = await _context.DavisVantageProConsoleRecordsNews
                .OrderByDescending(x => x.RecordDateTime)
                .FirstOrDefaultAsync();

            if (latestRecord == null)
            {
                _logger.LogWarning("No current weather records found");
                return new InvalidOperationException("No current weather data is available in the database");
            }

            // Get today's highs and lows
            var today = DateTimeOffset.Now.Date;
            var tomorrow = today.AddDays(1);
            var todaysHighsLows = await _context.GetWeatherRecordHighLowSummary(today, tomorrow);

            var response = new CurrentWeatherResponse
            {
                Timestamp = DateTime.UtcNow,
                MachineName = Environment.MachineName,
                Current = MapToCurrentWeatherData(latestRecord),
                TodaysExtremes = todaysHighsLows != null ? MapToTodaysExtremesData(todaysHighsLows) : null
            };

            _logger.LogInformation("Successfully retrieved current weather conditions with today's extremes");
            return Result<CurrentWeatherResponse>.Success(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving current weather conditions");
            return ex;
        }
    }

    /// <summary>
    /// Maps database weather record to current weather data model
    /// </summary>
    private static CurrentWeatherData MapToCurrentWeatherData(DavisVantageProConsoleRecordsNew record)
    {
        return new CurrentWeatherData
        {
            RecordDateTime = record.RecordDateTime,
            OutsideTemperature = record.OutsideTemperature,
            OutsideHumidity = record.OutsideHumidity,
            InsideTemperature = record.InsideTemperature,
            InsideHumidity = record.InsideHumidity,
            WindSpeed = record.WindSpeed,
            WindDirection = record.WindDirection,
            Barometer = record.Barometer,
            BarometerTrend = record.BarometerTrend,
            RainRate = record.RainRate,
            DailyRainAmount = record.DailyRainAmount,
            MonthlyRainAmount = record.MonthlyRainAmount,
            YearlyRainAmount = record.YearlyRainAmount,
            UvIndex = record.UvIndex,
            SolarRadiation = record.SolarRadiation,
            OutsideHeatIndex = record.OutsideHeatIndex,
            OutsideWindChill = record.OutsideWindChill,
            OutsideDewpoint = record.OutsideDewpoint,
            SunriseTime = record.SunriseTime,
            SunsetTime = record.SunsetTime
        };
    }

    /// <summary>
    /// Maps database highs/lows record to today's extremes data model
    /// </summary>
    private static TodaysExtremesData MapToTodaysExtremesData(WeatherRecordHighLowSummary summary)
    {
        return new TodaysExtremesData
        {
            OutsideTemperature = new TemperatureExtremes
            {
                High = summary.OutsideTemperatureHigh,
                HighTime = summary.OutsideTemperatureHighDateTime,
                Low = summary.OutsideTemperatureLow,
                LowTime = summary.OutsideTemperatureLowDateTime
            },
            InsideTemperature = new TemperatureExtremes
            {
                High = summary.InsideTemperatureHigh,
                HighTime = summary.InsideTemperatureHighDateTime,
                Low = summary.InsideTemperatureLow,
                LowTime = summary.InsideTemperatureLowDateTime
            },
            OutsideHumidity = new HumidityExtremes
            {
                High = summary.OutsideHumidityHigh,
                HighTime = summary.OutsideHumidityHighDateTime,
                Low = summary.OutsideHumidityLow,
                LowTime = summary.OutsideHumidityLowDateTime
            },
            InsideHumidity = new HumidityExtremes
            {
                High = summary.InsideHumidityHigh,
                HighTime = summary.InsideHumidityHighDateTime,
                Low = summary.InsideHumidityLow,
                LowTime = summary.InsideHumidityLowDateTime
            },
            WindSpeed = new WindSpeedExtremes
            {
                High = summary.WindSpeedHigh,
                HighTime = summary.WindSpeedHighDateTime,
                HighDirection = summary.WindSpeedHighDirection,
                Low = summary.WindSpeedLow,
                LowTime = summary.WindSpeedLowDateTime,
                LowDirection = summary.WindSpeedLowDirection
            },
            Barometer = new BarometerExtremes
            {
                High = summary.BarometerHigh,
                HighTime = summary.BarometerHighDateTime,
                Low = summary.BarometerLow,
                LowTime = summary.BarometerLowDateTime
            },
            HeatIndex = new TemperatureExtremes
            {
                High = summary.OutsideHeatIndexHigh,
                HighTime = summary.OutsideHeatIndexHighDateTime,
                Low = summary.OutsideHeatIndexLow,
                LowTime = summary.OutsideHeatIndexLowDateTime
            },
            WindChill = new TemperatureExtremes
            {
                High = summary.OutsideWindChillHigh,
                HighTime = summary.OutsideWindChillHighDateTime,
                Low = summary.OutsideWindChillLow,
                LowTime = summary.OutsideWindChillLowDateTime
            },
            DewPoint = new TemperatureExtremes
            {
                High = summary.OutsideDewpointHigh,
                HighTime = summary.OutsideDewpointHighDateTime,
                Low = summary.OutsideDewpointLow,
                LowTime = summary.OutsideDewpointLowDateTime
            },
            SolarRadiation = new SolarRadiationExtremes
            {
                High = summary.SolarRadiationHigh,
                HighTime = summary.SolarRadiationHighDateTime
            },
            UvIndex = new UVIndexExtremes
            {
                High = summary.UVIndexHigh,
                HighTime = summary.UVIndexHighDateTime
            }
        };
    }
}
