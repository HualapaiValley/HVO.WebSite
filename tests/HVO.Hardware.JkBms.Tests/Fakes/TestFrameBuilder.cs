using HVO.Hardware.JkBms.Protocol;
using ProductionCrc = HVO.Hardware.JkBms.Protocol.CrcByteSum;

namespace HVO.Hardware.JkBms.Tests.Fakes;

/// <summary>
/// Builds synthetic JK BMS response frames for use in unit tests.
/// All multi-byte integers are little-endian following the JK BMS protocol specification,
/// except AlarmBitmask which is big-endian.
/// </summary>
public static class TestFrameBuilder
{
    // ── Full frame ────────────────────────────────────────────────────────────

    /// <summary>
    /// Build a complete cell-info frame (300 bytes: 6-byte header + 293-byte payload + CRC8).
    /// The data section is built using the supplied values;
    /// all unspecified fields default to safe, in-range values.
    /// </summary>
    public static byte[] BuildCellInfoFrame(
        int cellCount = 15,
        ushort[]? cellVoltagesMv = null,
        ushort[]? cellResistancesMOhm = null,
        ushort averageCellVoltageMv = 3300,
        ushort deltaCellVoltageMv = 5,
        byte maxCellIndex = 1,
        byte minCellIndex = 2,
        int balancingCurrentMa = 0,
        byte balancingActive = 0,
        short powerTubeRaw = 250,   // 25.0 °C (raw × 0.1)
        short battTemp1Raw = 250,
        short battTemp2Raw = 250,
        uint totalVoltageMv = 49500,
        int currentMa = 5000,
        byte socPercent = 80,
        uint remainingMah = 80_000,
        uint nominalMah = 100_000,
        uint cycleCount = 10,
        uint cycleMah = 500_000,
        byte sohPercent = 100,
        uint alarmBitmask = 0)
    {
        const int dataLength = 293;
        byte[] data = new byte[dataLength];

        // Cell voltages — 24 slots × 2 bytes each (0x00–0x2F)
        var voltages = cellVoltagesMv ?? Enumerable.Repeat(averageCellVoltageMv, cellCount).ToArray();
        for (int i = 0; i < Math.Min(cellCount, 24); i++)
            WriteU16Le(data, i * 2, voltages[i]);

        // Enabled cells bitmask at 0x30 (uint32 LE); popcount gives cellCount
        uint enabledMask = cellCount >= 32 ? 0xFFFFFFFFu : (1u << cellCount) - 1u;
        WriteU32Le(data, 0x30, enabledMask);

        WriteU16Le(data, 0x34, averageCellVoltageMv);
        WriteU16Le(data, 0x36, deltaCellVoltageMv);
        data[0x38] = maxCellIndex;
        data[0x39] = minCellIndex;

        // Cell resistance slots 0x3A–0x69 (24 × uint16 LE, mΩ)
        if (cellResistancesMOhm is not null)
            for (int i = 0; i < Math.Min(cellResistancesMOhm.Length, 24); i++)
                WriteU16Le(data, 0x3A + i * 2, cellResistancesMOhm[i]);

        WriteU32Le(data, 0x70, totalVoltageMv);
        WriteI32Le(data, 0x78, currentMa);
        WriteI16Le(data, 0x7C, battTemp1Raw);
        WriteI16Le(data, 0x7E, battTemp2Raw);
        WriteI16Le(data, 0x80, powerTubeRaw);

        // AlarmBitmask at 0x82 — big-endian uint16
        data[0x82] = (byte)((alarmBitmask >> 8) & 0xFF);
        data[0x83] = (byte)(alarmBitmask & 0xFF);

        WriteI16Le(data, 0x84, (short)balancingCurrentMa);
        data[0x86] = balancingActive;
        data[0x87] = socPercent;
        WriteU32Le(data, 0x88, remainingMah);
        WriteU32Le(data, 0x8C, nominalMah);
        WriteU32Le(data, 0x90, cycleCount);
        WriteU32Le(data, 0x94, cycleMah);
        data[0x98] = sohPercent;

        return WrapInFrame(JkBmsProtocol.FrameTypeCellInfo, data);
    }

