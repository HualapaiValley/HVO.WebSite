using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HVO.DataModels.Models.V9;

[Table("PowerConfigurationSnapshot", Schema = "v9")]
public class PowerConfigurationSnapshot
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

    public int SettingCount { get; set; }

    public int CommandCapabilityCount { get; set; }

    public string PayloadJson { get; set; } = string.Empty;

    public string PayloadHash { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
