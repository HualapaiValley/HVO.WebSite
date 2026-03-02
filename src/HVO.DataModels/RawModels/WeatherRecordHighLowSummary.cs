namespace HVO.DataModels.RawModels;

public class WeatherRecordHighLowSummary
    {
        public DateTimeOffset StartRecordDateTime { get; set; }

        public DateTimeOffset EndRecordDateTime { get; set; }

        public decimal BarometerHigh { get; set; }
        public decimal BarometerLow { get; set; }
        public DateTimeOffset BarometerHighDateTime { get; set; }
        public DateTimeOffset BarometerLowDateTime { get; set; }

        public decimal InsideTemperatureHigh { get; set; }
        public decimal InsideTemperatureLow { get; set; }
        public DateTimeOffset InsideTemperatureHighDateTime { get; set; }
        public DateTimeOffset InsideTemperatureLowDateTime { get; set; }

        public decimal? OutsideTemperatureHigh { get; set; }
        public decimal? OutsideTemperatureLow { get; set; }
        public DateTimeOffset OutsideTemperatureHighDateTime { get; set; }
        public DateTimeOffset OutsideTemperatureLowDateTime { get; set; }

        public byte InsideHumidityHigh { get; set; }
        public byte InsideHumidityLow { get; set; }
        public DateTimeOffset InsideHumidityHighDateTime { get; set; }
        public DateTimeOffset InsideHumidityLowDateTime { get; set; }

        public byte? OutsideHumidityHigh { get; set; }
        public byte? OutsideHumidityLow { get; set; }
        public DateTimeOffset OutsideHumidityHighDateTime { get; set; }
        public DateTimeOffset OutsideHumidityLowDateTime { get; set; }

        public byte? WindSpeedHigh { get; set; }
        public byte? WindSpeedLow { get; set; }
        public DateTimeOffset WindSpeedHighDateTime { get; set; }
        public DateTimeOffset WindSpeedLowDateTime { get; set; }
        public short? WindSpeedHighDirection { get; set; }
        public short? WindSpeedLowDirection { get; set; }

        public short? SolarRadiationHigh { get; set; }
        public DateTimeOffset SolarRadiationHighDateTime { get; set; }

        public byte? UVIndexHigh { get; set; }
        public DateTimeOffset UVIndexHighDateTime { get; set; }

        public decimal? OutsideHeatIndexHigh { get; set; }
        public decimal? OutsideHeatIndexLow { get; set; }
        public DateTimeOffset OutsideHeatIndexHighDateTime { get; set; }
        public DateTimeOffset OutsideHeatIndexLowDateTime { get; set; }

        public decimal? OutsideWindChillHigh { get; set; }
        public decimal? OutsideWindChillLow { get; set; }
        public DateTimeOffset OutsideWindChillHighDateTime { get; set; }
        public DateTimeOffset OutsideWindChillLowDateTime { get; set; }

        public decimal? OutsideDewpointHigh { get; set; }
        public decimal? OutsideDewpointLow { get; set; }
        public DateTimeOffset OutsideDewpointHighDateTime { get; set; }
        public DateTimeOffset OutsideDewpointLowDateTime { get; set; }
    }
