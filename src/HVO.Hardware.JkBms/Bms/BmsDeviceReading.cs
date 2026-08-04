namespace HVO.Hardware.JkBms.Bms;

/// <summary>
/// A single complete reading from one JK BMS device.
/// This is the payload model that gets serialised to JSON and stored in the outbox.
///
/// All electrical values are in SI base units to keep the API consistent:
///   voltages → millivolts (mV)
///   current  → milliamps (mA, positive = charging; negative = discharging)
///   capacity → milliamp-hours (mAh)
///   temperature → degrees Celsius
/// </summary>
public sealed class BmsDeviceReading
{
    // ── Identity ──────────────────────────────────────────────────────────────

    /// <summary>Bluetooth MAC address of the source device.</summary>
    public string DeviceAddress { get; init; } = string.Empty;

    /// <summary>Human-readable alias from configuration.</summary>
    public string DeviceAlias { get; init; } = string.Empty;

    /// <summary>UTC time this reading was captured.</summary>
    public DateTime RecordedAtUtc { get; init; } = DateTime.UtcNow;

    // ── Cell voltages ─────────────────────────────────────────────────────────

    /// <summary>Number of active cells.</summary>
    public int CellCount { get; init; }

    /// <summary>Cell voltages (mV), indexed from cell 1.</summary>
    public IReadOnlyList<ushort> CellVoltagesMv { get; init; } = [];

    /// <summary>Per-cell internal resistance (mΩ), same indexing as <see cref="CellVoltagesMv"/>.</summary>
    public IReadOnlyList<ushort> CellResistancesMOhm { get; init; } = [];

    /// <summary>Average cell voltage (mV) as reported by BMS.</summary>
    public ushort AverageCellVoltageMv { get; init; }

    /// <summary>Spread between highest and lowest cell (mV).</summary>
    public ushort DeltaCellVoltageMv { get; init; }

    /// <summary>1-based cell number with highest voltage as reported by the BMS; 0 = none.</summary>
    public byte MaxVoltageCellIndex { get; init; }

    /// <summary>1-based cell number with lowest voltage as reported by the BMS; 0 = none.</summary>
    public byte MinVoltageCellIndex { get; init; }

    // ── Balancing ─────────────────────────────────────────────────────────────

    /// <summary>Active balancing current (mA).</summary>
    public double BalancingCurrentMa { get; init; }

    /// <summary>True while the BMS is actively balancing cells.</summary>
    public bool BalancingActive { get; init; }

    // ── Temperatures ──────────────────────────────────────────────────────────

    /// <summary>Power MOSFET temperature (°C).</summary>
    public double PowerTubeTemperatureC { get; init; }

    /// <summary>Battery temperature sensor 1 (°C).</summary>
    public double BatteryTemperature1C { get; init; }

    /// <summary>Battery temperature sensor 2 (°C).</summary>
    public double BatteryTemperature2C { get; init; }

    // ── Pack electrical ───────────────────────────────────────────────────────

    /// <summary>Total pack voltage (mV).</summary>
    public uint TotalVoltageMv { get; init; }

    /// <summary>Pack current (mA). Positive = charging. Negative = discharging.</summary>
    public int CurrentMa { get; init; }

    // ── Capacity and state ────────────────────────────────────────────────────

    /// <summary>State of charge (%).</summary>
    public ushort StateOfChargePercent { get; init; }

    /// <summary>Remaining capacity (mAh).</summary>
    public uint RemainingCapacityMah { get; init; }

    /// <summary>Nominal rated capacity (mAh).</summary>
    public uint NominalCapacityMah { get; init; }

    /// <summary>Charge/discharge cycle count.</summary>
    public uint CycleCount { get; init; }

    /// <summary>Cumulative charge capacity across all cycles (mAh).</summary>
    public uint CycleCapacityMah { get; init; }

    /// <summary>State of health (%). 100 = new.</summary>
    public ushort StateOfHealthPercent { get; init; }

    // ── Alarms ────────────────────────────────────────────────────────────────

    /// <summary>Raw alarm bitmask from BMS. Zero = no alarms.</summary>
    public uint AlarmBitmask { get; init; }

    /// <summary>True if any alarm is active.</summary>
    public bool HasAlarms => AlarmBitmask != 0;
}
