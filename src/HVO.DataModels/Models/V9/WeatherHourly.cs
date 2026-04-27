using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HVO.DataModels.Models.V9;

[Table("WeatherHourly", Schema = "v9")]
public class WeatherHourly
{
    [Key]
    public long Id { get; set; }

    public DateTime PeriodStart { get; set; }

    public double? AvgTemperatureF { get; set; }

    public double? MinTemperatureF { get; set; }

    public double? MaxTemperatureF { get; set; }

    public double? AvgHumidityPercent { get; set; }

    public double? AvgDewPointF { get; set; }

    public double? AvgBarometricPressureInHg { get; set; }

    public double? AvgWindSpeedMph { get; set; }

    public double? MaxWindGustMph { get; set; }

    public int? DominantWindDirectionDegrees { get; set; }

    public double? TotalRainfallInches { get; set; }

    public double? AvgSolarRadiationWm2 { get; set; }

    public double? MaxUvIndex { get; set; }

    public string? StationId { get; set; }
}
