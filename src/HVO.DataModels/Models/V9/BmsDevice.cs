using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HVO.DataModels.Models.V9;

[Table("BmsDevice", Schema = "v9")]
public class BmsDevice
{
    [Key]
    public int Id { get; set; }

    public int? SiteId { get; set; }

    [ForeignKey(nameof(SiteId))]
    public BmsSite? Site { get; set; }

    /// <summary>Bluetooth MAC address (e.g. "C8:47:8C:EC:1B:0F").</summary>
    [MaxLength(17)]
    public string Address { get; set; } = string.Empty;

    /// <summary>Human-readable alias from configuration (e.g. "bank-2a").</summary>
    [MaxLength(100)]
    public string Alias { get; set; } = string.Empty;

    public DateTime FirstSeenAt { get; set; }

    [MaxLength(500)]
    public string? Notes { get; set; }

    public ICollection<BmsReading> Readings { get; set; } = [];
    public ICollection<BmsDeviceConfig> Configs { get; set; } = [];
    public ICollection<BmsDeviceInfo> DeviceInfos { get; set; } = [];
    public ICollection<BmsAlarm> Alarms { get; set; } = [];
}
