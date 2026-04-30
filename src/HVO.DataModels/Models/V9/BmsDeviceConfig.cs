using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HVO.DataModels.Models.V9;

/// <summary>
/// Snapshot of BMS device configuration captured when settings change.
/// A new row is inserted only when at least one field value differs from the last snapshot.
/// </summary>
[Table("BmsDeviceConfig", Schema = "v9")]
public class BmsDeviceConfig
{
    [Key]
    public long Id { get; set; }

    public int DeviceId { get; set; }

    [ForeignKey(nameof(DeviceId))]
    public BmsDevice? Device { get; set; }

    /// <summary>UTC time this config snapshot was recorded.</summary>
    public DateTime RecordedAt { get; set; }

    // ── Cell count / capacity ─────────────────────────────────────────────────

    public byte CellCount { get; set; }

    /// <summary>Nominal battery pack capacity (mAh).</summary>
    public long NominalCapacityMah { get; set; }

    // ── Enable switches ───────────────────────────────────────────────────────

    public bool ChargingEnabled { get; set; }
    public bool DischargingEnabled { get; set; }
    public bool BalancingEnabled { get; set; }

    // ── Cell voltage protection (mV) ──────────────────────────────────────────

    public long CellOvpMv { get; set; }
    public long CellOvpRecoveryMv { get; set; }
    public long CellUvpMv { get; set; }
    public long CellUvpRecoveryMv { get; set; }

    // ── Balancing (mV) ────────────────────────────────────────────────────────

    public long BalanceTriggerMv { get; set; }
    public long BalanceStartVoltageMv { get; set; }

    // ── Overcurrent protection (mA or s) ──────────────────────────────────────

    public long ChargeOcpMa { get; set; }
    public long ChargeOcpDelayS { get; set; }
    public long ChargeOcpRecoveryS { get; set; }
    public long DischargeOcpMa { get; set; }
    public long DischargeOcpDelayS { get; set; }
    public long DischargeOcpRecoveryS { get; set; }

    // ── Short-circuit protection ──────────────────────────────────────────────

    public long ShortCircuitDelayUs { get; set; }
    public long ShortCircuitRecoveryS { get; set; }

    // ── Temperature protection (°C, stored as double) ────────────────────────

    public double ChargeOtpC { get; set; }
    public double ChargeOtpRecoveryC { get; set; }
    public double ChargeUtpC { get; set; }
    public double ChargeUtpRecoveryC { get; set; }
    public double DischargeOtpC { get; set; }
    public double DischargeOtpRecoveryC { get; set; }
    public double MosOtpC { get; set; }
    public double MosOtpRecoveryC { get; set; }
}
