namespace HVO.Hardware.JkBms.Bms;

/// <summary>
/// Config settings snapshot included in an ingress record when the device configuration
/// has changed since the last successful forward. Null in steady state.
/// </summary>
public sealed class BmsConfigPayload
{
    public byte CellCount { get; init; }
    public long NominalCapacityMah { get; init; }
    public bool ChargingEnabled { get; init; }
    public bool DischargingEnabled { get; init; }
    public bool BalancingEnabled { get; init; }

    // Cell voltage protection (mV)
    public long CellOvpMv { get; init; }
    public long CellOvpRecoveryMv { get; init; }
    public long CellUvpMv { get; init; }
    public long CellUvpRecoveryMv { get; init; }

    // Balancing (mV)
    public long BalanceTriggerMv { get; init; }
    public long BalanceStartVoltageMv { get; init; }

    // Overcurrent protection
    public long ChargeOcpMa { get; init; }
    public long ChargeOcpDelayS { get; init; }
    public long ChargeOcpRecoveryS { get; init; }
    public long DischargeOcpMa { get; init; }
    public long DischargeOcpDelayS { get; init; }
    public long DischargeOcpRecoveryS { get; init; }

    // Short-circuit protection
    public long ShortCircuitDelayUs { get; init; }
    public long ShortCircuitRecoveryS { get; init; }

    // Temperature protection (°C)
    public double ChargeOtpC { get; init; }
    public double ChargeOtpRecoveryC { get; init; }
    public double ChargeUtpC { get; init; }
    public double ChargeUtpRecoveryC { get; init; }
    public double DischargeOtpC { get; init; }
    public double DischargeOtpRecoveryC { get; init; }
    public double MosOtpC { get; init; }
    public double MosOtpRecoveryC { get; init; }
}
