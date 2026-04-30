namespace HVO.Hardware.JkBms.Protocol.Packets;

/// <summary>
/// Parsed device-info response from a JK BMS unit (frame type 0x03).
///
/// Field layout (offsets relative to the data section — byte 6 of the full 300-byte frame):
///   0x00 – 0x0F  Vendor / Manufacturer ID (ASCII, null-padded, 16 bytes)
///   0x10 – 0x17  Hardware version string  (ASCII, null-padded, 8 bytes)
///   0x18 – 0x1F  Software / firmware version string (ASCII, null-padded, 8 bytes)
///
/// Based on the esphome-jk-bms reference implementation, decode_device_info_().
/// </summary>
public sealed class DeviceInfoPacket
{
    /// <summary>Manufacturer / vendor ID string (e.g. "JK-B2A24S15P").</summary>
    public string ManufacturerName { get; init; } = string.Empty;

    /// <summary>Hardware version string (e.g. "10.XW").</summary>
    public string HardwareName { get; init; } = string.Empty;

    /// <summary>Firmware / software version string (e.g. "10.07").</summary>
    public string FirmwareVersion { get; init; } = string.Empty;

    /// <summary>UTC time this packet was parsed.</summary>
    public DateTime RecordedAtUtc { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Parse a <see cref="DeviceInfoPacket"/> from the data section of a device-info frame.
    /// The <paramref name="data"/> span must cover bytes 6–298 of the complete 300-byte
    /// frame (i.e. the 293-byte payload returned by <see cref="JkBmsProtocol.GetData"/>).
    /// </summary>
    /// <exception cref="JkBmsFrameException">
    /// Thrown if <paramref name="data"/> is too short to contain the required fields.
    /// </exception>
    public static DeviceInfoPacket Parse(ReadOnlySpan<byte> data)
    {
        const int minLength = 0x20; // 0x18 + 8 bytes for software version
        if (data.Length < minLength)
            throw new JkBmsFrameException(
                $"Device info data section is {data.Length} bytes; expected at least {minLength}.");

        return new DeviceInfoPacket
        {
            ManufacturerName = ReadAsciiString(data, 0x00, 16),
            HardwareName     = ReadAsciiString(data, 0x10, 8),
            FirmwareVersion  = ReadAsciiString(data, 0x18, 8),
            RecordedAtUtc    = DateTime.UtcNow,
        };
    }

    private static string ReadAsciiString(ReadOnlySpan<byte> data, int offset, int length)
    {
        var slice = data.Slice(offset, length);
        // Trim null bytes and control characters from the end
        int end = slice.Length;
        while (end > 0 && (slice[end - 1] == 0 || slice[end - 1] < 0x20))
            end--;
        return System.Text.Encoding.ASCII.GetString(slice[..end]);
    }
}
