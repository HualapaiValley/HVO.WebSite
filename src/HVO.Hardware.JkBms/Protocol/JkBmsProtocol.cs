namespace HVO.Hardware.JkBms.Protocol;

/// <summary>
/// Builds and decodes JK BMS BLE protocol frames.
///
/// Protocol overview:
///   Request  (host → BMS):  20-byte fixed command frame starting with AA 55 90 EB,
///                           last byte = CRC8 (byte sum of first 19 bytes).
///   Response (BMS → host):  300-byte fixed frame, SOF = 55 AA EB 90,
///                           delivered in up to N 20-byte BLE notification chunks.
///
/// Response frame structure (300 bytes, fixed):
///   [0..3]   SOF:        55 AA EB 90
///   [4]      frame_type: 0x02 = cell_info, 0x03 = device_info
///   [5]      counter:    incrementing byte (ignored on receive)
///   [6..298] data:       293 bytes of payload (NO length prefix field)
///   [299]    CRC8:       unsigned byte sum of bytes 0..298 mod 256
///
/// Reference: https://github.com/syssi/esphome-jk-bms
/// </summary>
public static class JkBmsProtocol
{
    // ── BLE service / characteristic UUIDs ───────────────────────────────────

    /// <summary>JK BMS UART-over-BLE service UUID.</summary>
    public static readonly Guid ServiceUuid = new("0000ffe0-0000-1000-8000-00805f9b34fb");

    /// <summary>JK BMS UART-over-BLE characteristic UUID (Write + Notify).</summary>
    public static readonly Guid CharacteristicUuid = new("0000ffe1-0000-1000-8000-00805f9b34fb");

    // ── Frame types ───────────────────────────────────────────────────────────

    public const byte FrameTypeSettings  = 0x01;
    public const byte FrameTypeCellInfo  = 0x02;
    public const byte FrameTypeDeviceInfo = 0x03;

    // ── Frame geometry (response frames are always exactly 300 bytes) ─────────

    private const int TotalFrameLength = 300;
    private const int HeaderLength     = 6;   // SOF(4) + type(1) + counter(1)
    private const int DataLength       = 293; // payload bytes (no length prefix field)
    private const int CrcLength        = 1;   // 1-byte CRC8 at byte 299

    // ── Command building ──────────────────────────────────────────────────────

    /// <summary>
    /// Returns the 20-byte "get cell info" request frame.
    /// Last byte is CRC8 = byte sum of first 19 bytes.
    /// </summary>
    public static byte[] BuildCellInfoCommand() =>
        [0xAA, 0x55, 0x90, 0xEB, 0x96, 0x00, 0x00, 0x00,
         0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
         0x00, 0x00, 0x00, 0x10]; // CRC8: 0xAA+0x55+0x90+0xEB+0x96 = 0x310 → 0x10

    /// <summary>
    /// Returns the 20-byte "get device info" request frame.
    /// Last byte is CRC8 = byte sum of first 19 bytes.
    /// </summary>
    public static byte[] BuildDeviceInfoCommand() =>
        [0xAA, 0x55, 0x90, 0xEB, 0x97, 0x00, 0x00, 0x00,
         0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
         0x00, 0x00, 0x00, 0x11]; // CRC8: 0xAA+0x55+0x90+0xEB+0x97 = 0x311 → 0x11

    // ── Frame assembly from chunked BLE notifications ─────────────────────────

    /// <summary>
    /// Accumulate BLE notification chunks and attempt to extract a complete frame.
    ///
    /// Call this method each time a new chunk arrives. Returns true (and sets
    /// <paramref name="frame"/>) once a complete, structurally valid 300-byte frame
    /// has been assembled. Partial or malformed data is retained in
    /// <paramref name="buffer"/> for the next call.
    ///
    /// Does NOT validate CRC — call <see cref="ValidateCrc"/> separately.
    /// </summary>
    /// <param name="buffer">Accumulation buffer (modified in place).</param>
    /// <param name="chunk">Newly received bytes.</param>
    /// <param name="frame">Set to the complete frame bytes when method returns true.</param>
    public static bool TryAccumulateFrame(
        List<byte> buffer, ReadOnlySpan<byte> chunk, out byte[] frame)
    {
        frame = [];

        // Flush buffer on SOF preamble (matches esphome assemble() behaviour)
        if (chunk.Length >= 4 &&
            chunk[0] == 0x55 && chunk[1] == 0xAA && chunk[2] == 0xEB && chunk[3] == 0x90)
        {
            buffer.Clear();
        }

        buffer.AddRange(chunk);

        // Need at least the 4-byte SOF to locate the start
        if (buffer.Count < 4)
            return false;

        // Locate SOF — discard any leading garbage before 55 AA EB 90
        int sofIndex = FindSof(buffer);
        if (sofIndex < 0)
        {
            // SOF not yet seen; keep only the last 3 bytes in case it straddles a chunk boundary
            if (buffer.Count > 3)
                buffer.RemoveRange(0, buffer.Count - 3);
            return false;
        }
        if (sofIndex > 0)
            buffer.RemoveRange(0, sofIndex);

        if (buffer.Count < TotalFrameLength)
            return false; // still assembling

        frame = [.. buffer.Take(TotalFrameLength)];
        buffer.RemoveRange(0, TotalFrameLength);
        return true;
    }

    private static int FindSof(List<byte> buffer)
    {
        for (int i = 0; i <= buffer.Count - 4; i++)
        {
            if (buffer[i]     == 0x55 && buffer[i + 1] == 0xAA &&
                buffer[i + 2] == 0xEB && buffer[i + 3] == 0x90)
                return i;
        }
        return -1;
    }

    // ── Validation ────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns true if the frame begins with the expected SOF bytes,
    /// is exactly 300 bytes long, and the CRC8 at byte 299 matches.
    /// </summary>
    public static bool ValidateCrc(ReadOnlySpan<byte> frame)
    {
        if (frame.Length != TotalFrameLength) return false;

        // Verify SOF
        if (frame[0] != 0x55 || frame[1] != 0xAA || frame[2] != 0xEB || frame[3] != 0x90)
            return false;

        return CrcByteSum.IsValid(frame);
    }

    /// <summary>
    /// Returns the frame type byte from a complete frame.
    /// Throws <see cref="JkBmsFrameException"/> if the frame is too short.
    /// </summary>
    public static byte GetFrameType(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < HeaderLength)
            throw new JkBmsFrameException("Frame is too short to contain a type byte.");
        return frame[4];
    }

    /// <summary>
    /// Returns a span over the data section of a complete frame (bytes 6–298, excludes SOF, type, counter, and CRC).
    /// </summary>
    public static ReadOnlySpan<byte> GetData(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < TotalFrameLength)
            throw new JkBmsFrameException("Frame is too short to contain a data section.");
        return frame.Slice(HeaderLength, DataLength);
    }
}
