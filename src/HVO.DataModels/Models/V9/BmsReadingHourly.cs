using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HVO.DataModels.Models.V9;

/// <summary>
/// Per-hour aggregation of BMS pack-level readings.
/// Same shape as <see cref="BmsReadingMinute"/> but over a 1-hour window.
/// </summary>
[Table("BmsReadingHourly", Schema = "v9")]
public class BmsReadingHourly
{
    [Key]
    public long Id { get; set; }

    public int DeviceId { get; set; }

    [ForeignKey(nameof(DeviceId))]
    public BmsDevice? Device { get; set; }

    /// <summary>Start of the 1-hour bucket (truncated to the hour, UTC).</summary>
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