    /// <summary>
    /// Build a complete device-info frame (300 bytes: 6-byte header + 293-byte payload + CRC8).
    /// </summary>
    public static byte[] BuildDeviceInfoFrame(
        string manufacturer = "JIKONG",
        string hardwareName = "JK-B2A24",
        string firmwareVersion = "V10.2",
        uint uptimeSeconds = 0,
        uint powerOnCount = 0,
        string deviceName = "",
        string manufacturingDate = "",
        string serialNumber = "",
        string userData = "",
        string setupPasscode = "")
    {
        const int dataLength = 293;
        byte[] data = new byte[dataLength];

        WriteAscii(data, 0x00, manufacturer, 16);
        WriteAscii(data, 0x10, hardwareName, 8);
        WriteAscii(data, 0x18, firmwareVersion, 8);
        WriteU32Le(data, 0x20, uptimeSeconds);
        WriteU32Le(data, 0x24, powerOnCount);
        WriteAscii(data, 0x28, deviceName, 16);
        WriteAscii(data, 0x48, manufacturingDate, 8);
        WriteAscii(data, 0x50, serialNumber, 11);
        WriteAscii(data, 0x60, userData, 16);
        WriteAscii(data, 0x70, setupPasscode, 16);

        return WrapInFrame(JkBmsProtocol.FrameTypeDeviceInfo, data);
    }

    /// <summary>
    /// Build a complete settings frame (300 bytes: 6-byte header + 293-byte payload + CRC8).
    /// Offset layout follows the JK02 settings frame specification.
    /// </summary>
    public static byte[] BuildSettingsFrame(
        uint cellUvpMv = 2900,
        uint cellUvprMv = 3000,
        uint cellOvpMv = 4200,
        uint cellOvprMv = 4100,
        uint balanceDeltaMv = 10,
        uint balanceStartMv = 3300,
        bool balancingEnabled = true,
        uint chargeOcpMa = 50_000,
        uint chargeOcpDelayS = 2,
        uint chargeOcpRecoveryS = 30,
        uint dischargeOcpMa = 100_000,
        uint dischargeOcpDelayS = 2,
        uint dischargeOcpRecoveryS = 30,
        uint scpRecoveryS = 30,
        uint scpDelayUs = 150,
        int chargeOtpRaw = 450,     // 45.0°C
        int chargeOtprRaw = 400,    // 40.0°C
        int dischargeOtpRaw = 600,  // 60.0°C
        int dischargeOtprRaw = 550, // 55.0°C
        int chargeUtpRaw = -100,    // -10.0°C
        int chargeUtprRaw = 0,      // 0.0°C
        int mosOtpRaw = 750,        // 75.0°C
        int mosOtprRaw = 700,       // 70.0°C
        byte cellCount = 16,
        bool chargingEnabled = true,
        bool dischargingEnabled = true,
        uint nominalCapacityMah = 100_000)
    {
        const int dataLength = 293;
        byte[] data = new byte[dataLength];

        // Cell voltage protection (U32 LE, mV)
        WriteU32Le(data, 0x04, cellUvpMv);
        WriteU32Le(data, 0x08, cellUvprMv);
        WriteU32Le(data, 0x0C, cellOvpMv);
        WriteU32Le(data, 0x10, cellOvprMv);

        // Balancing
        WriteU32Le(data, 0x14, balanceDeltaMv);
        WriteU32Le(data, 0x84, balanceStartMv);
        data[0x78] = balancingEnabled ? (byte)1 : (byte)0;

        // Overcurrent protection (U32 LE, mA or s)
        WriteU32Le(data, 0x2C, chargeOcpMa);
        WriteU32Le(data, 0x30, chargeOcpDelayS);
        WriteU32Le(data, 0x34, chargeOcpRecoveryS);
        WriteU32Le(data, 0x38, dischargeOcpMa);
        WriteU32Le(data, 0x3C, dischargeOcpDelayS);
        WriteU32Le(data, 0x40, dischargeOcpRecoveryS);
        WriteU32Le(data, 0x44, scpRecoveryS);
        WriteU32Le(data, 0x80, scpDelayUs);

        // Temperature protection (I32 LE, × 0.1°C)
        WriteI32Le(data, 0x4C, chargeOtpRaw);
        WriteI32Le(data, 0x50, chargeOtprRaw);
        WriteI32Le(data, 0x54, dischargeOtpRaw);
        WriteI32Le(data, 0x58, dischargeOtprRaw);
        WriteI32Le(data, 0x5C, chargeUtpRaw);
        WriteI32Le(data, 0x60, chargeUtprRaw);
        WriteI32Le(data, 0x64, mosOtpRaw);
        WriteI32Le(data, 0x68, mosOtprRaw);

        // Device configuration
        data[0x6C] = cellCount;
        data[0x70] = chargingEnabled ? (byte)1 : (byte)0;
        data[0x74] = dischargingEnabled ? (byte)1 : (byte)0;
        WriteU32Le(data, 0x7C, nominalCapacityMah);

        return WrapInFrame(JkBmsProtocol.FrameTypeSettings, data);
    }

