using System.Numerics;

namespace HVO.Hardware.JkBms.Protocol.Packets;

/// <summary>
/// Parsed cell-info response from a JK BMS device (frame type 0x02).
///
/// Field layout (offsets relative to the data section — byte 6 of the full 300-byte frame):
///
///   0x00 – 0x2F  24 × cell voltage (uint16 LE, mV) — unused cell slots contain 0
///   0x30 – 0x33  Enabled-cells bitmask (uint32 LE) — popcount = CellCount
///   0x34 – 0x35  AverageCellVoltageMv (uint16 LE, mV)
///   0x36 – 0x37  DeltaCellVoltageMv (uint16 LE, mV)
///   0x38         MaxVoltageCellIndex (uint8, 1-based; 0 = none)
///   0x39         MinVoltageCellIndex (uint8, 1-based; 0 = none)
///   0x3A – 0x69  24 × cell resistance (uint16 LE, mΩ)  [only first CellCount slots used]
///   0x70 – 0x73  TotalVoltageMv (uint32 LE, mV)
///   0x78 – 0x7B  CurrentMa (int32 LE, mA; positive = charge, negative = discharge)
///   0x7C – 0x7D  BatteryTemperature1 (int16 LE, × 0.1 °C)
///   0x7E – 0x7F  BatteryTemperature2 (int16 LE, × 0.1 °C)
///   0x80 – 0x81  PowerTubeTemperature (int16 LE, × 0.1 °C)
///   0x82 – 0x83  AlarmBitmask (uint16 LE)
///   0x84 – 0x85  BalancingCurrentMa (int16 LE, mA)
///   0x86         BalancingActive (uint8: 0 = off, 1 = charging, 2 = discharging)
///   0x87         StateOfChargePercent (uint8, %)
///   0x88 – 0x8B  RemainingCapacityMah (uint32 LE, mAh)
///   0x8C – 0x8F  NominalCapacityMah (uint32 LE, mAh)
///   0x90 – 0x93  CycleCount (uint32 LE)
///   0x94 – 0x97  CycleCapacityMah (uint32 LE, mAh)
///   0x98         StateOfHealthPercent (uint8, %)
///
/// Based on the esphome-jk-bms reference implementation, JK02_24S variant (offset=0).
/// </summary>
public sealed class CellInfoPacket
{
    // ── Cell voltages ─────────────────────────────────────────────────────────

    /// <summary>
    /// Voltages (mV) for each populated cell, in order from cell 1.
    /// Length equals <see cref="CellCount"/>.
    /// </summary>
    public IReadOnlyList<ushort> CellVoltagesMv { get; init; } = [];

    /// <summary>Number of enabled cells, derived from the enabled-cells bitmask.</summary>
    public byte CellCount { get; init; }

    /// <summary>Average cell voltage (mV), as reported by BMS.</summary>
    public ushort AverageCellVoltageMv { get; init; }

    /// <summary>Delta between max and min cell voltage (mV).</summary>
    public ushort DeltaCellVoltageMv { get; init; }

    /// <summary>1-based index of the cell with the highest voltage (0 = none).</summary>
    public byte MaxVoltageCellIndex { get; init; }

    /// <summary>1-based index of the cell with the lowest voltage (0 = none).</summary>
    public byte MinVoltageCellIndex { get; init; }

    // ── Cell resistance ───────────────────────────────────────────────────────

    /// <summary>
    /// Internal resistance (mΩ) for each populated cell, in order from cell 1.
    /// Length equals <see cref="CellCount"/>.
    /// </summary>
    public IReadOnlyList<ushort> CellResistancesMOhm { get; init; } = [];

    // ── Balancing ─────────────────────────────────────────────────────────────

    /// <summary>Active balancing current (mA).</summary>
    public double BalancingCurrentMa { get; init; }

    /// <summary>True if balancing is currently active.</summary>
    public bool BalancingActive { get; init; }

    // ── Temperatures (°C) ─────────────────────────────────────────────────────

    /// <summary>BMS power tube (MOSFET) temperature (°C). Formula: raw_int16 × 0.1.</summary>
    public double PowerTubeTemperatureC { get; init; }

    /// <summary>Battery temperature sensor 1 (°C). Formula: raw_int16 × 0.1.</summary>
    public double BatteryTemperature1C { get; init; }

    /// <summary>Battery temperature sensor 2 (°C). May equal sensor 1 if only one sensor is fitted.</summary>
    public double BatteryTemperature2C { get; init; }

    // ── Pack-level electrical ─────────────────────────────────────────────────

    /// <summary>Total pack voltage (mV).</summary>
    public uint TotalVoltageMv { get; init; }

