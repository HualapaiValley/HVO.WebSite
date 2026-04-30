namespace HVO.Hardware.JkBms.Protocol;

/// <summary>
/// CRC checksum used by the JK BMS BLE protocol.
///
/// Algorithm: simple unsigned byte sum (mod 256) — identical to the
/// <c>uint8_t crc(const uint8_t data[], uint16_t len)</c> function in the
/// esphome-jk-bms reference implementation.
///
/// Frame layout: the checksum occupies the single byte at position 299
/// (the last byte of the fixed 300-byte response frame), covering bytes 0–298.
/// </summary>
public static class CrcByteSum
{
    /// <summary>Compute the byte-sum CRC of <paramref name="data"/>.</summary>
    public static byte Compute(ReadOnlySpan<byte> data)
    {
        byte crc = 0;
        foreach (byte b in data)
            crc += b;
        return crc;
    }

    /// <summary>
    /// Append the 1-byte CRC to the end of <paramref name="data"/>
    /// and return the combined array.
    /// </summary>
    public static byte[] AppendCrc(ReadOnlySpan<byte> data)
    {
        byte crc = Compute(data);
        var result = new byte[data.Length + 1];
        data.CopyTo(result);
        result[data.Length] = crc;
        return result;
    }

    /// <summary>
    /// Returns true if the last byte of <paramref name="frameWithCrc"/> equals
    /// the byte-sum CRC of the preceding bytes.
    /// </summary>
    public static bool IsValid(ReadOnlySpan<byte> frameWithCrc)
    {
        if (frameWithCrc.Length < 2) return false;
        return Compute(frameWithCrc[..^1]) == frameWithCrc[^1];
    }
}