    /// <summary>
    /// Build a complete cell-info frame using the JK02_32S layout (for newer EC/EA prefix devices
    /// that support 32 cells).  Key differences from the 24S layout:
    /// <list type="bullet">
    ///   <item>Enabled-cells bitmask at 0x30 is zero (32S devices don't set it).</item>
    ///   <item>TotalVoltage at 0x70 is zero; the real pack voltage is at 0x90.</item>
    ///   <item>Pack-level and capacity fields are shifted +0x20 relative to 24S.</item>
    ///   <item>Cell resistances start at 0x4A (+0x10 relative to 24S).</item>
    /// </list>
    /// </summary>
    public static byte[] BuildCellInfoFrame32S(
        int cellCount = 16,
        ushort[]? cellVoltagesMv = null,
        ushort[]? cellResistancesMOhm = null,
        ushort averageCellVoltageMv = 3300,
        ushort deltaCellVoltageMv = 5,
        byte maxCellIndex = 1,
        byte minCellIndex = 2,
        int balancingCurrentMa = 0,
        byte balancingActive = 0,
        short powerTubeRaw = 250,
        short battTemp1Raw = 250,
        short battTemp2Raw = 250,
        uint totalVoltageMv = 52800,
        int currentMa = 5000,
        byte socPercent = 80,
        uint remainingMah = 80_000,
        uint nominalMah = 100_000,
        uint cycleCount = 10,
        uint cycleMah = 500_000,
        byte sohPercent = 100,
        ushort alarmBitmask = 0)
    {
        const int dataLength = 293;
        byte[] data = new byte[dataLength];

        // Cell voltages — up to 32 slots × 2 bytes each (0x00–0x3F)
        var voltages = cellVoltagesMv ?? Enumerable.Repeat(averageCellVoltageMv, cellCount).ToArray();
        for (int i = 0; i < Math.Min(cellCount, 32); i++)
            WriteU16Le(data, i * 2, voltages[i]);

        // Enabled-cells bitmask at 0x30 is 0 for 32S devices (parser falls back to voltage count)
        // data[0x30..0x33] remain 0

        // 32S avg/delta/max/min are at 0x44–0x49 (+0x10 vs 24S)
        WriteU16Le(data, 0x44, averageCellVoltageMv);
        WriteU16Le(data, 0x46, deltaCellVoltageMv);
        data[0x48] = maxCellIndex;
        data[0x49] = minCellIndex;

        // Cell resistance slots 0x4A–0x89 (32 × uint16 LE, mΩ) — (+0x10 vs 24S)
        if (cellResistancesMOhm is not null)
            for (int i = 0; i < Math.Min(cellResistancesMOhm.Length, 32); i++)
                WriteU16Le(data, 0x4A + i * 2, cellResistancesMOhm[i]);

        // PowerTubeTemp at 0x8A (+0x0A vs 24S 0x80)
        WriteI16Le(data, 0x8A, powerTubeRaw);

        // TotalVoltage at 0x90 (24S field at 0x70 must be 0 to trigger 32S detection)
        // data[0x70..0x73] remain 0
        WriteU32Le(data, 0x90, totalVoltageMv);
        WriteI32Le(data, 0x98, currentMa);
        WriteI16Le(data, 0x9C, battTemp1Raw);
        WriteI16Le(data, 0x9E, battTemp2Raw);

        // AlarmBitmask at 0xA0 — big-endian uint16
        data[0xA0] = (byte)((alarmBitmask >> 8) & 0xFF);
        data[0xA1] = (byte)(alarmBitmask & 0xFF);

        WriteI16Le(data, 0xA4, (short)balancingCurrentMa);
        data[0xA6] = balancingActive;
        data[0xA7] = socPercent;
        WriteU32Le(data, 0xA8, remainingMah);
        WriteU32Le(data, 0xAC, nominalMah);
        WriteU32Le(data, 0xB0, cycleCount);
        WriteU32Le(data, 0xB4, cycleMah);
        data[0xB8] = sohPercent;

        return WrapInFrame(JkBmsProtocol.FrameTypeCellInfo, data);
    }