    /// <summary>
    /// Pack current (mA). Positive = charging. Negative = discharging.
    /// </summary>
    public int CurrentMa { get; init; }

    // ── Capacity and health ───────────────────────────────────────────────────

    /// <summary>State of charge (%).</summary>
    public ushort StateOfChargePercent { get; init; }

    /// <summary>Remaining capacity (mAh).</summary>
    public uint RemainingCapacityMah { get; init; }

    /// <summary>Nominal (rated) capacity (mAh).</summary>
    public uint NominalCapacityMah { get; init; }

    /// <summary>Cumulative charge/discharge cycle count.</summary>
    public uint CycleCount { get; init; }

    /// <summary>Cumulative charge capacity across all cycles (mAh).</summary>
    public uint CycleCapacityMah { get; init; }

    /// <summary>State of health (%). 100 = new.</summary>
    public ushort StateOfHealthPercent { get; init; }

    // ── Alarms ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Raw JK02 alarm flag bitmask. The 24S frame supplies 16 bits and the 32S frame supplies 32 bits.
    /// </summary>
    public uint AlarmBitmask { get; init; }

    /// <summary>True if any alarm flag is set.</summary>
    public bool HasAlarms => AlarmBitmask != 0;

    // ── Timestamp ─────────────────────────────────────────────────────────────

    /// <summary>UTC time this packet was parsed.</summary>
    public DateTime RecordedAtUtc { get; init; } = DateTime.UtcNow;

