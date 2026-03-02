using HVO.DataModels.Models;
using HVO.DataModels.RawModels;
using HVO.WebSite.Playground.Models;

namespace HVO.WebSite.Playground.Tests.TestHelpers;

/// <summary>
/// Builder class for creating test data objects used in weather service and controller tests
/// </summary>
public static class WeatherTestDataBuilder
{
    /// <summary>
    /// Creates a sample DavisVantageProConsoleRecordsNew for testing
    /// </summary>
    public static DavisVantageProConsoleRecordsNew CreateSampleWeatherRecord(DateTimeOffset? recordDateTime = null)
    {
        return new DavisVantageProConsoleRecordsNew
        {
            Id = 1,
            RecordDateTime = recordDateTime ?? DateTimeOffset.UtcNow,
            OutsideTemperature = 75.5m,
            OutsideHumidity = 45,
            InsideTemperature = 72.0m,
            InsideHumidity = 40,
            WindSpeed = 5,
            WindDirection = 180,
            Barometer = 30.12m,
            BarometerTrend = 0,
            RainRate = 0.0m,
            DailyRainAmount = 0.25m,
            MonthlyRainAmount = 2.5m,
            YearlyRainAmount = 15.75m,
            UvIndex = 3,
            SolarRadiation = 450,
            OutsideHeatIndex = 76.8m,
            OutsideWindChill = 74.2m,
            OutsideDewpoint = 52.3m,
            SunriseTime = new TimeOnly(6, 30),
            SunsetTime = new TimeOnly(19, 45)
        };
    }

    /// <summary>
    /// Creates a sample WeatherRecordHighLowSummary for testing
    /// </summary>
    public static WeatherRecordHighLowSummary CreateSampleHighsLowsSummary(DateTimeOffset? date = null)
    {
        var baseDate = date ?? DateTimeOffset.UtcNow.Date;
        
        return new WeatherRecordHighLowSummary
        {
            OutsideTemperatureHigh = 85.5m,
            OutsideTemperatureHighDateTime = baseDate.AddHours(14),
            OutsideTemperatureLow = 65.2m,
            OutsideTemperatureLowDateTime = baseDate.AddHours(6),
            
            InsideTemperatureHigh = 75.0m,
            InsideTemperatureHighDateTime = baseDate.AddHours(15),
            InsideTemperatureLow = 68.5m,
            InsideTemperatureLowDateTime = baseDate.AddHours(7),
            
            OutsideHumidityHigh = 85,
            OutsideHumidityHighDateTime = baseDate.AddHours(7),
            OutsideHumidityLow = 35,
            OutsideHumidityLowDateTime = baseDate.AddHours(14),
            
            InsideHumidityHigh = 45,
            InsideHumidityHighDateTime = baseDate.AddHours(8),
            InsideHumidityLow = 38,
            InsideHumidityLowDateTime = baseDate.AddHours(16),
            
            WindSpeedHigh = 12,
            WindSpeedHighDateTime = baseDate.AddHours(13),
            WindSpeedHighDirection = 225,
            WindSpeedLow = 0,
            WindSpeedLowDateTime = baseDate.AddHours(4),
            WindSpeedLowDirection = 0,
            
            BarometerHigh = 30.25m,
            BarometerHighDateTime = baseDate.AddHours(10),
            BarometerLow = 29.95m,
            BarometerLowDateTime = baseDate.AddHours(16),
            
            OutsideHeatIndexHigh = 88.2m,
            OutsideHeatIndexHighDateTime = baseDate.AddHours(14),
            OutsideHeatIndexLow = 65.2m,
            OutsideHeatIndexLowDateTime = baseDate.AddHours(6),
            
            OutsideWindChillHigh = 75.0m,
            OutsideWindChillHighDateTime = baseDate.AddHours(15),
            OutsideWindChillLow = 62.8m,
            OutsideWindChillLowDateTime = baseDate.AddHours(5),
            
            OutsideDewpointHigh = 58.7m,
            OutsideDewpointHighDateTime = baseDate.AddHours(8),
            OutsideDewpointLow = 45.3m,
            OutsideDewpointLowDateTime = baseDate.AddHours(14),
            
            SolarRadiationHigh = 950,
            SolarRadiationHighDateTime = baseDate.AddHours(12),
            
            UVIndexHigh = 8,
            UVIndexHighDateTime = baseDate.AddHours(12)
        };
    }

    /// <summary>
    /// Creates a sample LatestWeatherResponse for testing
    /// </summary>
    public static LatestWeatherResponse CreateSampleLatestWeatherResponse()
    {
        return new LatestWeatherResponse
        {
            Timestamp = DateTime.UtcNow,
            MachineName = "TEST-MACHINE",
            Data = CreateSampleWeatherRecord()
        };
    }

    /// <summary>
    /// Creates a sample WeatherHighsLowsResponse for testing
    /// </summary>
    public static WeatherHighsLowsResponse CreateSampleHighsLowsResponse(DateTimeOffset? startDate = null, DateTimeOffset? endDate = null)
    {
        var start = startDate ?? DateTimeOffset.UtcNow.Date;
        var end = endDate ?? start.AddDays(1);
        
        return new WeatherHighsLowsResponse
        {
            Timestamp = DateTime.UtcNow,
            MachineName = "TEST-MACHINE",
            DateRange = new DateRangeInfo { Start = start, End = end },
            Data = CreateSampleHighsLowsSummary(start)
        };
    }

    /// <summary>
    /// Creates a sample CurrentWeatherResponse for testing
    /// </summary>
    public static CurrentWeatherResponse CreateSampleCurrentWeatherResponse()
    {
        var record = CreateSampleWeatherRecord();
        var summary = CreateSampleHighsLowsSummary();
        
        return new CurrentWeatherResponse
        {
            Timestamp = DateTime.UtcNow,
            MachineName = "TEST-MACHINE",
            Current = new CurrentWeatherData
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
            },
            TodaysExtremes = new TodaysExtremesData
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
            }
        };
    }
}
