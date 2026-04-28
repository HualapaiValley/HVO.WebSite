namespace HVO.Hardware.DavisVantagePro2.Protocol;

/// <summary>CRC-CCITT-16 calculator for the Davis serial protocol.</summary>
/// <remarks>
/// The Davis protocol uses CRC-CCITT-16 (polynomial 0x1021, initial value 0x0000).
/// A valid packet satisfies: crc16(data + crc_bytes) == 0x0000.
/// </remarks>
internal static class CrcCalculator
{
    // Pre-computed CRC-CCITT-16 table (polynomial 0x1021)
    private static readonly ushort[] Table = BuildTable();

    private static ushort[] BuildTable()
    {
        const ushort poly = 0x1021;
        var table = new ushort[256];
        for (int i = 0; i < 256; i++)
        {
            ushort crc = (ushort)(i << 8);
            for (int j = 0; j < 8; j++)
            {
                if ((crc & 0x8000) != 0)
                    crc = (ushort)((crc << 1) ^ poly);
                else
                    crc <<= 1;
            }
            table[i] = crc;
        }
        return table;
    }

    /// <summary>Compute CRC-CCITT-16 over the given buffer. Returns 0x0000 if the buffer includes valid CRC bytes at the end.</summary>
    public static ushort Compute(ReadOnlySpan<byte> buffer)
    {
        ushort crc = 0;
        foreach (byte b in buffer)
            crc = (ushort)((crc << 8) ^ Table[(crc >> 8) ^ b]);
        return crc;
    }

    /// <summary>Returns true when computing CRC over the full buffer (data + 2 appended CRC bytes) yields zero.</summary>
    public static bool IsValid(ReadOnlySpan<byte> bufferWithCrc) =>
        Compute(bufferWithCrc) == 0;

    /// <summary>Appends the 2-byte CRC to a byte array, big-endian (as required by Davis console).</summary>
    public static byte[] AppendCrc(byte[] data)
    {
        ushort crc = Compute(data);
        var result = new byte[data.Length + 2];
        data.CopyTo(result, 0);
        result[data.Length]     = (byte)(crc >> 8);
        result[data.Length + 1] = (byte)(crc & 0xFF);
        return result;
    }
}
