using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HVO.DataModels.Models.V9;

[Table("GatewayStatusSnapshot", Schema = "v9")]
public class GatewayStatusSnapshot
{
    public long Id { get; set; }

    [MaxLength(64)]
    public string SourceId { get; set; } = string.Empty;

    [MaxLength(64)]
    public string? SourceSystem { get; set; }

    [MaxLength(64)]
    public string? DeviceId { get; set; }

    [MaxLength(64)]
    public string GatewayId { get; set; } = string.Empty;

    [MaxLength(32)]
    public string HealthState { get; set; } = string.Empty;

    [MaxLength(32)]
    public string SourceFreshnessState { get; set; } = string.Empty;

    [MaxLength(32)]
    public string RestState { get; set; } = string.Empty;

    [MaxLength(32)]
    public string? MqttState { get; set; }

    public DateTime RecordedAt { get; set; }

    public int AlertCount { get; set; }

    public int OutboxPendingCount { get; set; }

    public int OutboxFailedCount { get; set; }

    public string PayloadJson { get; set; } = string.Empty;

    [MaxLength(64)]
    public string PayloadHash { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
}
