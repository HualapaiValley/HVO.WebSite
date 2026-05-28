using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HVO.DataModels.Models.V9;

[Table("PowerDeviceInventorySnapshot", Schema = "v9")]
public class PowerDeviceInventorySnapshot
{
    [Key]
    public long Id { get; set; }

    [MaxLength(64)]
    public string SourceId { get; set; } = string.Empty;

    [MaxLength(64)]
    public string? SourceSystem { get; set; }

    [MaxLength(64)]
    public string? DeviceId { get; set; }

    public DateTime RecordedAt { get; set; }

    public int RestMetricCount { get; set; }

    public int MqttEntityCount { get; set; }

    public int MqttStateTopicCount { get; set; }

    public string PayloadJson { get; set; } = string.Empty;

    public string PayloadHash { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
