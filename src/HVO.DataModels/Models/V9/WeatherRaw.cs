using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HVO.DataModels.Models.V9;

[Table("WeatherRaw", Schema = "v9")]
public class WeatherRaw
{
    [Key]
    public long Id { get; set; }

    public DateTime RecordedAt { get; set; }

    public double? TemperatureF { get; set; }

    public double? HumidityPercent { get; set; }

    public double? DewPointF { get; set; }

    public double? BarometricPressureInHg { get; set; }

    public double? WindSpeedMph { get; set; }

    public double? WindGustMph { get; set; }

    public int? WindDirectionDegrees { get; set; }

    public double? RainfallInches { get; set; }

    public double? SolarRadiationWm2 { get; set; }

    public double? UvIndex { get; set; }

    public string? StationId { get; set; }
}