    // ── Factory ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Parse a <see cref="CellInfoPacket"/> from the data section of a cell-info frame.
    /// The <paramref name="data"/> span must cover bytes 6–298 of the complete 300-byte
    /// frame (i.e. the 293-byte payload returned by <see cref="JkBmsProtocol.GetData"/>).
    /// </summary>
    /// <exception cref="JkBmsFrameException">
    /// Thrown if <paramref name="data"/> is too short to contain the required fields.
    /// </exception>
    public static CellInfoPacket Parse(ReadOnlySpan<byte> data)
    {
        // Minimum for JK02_24S: SOH byte at 0x98 inclusive = 0x99 = 153 bytes.
        // Minimum for JK02_32S: SOH byte at 0xB8 inclusive = 0xB9 = 185 bytes.
        // The full data section is always 293 bytes, so both thresholds are met in practice.
        const int minLength24S = 0x99;
        if (data.Length < minLength24S)
            throw new JkBmsFrameException(
                $"Cell info data section is {data.Length} bytes; expected at least {minLength24S}.");

        // ── Cell count from bitmask ────────────────────────────────────────────
        // JK02_24S: enabled-cells bitmask at 0x30 (frame byte 54); popcount = cell count.
        // JK02_32S (newer EC/EA firmware): bitmask at 0x30 is zero; instead count
        // consecutive non-zero U16 voltage entries from offset 0 (max 32 cells).
        uint enabledMask = ReadU32Le(data, 0x30);
        byte cellCount;
        if (enabledMask != 0)
        {
            cellCount = (byte)BitOperations.PopCount(enabledMask);
        }
        else
        {
            // Count leading non-zero U16 entries (up to 32 for potential 32S devices).
            cellCount = 0;
            for (int i = 0; i < 32; i++)
            {
                if (ReadU16Le(data, i * 2) == 0) break;
                cellCount++;
            }
        }
        if (cellCount is 0 or > 32)
            throw new JkBmsFrameException(
                $"Invalid cell count {cellCount} derived from bitmask 0x{enabledMask:X8} (expected 1–32).");

        // ── Detect JK02_32S frame variant ──────────────────────────────────────
        // When the bitmask is absent (enabledMask == 0), probe TotalVoltage at both
        // the 24S offset (0x70) and the 32S offset (0x90).  If 0x70 is zero but 0x90
        // is non-zero the frame follows the 32S layout (pack-level fields shifted +0x20).
        // Reference: esphome-jk-bms decode_jk02_cell_info_ with offset variable (16→32).
        bool is32S = enabledMask == 0
                  && ReadU32Le(data, 0x70) == 0
                  && data.Length >= 0xB9
                  && ReadU32Le(data, 0x90) != 0;

        // ── Cell voltages (slots 0..cellCount-1) ──────────────────────────────
        var voltages = new ushort[cellCount];
        for (int i = 0; i < cellCount; i++)
            voltages[i] = ReadU16Le(data, i * 2);

        // ── Cell resistances ──────────────────────────────────────────────────
        // 24S: resistance slots start at 0x3A (right after avg/delta/min/max).
        // 32S: resistance slots start at 0x4A (+0x10, because voltage+metadata sections
        //      each grow by one additional slot vs 24S).
        int resistanceBase = is32S ? 0x4A : 0x3A;
        var resistances = new ushort[cellCount];
        for (int i = 0; i < cellCount; i++)
            resistances[i] = ReadU16Le(data, resistanceBase + i * 2);

        if (is32S)
        {
            // JK02_32S pack-level field offsets (esphome field_offset = 32 = 0x20):
            //   avg/delta/max/min: base + 0x10  (voltage section grows by 16 bytes)
            //   TotalVoltage…CycleCapacity: base + 0x20  (both sections grow by 16 bytes)
            //   PowerTubeTemp: 0x8A  (esphome data[112+32]=frame[144]=our[138])
            //   AlarmBitmask (LE): 0xA0  (esphome data[134+32]=frame[166]=our[160])
            return new CellInfoPacket
            {
                CellVoltagesMv         = voltages,
                CellCount              = cellCount,
                AverageCellVoltageMv   = ReadU16Le(data, 0x44),
                DeltaCellVoltageMv     = ReadU16Le(data, 0x46),
                MaxVoltageCellIndex    = data[0x48],
                MinVoltageCellIndex    = data[0x49],
                CellResistancesMOhm    = resistances,
                TotalVoltageMv         = ReadU32Le(data, 0x90),
                CurrentMa              = ReadI32Le(data, 0x98),
                BatteryTemperature1C   = DecodeTemperature(ReadI16Le(data, 0x9C)),
                BatteryTemperature2C   = DecodeTemperature(ReadI16Le(data, 0x9E)),
                PowerTubeTemperatureC  = DecodeTemperature(ReadI16Le(data, 0x8A)),
                AlarmBitmask           = ReadU32Le(data, 0xA0),
                BalancingCurrentMa     = ReadI16Le(data, 0xA4),
                BalancingActive        = data[0xA6] != 0,
                StateOfChargePercent   = data[0xA7],
                RemainingCapacityMah   = ReadU32Le(data, 0xA8),
                NominalCapacityMah     = ReadU32Le(data, 0xAC),
                CycleCount             = ReadU32Le(data, 0xB0),
                CycleCapacityMah       = ReadU32Le(data, 0xB4),
                StateOfHealthPercent   = data[0xB8],
                RecordedAtUtc          = DateTime.UtcNow,
            };
        }

        // JK02_24S layout (standard offsets)
        return new CellInfoPacket
        {
            CellVoltagesMv         = voltages,
            CellCount              = cellCount,
            AverageCellVoltageMv   = ReadU16Le(data, 0x34),
            DeltaCellVoltageMv     = ReadU16Le(data, 0x36),
            MaxVoltageCellIndex    = data[0x38],
            MinVoltageCellIndex    = data[0x39],
            CellResistancesMOhm    = resistances,
            TotalVoltageMv         = ReadU32Le(data, 0x70),
            CurrentMa              = ReadI32Le(data, 0x78),
            BatteryTemperature1C   = DecodeTemperature(ReadI16Le(data, 0x7C)),
            BatteryTemperature2C   = DecodeTemperature(ReadI16Le(data, 0x7E)),
            PowerTubeTemperatureC  = DecodeTemperature(ReadI16Le(data, 0x80)),
            AlarmBitmask           = ReadU16Le(data, 0x82),
            BalancingCurrentMa     = ReadI16Le(data, 0x84),
            BalancingActive        = data[0x86] != 0,
            StateOfChargePercent   = data[0x87],
            RemainingCapacityMah   = ReadU32Le(data, 0x88),
            NominalCapacityMah     = ReadU32Le(data, 0x8C),
            CycleCount             = ReadU32Le(data, 0x90),
            CycleCapacityMah       = ReadU32Le(data, 0x94),
            StateOfHealthPercent   = data[0x98],
            RecordedAtUtc          = DateTime.UtcNow,
        };
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Decode a JK BMS temperature raw value to degrees Celsius.
    /// Encoding: signed int16, units of 0.1 °C.
    /// Formula: °C = raw × 0.1
    /// </summary>
    internal static double DecodeTemperature(short raw) => raw * 0.1;

    private static ushort ReadU16Le(ReadOnlySpan<byte> data, int offset) =>
        (ushort)(data[offset] | (data[offset + 1] << 8));

    private static short ReadI16Le(ReadOnlySpan<byte> data, int offset) =>
        (short)(data[offset] | (data[offset + 1] << 8));

    private static uint ReadU32Le(ReadOnlySpan<byte> data, int offset) =>
        (uint)(data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24));

    private static int ReadI32Le(ReadOnlySpan<byte> data, int offset) =>
        data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24);
}
