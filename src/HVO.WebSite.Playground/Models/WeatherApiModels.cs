using HVO.DataModels.Models;
using HVO.DataModels.RawModels;

namespace HVO.WebSite.Playground.Models
{
    /// <summary>
    /// Response model for the latest weather record endpoint
    /// </summary>
    public class LatestWeatherResponse
    {
        /// <summary>
        /// UTC timestamp when the response was generated
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// Name of the machine that generated the response
        /// </summary>
        public string MachineName { get; set; } = string.Empty;

        /// <summary>
        /// The latest weather record data
        /// </summary>
        public DavisVantageProConsoleRecordsNew Data { get; set; } = new();
    }

    /// <summary>
    /// Response model for weather highs and lows endpoint
    /// </summary>
    public class WeatherHighsLowsResponse
    {
        /// <summary>
        /// UTC timestamp when the response was generated
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// Name of the machine that generated the response
        /// </summary>
        public string MachineName { get; set; } = string.Empty;

        /// <summary>
        /// Date range for the highs and lows data
        /// </summary>
        public DateRangeInfo DateRange { get; set; } = new();

        /// <summary>
        /// Weather highs and lows summary data
        /// </summary>
        public WeatherRecordHighLowSummary Data { get; set; } = new();
    }

    /// <summary>
    /// Date range information for weather data queries
    /// </summary>
    public class DateRangeInfo
    {
        /// <summary>
        /// Start date of the range
        /// </summary>
        public DateTimeOffset Start { get; set; }

        /// <summary>
        /// End date of the range
        /// </summary>
        public DateTimeOffset End { get; set; }
    }

    /// <summary>
    /// Response model for current weather conditions endpoint
    /// </summary>
    public class CurrentWeatherResponse
    {
        /// <summary>
        /// UTC timestamp when the response was generated
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// Name of the machine that generated the response
        /// </summary>
        public string MachineName { get; set; } = string.Empty;

        /// <summary>
        /// Current weather conditions
        /// </summary>
        public CurrentWeatherData Current { get; set; } = new();

        /// <summary>
        /// Today's extreme values (highs and lows)
        /// </summary>
        public TodaysExtremesData? TodaysExtremes { get; set; }
    }

    /// <summary>
    /// Current weather conditions data
    /// </summary>
    public class CurrentWeatherData
    {
        /// <summary>
        /// Date and time of the weather record
        /// </summary>
        public DateTimeOffset? RecordDateTime { get; set; }

        /// <summary>
        /// Outside temperature in Fahrenheit
        /// </summary>
        public decimal? OutsideTemperature { get; set; }

        /// <summary>
        /// Outside humidity percentage
        /// </summary>
        public decimal? OutsideHumidity { get; set; }

        /// <summary>
        /// Inside temperature in Fahrenheit
        /// </summary>
        public decimal? InsideTemperature { get; set; }

        /// <summary>
        /// Inside humidity percentage
        /// </summary>
        public decimal? InsideHumidity { get; set; }

        /// <summary>
        /// Wind speed in mph
        /// </summary>
        public decimal? WindSpeed { get; set; }

        /// <summary>
        /// Wind direction in degrees
        /// </summary>
        public decimal? WindDirection { get; set; }

        /// <summary>
        /// Barometric pressure in inches of mercury
        /// </summary>
        public decimal? Barometer { get; set; }

        /// <summary>
        /// Barometric pressure trend
        /// </summary>
        public short? BarometerTrend { get; set; }

        /// <summary>
        /// Current rain rate in inches per hour
        /// </summary>
        public decimal? RainRate { get; set; }

        /// <summary>
        /// Daily rain accumulation in inches
        /// </summary>
        public decimal? DailyRainAmount { get; set; }

        /// <summary>
        /// Monthly rain accumulation in inches
        /// </summary>
        public decimal? MonthlyRainAmount { get; set; }

        /// <summary>
        /// Yearly rain accumulation in inches
        /// </summary>
        public decimal? YearlyRainAmount { get; set; }

        /// <summary>
        /// UV index
        /// </summary>
        public decimal? UvIndex { get; set; }

        /// <summary>
        /// Solar radiation in watts per square meter
        /// </summary>
        public decimal? SolarRadiation { get; set; }

        /// <summary>
        /// Outside heat index in Fahrenheit
        /// </summary>
        public decimal? OutsideHeatIndex { get; set; }

        /// <summary>
        /// Outside wind chill in Fahrenheit
        /// </summary>
        public decimal? OutsideWindChill { get; set; }

        /// <summary>
        /// Outside dew point in Fahrenheit
        /// </summary>
        public decimal? OutsideDewpoint { get; set; }

        /// <summary>
        /// Sunrise time
        /// </summary>
        public TimeOnly? SunriseTime { get; set; }

        /// <summary>
        /// Sunset time
        /// </summary>
        public TimeOnly? SunsetTime { get; set; }
    }

