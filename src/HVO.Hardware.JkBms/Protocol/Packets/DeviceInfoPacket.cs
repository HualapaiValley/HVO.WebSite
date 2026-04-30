namespace HVO.Hardware.JkBms.Protocol.Packets;

/// <summary>
/// Parsed device-info response from a JK BMS unit (frame type 0x03).
///
/// Field layout (offsets relative to the data section — byte 6 of the full 300-byte frame).
/// Corresponding absolute esphome-jk-bms frame offsets = our offset + 6.
///
///   0x00 – 0x0F  Vendor / Manufacturer ID (ASCII, null-padded, 16 bytes)     abs 6
///   0x10 – 0x17  Hardware version string  (ASCII, null-padded, 8 bytes)      abs 22
///   0x18 – 0x1F  Software / firmware version string (ASCII, 8 bytes)         abs 30
///   0x20 – 0x23  Uptime (U32 LE, seconds)                                    abs 38
///   0x24 – 0x27  Power-on count (U32 LE)                                     abs 42
///   0x28 – 0x37  Device name (ASCII, null-padded, 16 bytes)                  abs 46
///   0x38 – 0x47  Device passcode (ASCII, null-padded, 16 bytes)              abs 62
///   0x48 – 0x4F  Manufacturing date (ASCII, null-padded, 8 bytes)            abs 78
///   0x50 – 0x5A  Serial number (ASCII, null-padded, 11 bytes)                abs 86
///   0x5B – 0x5F  Short passcode (ASCII, null-padded, 5 bytes)                abs 97
///   0x60 – 0x6F  User data / device type (ASCII, null-padded, 16 bytes)      abs 102
///   0x70 – 0x7F  Setup passcode (ASCII, null-padded, 16 bytes)               abs 118
///
/// Based on the esphome-jk-bms reference implementation, decode_device_info_().
/// </summary>
public sealed class DeviceInfoPacket
{
    /// <summary>Vendor / model ID string (e.g. "JK-B2A24S15P").</summary>
    public string ManufacturerName { get; init; } = string.Empty;

    /// <summary>Hardware version string (e.g. "10.XW").</summary>
    public string HardwareName { get; init; } = string.Empty;

    /// <summary>Firmware / software version string (e.g. "10.07").</summary>
    public string FirmwareVersion { get; init; } = string.Empty;

    /// <summary>Total device uptime (seconds).</summary>
    public uint UptimeSeconds { get; init; }

    /// <summary>Number of power-on events recorded by the BMS.</summary>
    public uint PowerOnCount { get; init; }

    /// <summary>Device name as configured by the user (e.g. "JK-B2A24S15P").</summary>
    public string DeviceName { get; init; } = string.Empty;

    /// <summary>Manufacturing date string (e.g. "220407"). Empty when not programmed.</summary>
    public string ManufacturingDate { get; init; } = string.Empty;

    /// <summary>
    /// Serial number / manufacturer batch code (e.g. "BT30720201200002005210001").
    /// Displayed as "Manufacturer" in many third-party JK BMS apps.
    /// </summary>
    public string SerialNumber { get; init; } = string.Empty;

    /// <summary>
    /// User data / device type label (e.g. "Input Userdata").
    /// Displayed as "Device Type" in many third-party JK BMS apps.
    /// </summary>
    public string UserData { get; init; } = string.Empty;

    /// <summary>
    /// Setup passcode (e.g. "123456").
    /// Displayed as "Password" in many third-party JK BMS apps.
    /// </summary>
    public string SetupPasscode { get; init; } = string.Empty;

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
        const int minLength = 0x20; // At least through firmware version
        if (data.Length < minLength)
            throw new JkBmsFrameException(
                $"Device info data section is {data.Length} bytes; expected at least {minLength}.");

        return new DeviceInfoPacket
        {
            ManufacturerName  = ReadAsciiString(data, 0x00, 16),
            HardwareName      = ReadAsciiString(data, 0x10, 8),
            FirmwareVersion   = ReadAsciiString(data, 0x18, 8),
            UptimeSeconds     = data.Length >= 0x28 ? ReadU32Le(data, 0x20) : 0,
            PowerOnCount      = data.Length >= 0x28 ? ReadU32Le(data, 0x24) : 0,
            DeviceName        = data.Length >= 0x38 ? ReadAsciiString(data, 0x28, 16) : string.Empty,
            ManufacturingDate = data.Length >= 0x50 ? ReadAsciiString(data, 0x48, 8) : string.Empty,
            SerialNumber      = data.Length >= 0x5C ? ReadAsciiString(data, 0x50, 11) : string.Empty,
            UserData          = data.Length >= 0x70 ? ReadAsciiString(data, 0x60, 16) : string.Empty,
            SetupPasscode     = data.Length >= 0x80 ? ReadAsciiString(data, 0x70, 16) : string.Empty,
            RecordedAtUtc     = DateTime.UtcNow,
        };
    }

    private static uint ReadU32Le(ReadOnlySpan<byte> data, int offset) =>
        (uint)(data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24));

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
