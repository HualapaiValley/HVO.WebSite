using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HVO.DataModels.Models.V9;

/// <summary>
/// Pack-level reading from a BMS device. One row per poll per device (~30-second interval).
/// Per-cell voltages and resistances are stored in child tables <see cref="BmsCellVoltage"/>
/// and <see cref="BmsCellResistance"/>.
/// </summary>
[Table("BmsReading", Schema = "v9")]
public class BmsReading
{
    [Key]
    public long Id { get; set; }

    public int DeviceId { get; set; }

    [ForeignKey(nameof(DeviceId))]
    public BmsDevice? Device { get; set; }

    /// <summary>UTC time this reading was captured at the source device.</summary>
    public DateTime RecordedAt { get; set; }

    // ── Pack electrical ───────────────────────────────────────────────────────

    /// <summary>Total pack voltage (mV).</summary>
    public long PackVoltageMv { get; set; }

    /// <summary>Pack current (mA). Positive = discharging, negative = charging.</summary>
    public int CurrentMa { get; set; }

    /// <summary>Instantaneous power (W). Positive = discharging, negative = charging.</summary>
    public double PowerWatts { get; set; }

    // ── State ─────────────────────────────────────────────────────────────────

    public byte SocPercent { get; set; }
    public byte SohPercent { get; set; }

    // ── Capacity ──────────────────────────────────────────────────────────────

    public long RemainingCapacityMah { get; set; }
    public long NominalCapacityMah { get; set; }
    public long CycleCount { get; set; }
    public long CycleCapacityMah { get; set; }

    // ── Temperatures (°C) ─────────────────────────────────────────────────────

    public double BatteryTemp1C { get; set; }
    public double BatteryTemp2C { get; set; }
    public double PowerTubeC { get; set; }

    // ── Balancing ─────────────────────────────────────────────────────────────

    public bool BalancingActive { get; set; }
    public double BalancingCurrentMa { get; set; }

    // ── Cell delta ────────────────────────────────────────────────────────────

    /// <summary>Spread between highest and lowest cell voltage (mV).</summary>
    public int DeltaCellVoltageMv { get; set; }

    // ── Alarms ────────────────────────────────────────────────────────────────

    /// <summary>Raw alarm bitmask. Zero = no alarms.</summary>
    public long AlarmBitmask { get; set; }

    public ICollection<BmsCellVoltage> CellVoltages { get; set; } = [];
    public ICollection<BmsCellResistance> CellResistances { get; set; } = [];
}
