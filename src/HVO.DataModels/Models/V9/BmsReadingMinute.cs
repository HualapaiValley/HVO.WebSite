using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HVO.DataModels.Models.V9;

/// <summary>
/// Per-minute aggregation of BMS pack-level readings.
/// Computed from <see cref="BmsReading"/> rows within the minute window.
/// </summary>
[Table("BmsReadingMinute", Schema = "v9")]
public class BmsReadingMinute
{
    [Key]
    public long Id { get; set; }

    public int DeviceId { get; set; }

    [ForeignKey(nameof(DeviceId))]
    public BmsDevice? Device { get; set; }

    /// <summary>Start of the 1-minute bucket (truncated to the minute, UTC).</summary>
    public DateTime PeriodStart { get; set; }

    // ── Pack voltage ──────────────────────────────────────────────────────────

    public long AvgPackVoltageMv { get; set; }

    // ── Current ───────────────────────────────────────────────────────────────

    public int AvgCurrentMa { get; set; }
    public int MaxCurrentMa { get; set; }
    public int MinCurrentMa { get; set; }

    // ── Power ─────────────────────────────────────────────────────────────────

    public double AvgPowerWatts { get; set; }
    public double MaxPowerWatts { get; set; }

    // ── SOC ───────────────────────────────────────────────────────────────────

    public double AvgSocPercent { get; set; }
    public double MinSocPercent { get; set; }

    // ── Temperatures ──────────────────────────────────────────────────────────

    public double MaxBatteryTemp1C { get; set; }
    public double MaxBatteryTemp2C { get; set; }
    public double MaxPowerTubeC { get; set; }

    // ── Cell delta ────────────────────────────────────────────────────────────

    public int MaxDeltaCellMv { get; set; }

    // ── Alarms ────────────────────────────────────────────────────────────────

    public int AlarmCount { get; set; }
}
