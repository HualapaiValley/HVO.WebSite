using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HVO.DataModels.Models.V9;

/// <summary>
/// Snapshot of BMS device information captured when firmware/hardware details change.
/// A new row is inserted only when at least one field value differs from the last snapshot.
/// </summary>
[Table("BmsDeviceInfo", Schema = "v9")]
public class BmsDeviceInfo
{
    [Key]
    public long Id { get; set; }

    public int DeviceId { get; set; }

    [ForeignKey(nameof(DeviceId))]
    public BmsDevice? Device { get; set; }

    /// <summary>UTC time this info snapshot was recorded.</summary>
    public DateTime RecordedAt { get; set; }

    [MaxLength(64)]
    public string? Manufacturer { get; set; }

    [MaxLength(32)]
    public string? Hardware { get; set; }

    [MaxLength(32)]
    public string? Firmware { get; set; }

    [MaxLength(64)]
    public string? SerialNumber { get; set; }

    [MaxLength(64)]
    public string? DeviceName { get; set; }

    [MaxLength(32)]
    public string? ManufacturingDate { get; set; }

    [MaxLength(64)]
    public string? UserData { get; set; }
}
