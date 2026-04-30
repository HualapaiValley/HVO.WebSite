using System.ComponentModel.DataAnnotations.Schema;

namespace HVO.DataModels.Models.V9;

/// <summary>
/// Alarm lifecycle record. A row is created when a new alarm bit activates (AlarmBitmask 0→1)
/// and updated (ClearedAt set) when that bit returns to 0.
/// </summary>
[Table("BmsAlarm", Schema = "v9")]
public class BmsAlarm
{
    public long Id { get; set; }

    public int DeviceId { get; set; }

    [ForeignKey(nameof(DeviceId))]
    public BmsDevice? Device { get; set; }

    /// <summary>The bitmask value that was active when this alarm opened.</summary>
    public long AlarmBitmask { get; set; }

    /// <summary>UTC time the alarm was first detected.</summary>
    public DateTime ActivatedAt { get; set; }

    /// <summary>UTC time the alarm cleared. Null means still active.</summary>
    public DateTime? ClearedAt { get; set; }
}
