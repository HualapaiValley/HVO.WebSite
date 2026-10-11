namespace HVO.WebSite.v9.Models;

/// <summary>Compatible highs/lows JSON fields; unavailable values and their times stay null.</summary>
public sealed class WeatherHighsLowsData
{
    public DateTimeOffset StartRecordDateTime { get; init; }
    public DateTimeOffset EndRecordDateTime { get; init; }
    public decimal? BarometerHigh { get; init; }
    public decimal? BarometerLow { get; init; }
    public DateTimeOffset? BarometerHighDateTime { get; init; }
    public DateTimeOffset? BarometerLowDateTime { get; init; }
    public decimal? InsideTemperatureHigh { get; init; }
    public decimal? InsideTemperatureLow { get; init; }
    public DateTimeOffset? InsideTemperatureHighDateTime { get; init; }
    public DateTimeOffset? InsideTemperatureLowDateTime { get; init; }
    public decimal? OutsideTemperatureHigh { get; init; }
    public decimal? OutsideTemperatureLow { get; init; }
    public DateTimeOffset? OutsideTemperatureHighDateTime { get; init; }
    public DateTimeOffset? OutsideTemperatureLowDateTime { get; init; }
    public decimal? InsideHumidityHigh { get; init; }
    public decimal? InsideHumidityLow { get; init; }
    public DateTimeOffset? InsideHumidityHighDateTime { get; init; }
    public DateTimeOffset? InsideHumidityLowDateTime { get; init; }
    public decimal? OutsideHumidityHigh { get; init; }
    public decimal? OutsideHumidityLow { get; init; }
    public DateTimeOffset? OutsideHumidityHighDateTime { get; init; }
    public DateTimeOffset? OutsideHumidityLowDateTime { get; init; }
    public decimal? WindSpeedHigh { get; init; }
    public decimal? WindSpeedLow { get; init; }
    public DateTimeOffset? WindSpeedHighDateTime { get; init; }
    public DateTimeOffset? WindSpeedLowDateTime { get; init; }
    public decimal? WindSpeedHighDirection { get; init; }
    public decimal? WindSpeedLowDirection { get; init; }
    public decimal? SolarRadiationHigh { get; init; }
    public DateTimeOffset? SolarRadiationHighDateTime { get; init; }
    public decimal? UVIndexHigh { get; init; }
    public DateTimeOffset? UVIndexHighDateTime { get; init; }
    public decimal? OutsideHeatIndexHigh { get; init; }
    public decimal? OutsideHeatIndexLow { get; init; }
    public DateTimeOffset? OutsideHeatIndexHighDateTime { get; init; }
    public DateTimeOffset? OutsideHeatIndexLowDateTime { get; init; }
    public decimal? OutsideWindChillHigh { get; init; }
    public decimal? OutsideWindChillLow { get; init; }
    public DateTimeOffset? OutsideWindChillHighDateTime { get; init; }
    public DateTimeOffset? OutsideWindChillLowDateTime { get; init; }
    public decimal? OutsideDewpointHigh { get; init; }
    public decimal? OutsideDewpointLow { get; init; }
    public DateTimeOffset? OutsideDewpointHighDateTime { get; init; }
    public DateTimeOffset? OutsideDewpointLowDateTime { get; init; }
}
