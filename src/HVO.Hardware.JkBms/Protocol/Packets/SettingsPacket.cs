namespace HVO.Hardware.JkBms.Protocol.Packets;

/// <summary>
/// Parsed settings/configuration response from a JK BMS unit (frame type 0x01).
///
/// The BMS pushes this frame spontaneously on every new BLE connection before responding
/// to any command. It contains all user-configurable protection thresholds and device
/// configuration parameters.
///
/// Field layout (offsets relative to the data section — byte 6 of the full 300-byte frame).
/// All multi-byte integers are little-endian unless noted.
/// Voltage values are stored in mV (raw U32 LE; multiply by 0.001 to get volts).
/// Temperature values are stored as × 0.1 °C (raw I32 LE; divide by 10 to get °C).
/// Current values are stored in mA (raw U32 LE; multiply by 0.001 to get amperes).
///
/// Based on the esphome-jk-bms reference implementation, decode_jk02_settings_().
/// Absolute esphome frame offsets = our data section offset + 6.
/// </summary>
public sealed class SettingsPacket
{
    // ── Cell voltage protection ───────────────────────────────────────────────

    /// <summary>Cell overvoltage protection threshold (mV). Charging stops above this.</summary>
    public uint CellOvervoltageProtectionMv { get; init; }

    /// <summary>Cell overvoltage recovery threshold (mV). Must be below OVP.</summary>
    public uint CellOvervoltageRecoveryMv { get; init; }

    /// <summary>Cell undervoltage protection threshold (mV). Discharging stops below this.</summary>
    public uint CellUndervoltageProtectionMv { get; init; }

    /// <summary>Cell undervoltage recovery threshold (mV). Must be above UVP.</summary>
    public uint CellUndervoltageRecoveryMv { get; init; }

    // ── Balancing ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Balance pressure-difference trigger voltage (mV).
    /// Balancing activates when the cell voltage spread exceeds this value.
    /// Shown as "Balance Opening Pressure Difference" in the app.
    /// </summary>
    public uint BalancePressureDifferenceMv { get; init; }

    /// <summary>
    /// Minimum cell voltage required before balancing can start (mV).
    /// Shown as "Balance Starting Voltage" in the app.
    /// </summary>
    public uint BalanceStartingVoltageMv { get; init; }

    /// <summary>Whether the balancer is enabled.</summary>
    public bool BalancingEnabled { get; init; }

    // ── Overcurrent protection ────────────────────────────────────────────────

    /// <summary>Maximum charge current protection threshold (mA).</summary>
    public uint ChargingOvercurrentProtectionMa { get; init; }

    /// <summary>Delay before charging overcurrent protection trips (seconds).</summary>
    public uint ChargingOvercurrentProtectionDelayS { get; init; }

    /// <summary>Charging overcurrent protection recovery time (seconds).</summary>
    public uint ChargingOvercurrentProtectionRecoveryS { get; init; }

    /// <summary>Maximum discharge current protection threshold (mA).</summary>
    public uint DischargingOvercurrentProtectionMa { get; init; }

    /// <summary>Delay before discharging overcurrent protection trips (seconds).</summary>
    public uint DischargingOvercurrentProtectionDelayS { get; init; }

    /// <summary>Discharging overcurrent protection recovery time (seconds).</summary>
    public uint DischargingOvercurrentProtectionRecoveryS { get; init; }

    /// <summary>Short-circuit protection recovery time (seconds).</summary>
    public uint ShortCircuitProtectionRecoveryS { get; init; }

    /// <summary>Short-circuit protection delay (microseconds).</summary>
    public uint ShortCircuitProtectionDelayUs { get; init; }

    // ── Temperature protection ────────────────────────────────────────────────

    /// <summary>Charging high-temperature protection (°C).</summary>
    public double ChargingOvertemperatureProtectionC { get; init; }

    /// <summary>Charging high-temperature protection recovery (°C).</summary>
    public double ChargingOvertemperatureRecoveryC { get; init; }

