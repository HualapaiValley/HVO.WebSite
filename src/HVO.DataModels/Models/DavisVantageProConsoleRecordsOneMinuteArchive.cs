using System;
using System.Collections.Generic;

namespace HVO.DataModels.Models;

public partial class DavisVantageProConsoleRecordsOneMinuteArchive
{
    public int Id { get; set; }

    public DateTimeOffset RecordDateTime { get; set; }

    public decimal Barometer { get; set; }

    public short BarometerTrend { get; set; }

    public decimal InsideTemperature { get; set; }

    public byte InsideHumidity { get; set; }

    public decimal? OutsideTemperature { get; set; }

    public byte? OutsideHumidity { get; set; }

    public byte? WindSpeed { get; set; }

    public short? WindDirection { get; set; }

    public byte? TenMinuteWindSpeedAverage { get; set; }

    public decimal? RainRate { get; set; }

    public byte? UvIndex { get; set; }

    public short? SolarRadiation { get; set; }

    public decimal? StormRain { get; set; }

    public DateTimeOffset? StormStartDate { get; set; }

    public decimal? DailyRainAmount { get; set; }

    public decimal? MonthlyRainAmount { get; set; }

    public decimal? YearlyRainAmount { get; set; }

    public decimal? ConsoleBatteryVoltage { get; set; }

    public short? ForcastIcons { get; set; }

    public TimeOnly? SunriseTime { get; set; }

    public TimeOnly? SunsetTime { get; set; }

    public decimal? DailyEtamount { get; set; }

    public decimal? MonthlyEtamount { get; set; }

    public decimal? YearlyEtamount { get; set; }

    public decimal? OutsideHeatIndex { get; set; }

    public decimal? OutsideWindChill { get; set; }

    public decimal? OutsideDewpoint { get; set; }
}
