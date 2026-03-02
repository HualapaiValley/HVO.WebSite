using System;

namespace HVO.DataModels.RawModels;

public class DavisVantageProAverage
{
    public DateTimeOffset RecordDateTime { get; set; }
    public decimal? Barometer { get; set; }
    public decimal? InsideTemperature { get; set; }
    public byte? InsideHumidity { get; set; }
    public decimal? OutsideTemperature { get; set; }
    public byte? OutsideHumidity { get; set; }
    public byte? WindSpeed { get; set; }
    public byte? WindSpeedLow { get; set; }
    public byte? WindSpeedHigh { get; set; }
    public short? WindDirection { get; set; }
    public short? SolarRadiation { get; set; }
    public decimal? OutsideDewpoint { get; set; }
}