    /// <summary>Charging low-temperature protection (°C). Usually negative.</summary>
    public double ChargingUndertemperatureProtectionC { get; init; }

    /// <summary>Charging low-temperature protection recovery (°C). Usually negative.</summary>
    public double ChargingUndertemperatureRecoveryC { get; init; }

    /// <summary>Discharging high-temperature protection (°C).</summary>
    public double DischargingOvertemperatureProtectionC { get; init; }

    /// <summary>Discharging high-temperature protection recovery (°C).</summary>
    public double DischargingOvertemperatureRecoveryC { get; init; }

    /// <summary>Power tube (MOSFET) over-temperature protection (°C).</summary>
    public double PowerTubeOvertemperatureProtectionC { get; init; }

    /// <summary>Power tube over-temperature protection recovery (°C).</summary>
    public double PowerTubeOvertemperatureRecoveryC { get; init; }

    // ── Device configuration ──────────────────────────────────────────────────

    /// <summary>Number of battery cell strings (cell count).</summary>
    public byte CellCount { get; init; }

    /// <summary>Nominal battery pack capacity (mAh).</summary>
    public uint NominalCapacityMah { get; init; }

    /// <summary>Whether charging is enabled (MOSFET on).</summary>
    public bool ChargingEnabled { get; init; }

    /// <summary>Whether discharging is enabled (MOSFET on).</summary>
    public bool DischargingEnabled { get; init; }

    // ── Timestamp ─────────────────────────────────────────────────────────────

    /// <summary>UTC time this packet was parsed.</summary>
    public DateTime RecordedAtUtc { get; init; } = DateTime.UtcNow;

