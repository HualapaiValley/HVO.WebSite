using System.Buffers.Binary;

namespace HVO.Hardware.DavisVantagePro2.Protocol.Packets;

/// <summary>
/// Decoded archive record (type B — Vantage Pro 2) from a DMPAFT or DMP page.
/// Each page is 267 bytes: 1 sequence byte + 5 × 52-byte records + 2-byte CRC.
/// Field offsets match rec_B_schema in the WeeWX vantage driver.
/// </summary>
public sealed record ArchiveRecord
{
    public DateTime DateTimeLocal { get; init; }
    public int ArchiveIntervalMinutes { get; init; }

    // Temperature
    public double? OutsideTemperatureF { get; init; }
    public double? HighOutsideTemperatureF { get; init; }
    public double? LowOutsideTemperatureF { get; init; }
    public double? InsideTemperatureF { get; init; }

    // Humidity
    public double? InsideHumidityPercent { get; init; }
    public double? OutsideHumidityPercent { get; init; }

    // Barometer
    public double? BarometricPressureInHg { get; init; }

    // Wind
    public double? WindSpeedMph { get; init; }
    public double? WindGustMph { get; init; }
    public double? WindGustDirectionDegrees { get; init; }
    public double? WindDirectionDegrees { get; init; }
    public int WindSamples { get; init; }

    // Rain
    /// <summary>Total rain during this archive interval (in). Null = dash.</summary>
    public double? RainInches { get; init; }
    /// <summary>Highest rain rate during this archive interval (in/hr). Null = dash.</summary>
    public double? RainRateInchesPerHour { get; init; }

    // Solar / UV
    public double? SolarRadiationWm2 { get; init; }
    public double? HighSolarRadiationWm2 { get; init; }
    public double? UvIndex { get; init; }
    public double? HighUvIndex { get; init; }
    public double? EtInches { get; init; }

    // Misc
    public int? ForecastRule { get; init; }
    public int DownloadRecordType { get; init; }

    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Parse a single 52-byte rec_B archive record.
    /// Returns null if the record is unused (all 0x00 or all 0xFF in the first 4 bytes).
    /// </summary>
    public static ArchiveRecord? Parse(ReadOnlySpan<byte> buffer, int bucketType, int archiveIntervalMinutes)
    {
        if (buffer.Length < DavisProtocol.ArchiveRecordBytes)
            throw new DavisProtocolException($"Archive record buffer too short: {buffer.Length}");

        // Detect unused records
        if ((buffer[0] == 0xFF && buffer[1] == 0xFF && buffer[2] == 0xFF && buffer[3] == 0xFF) ||
            (buffer[0] == 0x00 && buffer[1] == 0x00 && buffer[2] == 0x00 && buffer[3] == 0x00))
            return null;

        ushort dateStamp = BinaryPrimitives.ReadUInt16LittleEndian(buffer[0..]);
        ushort timeStamp = BinaryPrimitives.ReadUInt16LittleEndian(buffer[2..]);
        DateTime? dt = DecodeDateTime(dateStamp, timeStamp);
        if (dt is null) return null;

        return new ArchiveRecord
        {
            DateTimeLocal          = dt.Value,
            ArchiveIntervalMinutes = archiveIntervalMinutes,
            DownloadRecordType     = buffer[30],

            OutsideTemperatureF     = DecodeTemp(BinaryPrimitives.ReadInt16LittleEndian(buffer[4..])),
            HighOutsideTemperatureF = DecodeTemp(BinaryPrimitives.ReadInt16LittleEndian(buffer[6..])),
            LowOutsideTemperatureF  = DecodeTemp(BinaryPrimitives.ReadInt16LittleEndian(buffer[8..])),

            RainInches             = Loop2Packet.DecodeRain(BinaryPrimitives.ReadUInt16LittleEndian(buffer[10..]), bucketType),
            RainRateInchesPerHour  = Loop2Packet.DecodeRain(BinaryPrimitives.ReadUInt16LittleEndian(buffer[12..]), bucketType),

            BarometricPressureInHg = BinaryPrimitives.ReadUInt16LittleEndian(buffer[14..]) is ushort b and > 0 ? b / 1000.0 : null,
            SolarRadiationWm2      = BinaryPrimitives.ReadUInt16LittleEndian(buffer[16..]) is ushort rad and not 0x7FFF ? (double)rad : null,
            WindSamples            = BinaryPrimitives.ReadUInt16LittleEndian(buffer[18..]),

            InsideTemperatureF    = DecodeTemp(BinaryPrimitives.ReadInt16LittleEndian(buffer[20..])),
            InsideHumidityPercent = buffer[22] != 0xFF ? (double)buffer[22] : null,
            OutsideHumidityPercent = buffer[23] != 0xFF ? (double)buffer[23] : null,

            WindSpeedMph              = buffer[24] != 0xFF ? (double)buffer[24] : null,
            WindGustMph               = buffer[25] != 0xFF ? (double)buffer[25] : null,
            WindGustDirectionDegrees  = buffer[26] != 0xFF ? buffer[26] * 22.5 : null,
            WindDirectionDegrees      = buffer[27] != 0xFF ? buffer[27] * 22.5 : null,

            UvIndex            = buffer[28] != 0xFF ? buffer[28] / 10.0 : null,
            EtInches           = buffer[29] / 1000.0,

            HighSolarRadiationWm2 = BinaryPrimitives.ReadUInt16LittleEndian(buffer[31..]) is ushort hrs and not 0x7FFF ? (double)hrs : null,
            HighUvIndex        = buffer[33] != 0xFF ? buffer[33] / 10.0 : null,
            ForecastRule       = buffer[34],
        };
    }

    private static double? DecodeTemp(short raw) =>
        raw != unchecked((short)0x7FFF) ? raw / 10.0 : null;

    private static DateTime? DecodeDateTime(ushort datestamp, ushort timestamp)
    {
        try
        {
            int year   = ((datestamp & 0xFE00) >> 9) + 2000;
            int month  = (datestamp & 0x01E0) >> 5;
            int day    = datestamp & 0x001F;
            int hour   = timestamp / 100;
            int minute = timestamp % 100;
            return new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Local);
        }
        catch { return null; }
    }
}
