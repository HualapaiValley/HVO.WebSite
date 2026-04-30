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

        // Cell resistance slots 0x3A–0x69 left as zeros

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
        string firmwareVersion = "V10.2")
    {
        const int dataLength = 293;
        byte[] data = new byte[dataLength];

        WriteAscii(data, 0x00, manufacturer, 16);
        WriteAscii(data, 0x10, hardwareName, 8);
        WriteAscii(data, 0x18, firmwareVersion, 8);

        return WrapInFrame(JkBmsProtocol.FrameTypeDeviceInfo, data);
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