    // ── Factory ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Parse a <see cref="SettingsPacket"/> from the data section of a settings frame (type 0x01).
    /// The <paramref name="data"/> span must cover bytes 6–298 of the complete 300-byte frame
    /// (i.e. the 293-byte payload returned by <see cref="JkBmsProtocol.GetData"/>).
    ///
    /// All offsets below are relative to the start of the data section (frame byte 6).
    /// Corresponding absolute esphome-jk-bms frame offsets = our offset + 6.
    /// </summary>
    /// <exception cref="JkBmsFrameException">
    /// Thrown if <paramref name="data"/> is too short.
    /// </exception>
    public static SettingsPacket Parse(ReadOnlySpan<byte> data)
    {
        // Minimum length: BalanceStartingVoltage ends at 0x87 (inclusive) = 0x88 bytes
        const int minLength = 0x88;
        if (data.Length < minLength)
            throw new JkBmsFrameException(
                $"Settings data section is {data.Length} bytes; expected at least {minLength}.");

        return new SettingsPacket
        {
            // ── Cell voltage protection (U32 LE, mV) ─────────────────────────────
            // esphome abs 10-13  → our 0x04-0x07  Cell UVP
            // esphome abs 14-17  → our 0x08-0x0B  Cell UVPR
            // esphome abs 18-21  → our 0x0C-0x0F  Cell OVP
            // esphome abs 22-25  → our 0x10-0x13  Cell OVPR
            CellUndervoltageProtectionMv  = ReadU32Le(data, 0x04),
            CellUndervoltageRecoveryMv    = ReadU32Le(data, 0x08),
            CellOvervoltageProtectionMv   = ReadU32Le(data, 0x0C),
            CellOvervoltageRecoveryMv     = ReadU32Le(data, 0x10),

            // ── Balancing ─────────────────────────────────────────────────────────
            // esphome abs 26-29  → our 0x14-0x17  Balance trigger voltage (pressure diff)
            // esphome abs 138-141 → our 0x84-0x87  Start balance voltage
            // esphome abs 126    → our 0x78        Balancer switch
            BalancePressureDifferenceMv   = ReadU32Le(data, 0x14),
            BalanceStartingVoltageMv      = ReadU32Le(data, 0x84),
            BalancingEnabled              = data[0x78] != 0,

            // ── Overcurrent protection (U32 LE, mA or s) ─────────────────────────
            // esphome abs 50-53  → our 0x2C-0x2F  Max charge current (mA)
            // esphome abs 54-57  → our 0x30-0x33  Charge OCP delay (s)
            // esphome abs 58-61  → our 0x34-0x37  Charge OCP recovery time (s)
            // esphome abs 62-65  → our 0x38-0x3B  Max discharge current (mA)
            // esphome abs 66-69  → our 0x3C-0x3F  Discharge OCP delay (s)
            // esphome abs 70-73  → our 0x40-0x43  Discharge OCP recovery time (s)
            // esphome abs 74-77  → our 0x44-0x47  Short-circuit protection recovery (s)
            ChargingOvercurrentProtectionMa         = ReadU32Le(data, 0x2C),
            ChargingOvercurrentProtectionDelayS     = ReadU32Le(data, 0x30),
            ChargingOvercurrentProtectionRecoveryS  = ReadU32Le(data, 0x34),
            DischargingOvercurrentProtectionMa      = ReadU32Le(data, 0x38),
            DischargingOvercurrentProtectionDelayS  = ReadU32Le(data, 0x3C),
            DischargingOvercurrentProtectionRecoveryS = ReadU32Le(data, 0x40),
            ShortCircuitProtectionRecoveryS         = ReadU32Le(data, 0x44),

            // ── Temperature protection (I32 LE, × 0.1 °C) ──────────────────────── (I32 LE, × 0.1 °C) ────────────────────────
            // esphome abs 82-85  → our 0x4C-0x4F  Charge OTP
            // esphome abs 86-89  → our 0x50-0x53  Charge OTRP
            // esphome abs 90-93  → our 0x54-0x57  Discharge OTP
            // esphome abs 94-97  → our 0x58-0x5B  Discharge OTRP
            // esphome abs 98-101 → our 0x5C-0x5F  Charge UTP
            // esphome abs 102-105→ our 0x60-0x63  Charge UTRP
            // esphome abs 106-109→ our 0x64-0x67  MOS OTP
            // esphome abs 110-113→ our 0x68-0x6B  MOS OTRP
            ChargingOvertemperatureProtectionC   = ReadI32Le(data, 0x4C) * 0.1,
            ChargingOvertemperatureRecoveryC     = ReadI32Le(data, 0x50) * 0.1,
            DischargingOvertemperatureProtectionC = ReadI32Le(data, 0x54) * 0.1,
            DischargingOvertemperatureRecoveryC  = ReadI32Le(data, 0x58) * 0.1,
            ChargingUndertemperatureProtectionC  = ReadI32Le(data, 0x5C) * 0.1,
            ChargingUndertemperatureRecoveryC    = ReadI32Le(data, 0x60) * 0.1,
            PowerTubeOvertemperatureProtectionC  = ReadI32Le(data, 0x64) * 0.1,
            PowerTubeOvertemperatureRecoveryC    = ReadI32Le(data, 0x68) * 0.1,

            // ── Device configuration ──────────────────────────────────────────────
            // esphome abs 114    → our 0x6C        Cell count
            // esphome abs 118    → our 0x70        Charge switch
            // esphome abs 122    → our 0x74        Discharge switch
            // esphome abs 130-133→ our 0x7C-0x7F   Nominal capacity (mAh)
            CellCount         = data[0x6C],
            ChargingEnabled   = data[0x70] != 0,
            DischargingEnabled = data[0x74] != 0,
            NominalCapacityMah = ReadU32Le(data, 0x7C),
            ShortCircuitProtectionDelayUs = ReadU32Le(data, 0x80),

            RecordedAtUtc = DateTime.UtcNow,
        };
    }

    // ── Binary helpers ────────────────────────────────────────────────────────

    private static uint ReadU32Le(ReadOnlySpan<byte> data, int offset) =>
        (uint)(data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24));

    private static int ReadI32Le(ReadOnlySpan<byte> data, int offset) =>
        data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24);
}