    /// <summary>
    /// Today's weather extremes data
    /// </summary>
    public class TodaysExtremesData
    {
        /// <summary>
        /// Outside temperature extremes
        /// </summary>
        public TemperatureExtremes OutsideTemperature { get; set; } = new();

        /// <summary>
        /// Inside temperature extremes
        /// </summary>
        public TemperatureExtremes InsideTemperature { get; set; } = new();

        /// <summary>
        /// Outside humidity extremes
        /// </summary>
        public HumidityExtremes OutsideHumidity { get; set; } = new();

        /// <summary>
        /// Inside humidity extremes
        /// </summary>
        public HumidityExtremes InsideHumidity { get; set; } = new();

        /// <summary>
        /// Wind speed extremes
        /// </summary>
        public WindSpeedExtremes WindSpeed { get; set; } = new();

        /// <summary>
        /// Barometric pressure extremes
        /// </summary>
        public BarometerExtremes Barometer { get; set; } = new();

        /// <summary>
        /// Heat index extremes
        /// </summary>
        public TemperatureExtremes HeatIndex { get; set; } = new();

        /// <summary>
        /// Wind chill extremes
        /// </summary>
        public TemperatureExtremes WindChill { get; set; } = new();

        /// <summary>
        /// Dew point extremes
        /// </summary>
        public TemperatureExtremes DewPoint { get; set; } = new();

        /// <summary>
        /// Solar radiation extremes
        /// </summary>
        public SolarRadiationExtremes SolarRadiation { get; set; } = new();

        /// <summary>
        /// UV index extremes
        /// </summary>
        public UVIndexExtremes UvIndex { get; set; } = new();
    }

    /// <summary>
    /// Temperature extreme values with timestamps
    /// </summary>
    public class TemperatureExtremes
    {
        /// <summary>
        /// High temperature value
        /// </summary>
        public decimal? High { get; set; }

        /// <summary>
        /// Timestamp of high temperature
        /// </summary>
        public DateTimeOffset? HighTime { get; set; }

        /// <summary>
        /// Low temperature value
        /// </summary>
        public decimal? Low { get; set; }

        /// <summary>
        /// Timestamp of low temperature
        /// </summary>
        public DateTimeOffset? LowTime { get; set; }
    }

    /// <summary>
    /// Humidity extreme values with timestamps
    /// </summary>
    public class HumidityExtremes
    {
        /// <summary>
        /// High humidity value
        /// </summary>
        public decimal? High { get; set; }

        /// <summary>
        /// Timestamp of high humidity
        /// </summary>
        public DateTimeOffset? HighTime { get; set; }

        /// <summary>
        /// Low humidity value
        /// </summary>
        public decimal? Low { get; set; }

        /// <summary>
        /// Timestamp of low humidity
        /// </summary>
        public DateTimeOffset? LowTime { get; set; }
    }

    /// <summary>
    /// Wind speed extreme values with timestamps and directions
    /// </summary>
    public class WindSpeedExtremes
    {
        /// <summary>
        /// High wind speed value
        /// </summary>
        public decimal? High { get; set; }

        /// <summary>
        /// Timestamp of high wind speed
        /// </summary>
        public DateTimeOffset? HighTime { get; set; }

        /// <summary>
        /// Wind direction at high speed
        /// </summary>
        public decimal? HighDirection { get; set; }

        /// <summary>
        /// Low wind speed value
        /// </summary>
        public decimal? Low { get; set; }

        /// <summary>
        /// Timestamp of low wind speed
        /// </summary>
        public DateTimeOffset? LowTime { get; set; }

        /// <summary>
        /// Wind direction at low speed
        /// </summary>
        public decimal? LowDirection { get; set; }
    }

    /// <summary>
    /// Barometric pressure extreme values with timestamps
    /// </summary>
    public class BarometerExtremes
    {
        /// <summary>
        /// High barometric pressure value
        /// </summary>
        public decimal? High { get; set; }

        /// <summary>
        /// Timestamp of high barometric pressure
        /// </summary>
        public DateTimeOffset? HighTime { get; set; }

        /// <summary>
        /// Low barometric pressure value
        /// </summary>
        public decimal? Low { get; set; }

        /// <summary>
        /// Timestamp of low barometric pressure
        /// </summary>
        public DateTimeOffset? LowTime { get; set; }
    }

    /// <summary>
    /// Solar radiation extreme values with timestamps
    /// </summary>
    public class SolarRadiationExtremes
    {
        /// <summary>
        /// High solar radiation value
        /// </summary>
        public decimal? High { get; set; }

        /// <summary>
        /// Timestamp of high solar radiation
        /// </summary>
        public DateTimeOffset? HighTime { get; set; }
    }

    /// <summary>
    /// UV index extreme values with timestamps
    /// </summary>
    public class UVIndexExtremes
    {
        /// <summary>
        /// High UV index value
        /// </summary>
        public decimal? High { get; set; }

        /// <summary>
        /// Timestamp of high UV index
        /// </summary>
        public DateTimeOffset? HighTime { get; set; }
    }
}
