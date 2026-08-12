using System.ComponentModel.DataAnnotations;

namespace HVO.DataModels.Models.V9;

public sealed class SmartShuntDetailSnapshot
{
    public long Id { get; set; }
    [MaxLength(64)] public string SourceId { get; set; } = string.Empty;
    [MaxLength(64)] public string? SourceSystem { get; set; }
    [MaxLength(64)] public string? DeviceId { get; set; }
    public DateTime RecordedAt { get; set; }
    public double? ConsumedAh { get; set; }
    public double? RemainingMinutes { get; set; }
    public double? StarterVoltageV { get; set; }
    public double? TemperatureC { get; set; }
    public DateTime CreatedAt { get; set; }
}
