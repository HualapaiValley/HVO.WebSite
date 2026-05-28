using System.ComponentModel.DataAnnotations;

namespace HVO.DataModels.Models.V9;

public class PowerInverterDetailSnapshot
{
    public long Id { get; set; }

    [MaxLength(64)]
    public string SourceId { get; set; } = string.Empty;

    [MaxLength(64)]
    public string? SourceSystem { get; set; }

    [MaxLength(64)]
    public string? DeviceId { get; set; }

    public DateTime RecordedAt { get; set; }

    public int PvStringCount { get; set; }

    public int StatusCount { get; set; }

    public string PayloadJson { get; set; } = string.Empty;

    [MaxLength(64)]
    public string PayloadHash { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
}