    /// <summary>
    /// Build a minimal valid frame with the specified type and all-zero data.
    /// Useful for testing wrong-frame-type handling.
    /// </summary>
    public static byte[] BuildMinimalFrame(byte frameType)
    {
        byte[] data = new byte[293]; // all zeros
        return WrapInFrame(frameType, data);
    }

    // ── Chunked helpers ───────────────────────────────────────────────────────

    /// <summary>Split <paramref name="frame"/> into 20-byte BLE notification chunks.</summary>
    public static IReadOnlyList<byte[]> SplitIntoChunks(byte[] frame, int chunkSize = 20)
    {
        var chunks = new List<byte[]>();
        for (int i = 0; i < frame.Length; i += chunkSize)
        {
            int len = Math.Min(chunkSize, frame.Length - i);
            var chunk = new byte[len];
            Array.Copy(frame, i, chunk, 0, len);
            chunks.Add(chunk);
        }
        return chunks;
    }

    // ── Internals ─────────────────────────────────────────────────────────────

    private static byte[] WrapInFrame(byte frameType, byte[] data)
    {
        // Fixed 300-byte frame: 6-byte header + 293-byte payload + 1-byte CRC8
        var frame = new byte[300];
        frame[0] = 0x55; frame[1] = 0xAA; frame[2] = 0xEB; frame[3] = 0x90;
        frame[4] = frameType;
        frame[5] = 0x01; // counter (arbitrary)
        // Copy data payload to bytes 6..298 (up to 293 bytes)
        int len = Math.Min(data.Length, 293);
        Array.Copy(data, 0, frame, 6, len);
        // CRC8 (byte sum) at byte 299
        frame[299] = ProductionCrc.Compute(frame.AsSpan(0, 299));
        return frame;
    }

    private static void WriteU16Le(byte[] buf, int offset, ushort value)
    {
        buf[offset]     = (byte)(value & 0xFF);
        buf[offset + 1] = (byte)((value >> 8) & 0xFF);
    }

    private static void WriteI16Le(byte[] buf, int offset, short value)
    {
        buf[offset]     = (byte)(value & 0xFF);
        buf[offset + 1] = (byte)((value >> 8) & 0xFF);
    }

    private static void WriteU32Le(byte[] buf, int offset, uint value)
    {
        buf[offset]     = (byte)(value & 0xFF);
        buf[offset + 1] = (byte)((value >> 8) & 0xFF);
        buf[offset + 2] = (byte)((value >> 16) & 0xFF);
        buf[offset + 3] = (byte)((value >> 24) & 0xFF);
    }

    private static void WriteI32Le(byte[] buf, int offset, int value) =>
        WriteU32Le(buf, offset, (uint)value);

    private static void WriteAscii(byte[] buf, int offset, string text, int maxLength)
    {
        var bytes = System.Text.Encoding.ASCII.GetBytes(text);
        int len = Math.Min(bytes.Length, maxLength);
        Array.Copy(bytes, 0, buf, offset, len);
        // Remaining bytes stay as 0 (null-padded)
    }
}
