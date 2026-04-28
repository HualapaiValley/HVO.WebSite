using System.Buffers.Binary;

namespace HVO.Hardware.DavisVantagePro2.Protocol.Packets;

/// <summary>
/// Decoded LOOP2 packet from a Davis Vantage Pro 2 console.
/// Field offsets and decoding rules are derived from the Davis serial protocol
/// specification and the WeeWX vantage driver (loop2_schema + _loop_map).
/// </summary>
public sealed record Loop2Packet
{
    // ── Identity ──────────────────────────────────────────────────────────────
    public DateTime RecordedAtUtc { get; init; }

    // ── Atmosphere ────────────────────────────────────────────────────────────
    /// <summary>Station-corrected barometric pressure (inHg). Null = dash.</summary>
    public double? BarometricPressureInHg { get; init; }

    /// <summary>Raw (uncalibrated) station pressure (inHg). Null = dash.</summary>
    public double? PressureRawInHg { get; init; }

    /// <summary>Altimeter pressure (inHg). Null = dash.</summary>
    public double? AltimeterInHg { get; init; }

    // ── Temperature ───────────────────────────────────────────────────────────
    /// <summary>Outside temperature (°F). Null = dash/sensor error.</summary>
    public double? OutsideTemperatureF { get; init; }

    /// <summary>Dew point (°F), computed on console. Null = unavailable.</summary>
    public double? DewPointF { get; init; }

    /// <summary>Heat index (°F), computed on console. Null = unavailable.</summary>
    public double? HeatIndexF { get; init; }

    /// <summary>Wind chill (°F), computed on console. Null = unavailable.</summary>
    public double? WindChillF { get; init; }

    /// <summary>THSW index (°F). Null = unavailable.</summary>
    public double? ThswF { get; init; }

    /// <summary>Inside (console) temperature (°F). Null = dash/sensor error.</summary>
    public double? InsideTemperatureF { get; init; }

    // ── Humidity ──────────────────────────────────────────────────────────────
    /// <summary>Outside relative humidity (%). Null = dash/sensor error.</summary>
    public double? OutsideHumidityPercent { get; init; }

    /// <summary>Inside (console) relative humidity (%). Null = dash/sensor error.</summary>
    public double? InsideHumidityPercent { get; init; }

    // ── Wind ──────────────────────────────────────────────────────────────────
    /// <summary>Instantaneous wind speed (mph). Null = dash/calm.</summary>
    public double? WindSpeedMph { get; init; }

    /// <summary>Instantaneous wind direction (degrees, 0=N, 90=E). Null = calm/dash.</summary>
    public double? WindDirectionDegrees { get; init; }

    /// <summary>10-minute average wind speed (mph). Null = dash.</summary>
    public double? WindSpeed10MinAvgMph { get; init; }

    /// <summary>2-minute average wind speed (mph). Null = dash.</summary>
    public double? WindSpeed2MinAvgMph { get; init; }

    /// <summary>10-minute wind gust speed (mph). Null = dash.</summary>
    public double? WindGust10MinMph { get; init; }

    /// <summary>Direction of 10-minute wind gust (degrees). Null = calm/dash.</summary>
    public double? WindGust10MinDirectionDegrees { get; init; }

    // ── Rain ──────────────────────────────────────────────────────────────────
    /// <summary>Instantaneous rain rate (in/hr, decoded from bucket tips). Null = none.</summary>
    public double? RainRateInchesPerHour { get; init; }

    /// <summary>Cumulative rain since midnight (in). Null = dash.</summary>
    public double? DailyRainInches { get; init; }

    /// <summary>Rain in the last 15 minutes (in). Null = dash.</summary>
    public double? Rain15MinInches { get; init; }

    /// <summary>Rain in the last 60 minutes (in). Null = dash.</summary>
    public double? HourRainInches { get; init; }

    /// <summary>Rain in the last 24 hours (in). Null = dash.</summary>
    public double? Rain24HourInches { get; init; }

    /// <summary>Storm total rain (in, since StormStartDate). Null = no active storm.</summary>
    public double? StormRainInches { get; init; }

    /// <summary>Storm start date. Null = no active storm.</summary>
    public DateTime? StormStartDate { get; init; }

    // ── Solar / UV ────────────────────────────────────────────────────────────
    /// <summary>Solar radiation (W/m²). Null = dash/sensor absent.</summary>
    public double? SolarRadiationWm2 { get; init; }

    /// <summary>UV index. Null = dash/sensor absent.</summary>
    public double? UvIndex { get; init; }

    /// <summary>Daily ET (in × 1000). Null = not available.</summary>
    public double? DailyEtInches { get; init; }

    // ── Barometric trend ──────────────────────────────────────────────────────
    /// <summary>Barometric trend icon (-2=FF, -1=F, 0=S, 1=R, 2=RR). Null = unknown.</summary>
    public int? BarometricTrend { get; init; }

    // ─────────────────────────────────────────────────────────────────────────
    //  Factory — parse from raw 95-byte LOOP2 data buffer
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Parse a LOOP2 packet from the first 95 bytes of the 99-byte console response.
    /// Validates the "LOO" marker and packet type byte (must be 1).
    /// </summary>
    /// <param name="buffer">95-byte data portion (no CRC).</param>
    /// <param name="bucketType">Rain bucket type from EEPROM (0=0.01in, 1=0.2mm, 2=0.1mm).</param>
    public static Loop2Packet Parse(ReadOnlySpan<byte> buffer, int bucketType)
    {
        if (buffer.Length < DavisProtocol.LoopPacketDataBytes)
            throw new DavisProtocolException($"LOOP2 buffer too short: {buffer.Length} < {DavisProtocol.LoopPacketDataBytes}");

        if (buffer[0] != 'L' || buffer[1] != 'O' || buffer[2] != 'O')
            throw new DavisProtocolException("LOOP2 packet missing 'LOO' marker");

        if (buffer[4] == DavisProtocol.PacketTypeLoop1)
            return ParseLoop1(buffer, bucketType);

        if (buffer[4] != DavisProtocol.PacketTypeLoop2)
            throw new DavisUnknownPacketTypeException(buffer[4]);

        return new Loop2Packet
        {
            RecordedAtUtc = DateTime.UtcNow,

            BarometricPressureInHg = ReadUshort(buffer, 7) is ushort bar and > 0
                ? bar / 1000.0 : null,
            PressureRawInHg = ReadUshort(buffer, 65) is ushort pr and > 0
                ? pr / 1000.0 : null,
            AltimeterInHg = ReadUshort(buffer, 69) is ushort alt and > 0
                ? alt / 1000.0 : null,

            InsideTemperatureF = DecodeSignedTemp(buffer, 9),
            InsideHumidityPercent = buffer[11] != 0xFF ? (double)buffer[11] : null,

            OutsideTemperatureF = DecodeSignedTemp(buffer, 12),
            DewPointF = DecodeSignedFahrenheit(buffer, 30),
            HeatIndexF = DecodeSignedFahrenheit(buffer, 35),
            WindChillF = DecodeWindChill(buffer, 37),
            ThswF = DecodeSignedFahrenheit(buffer, 39),

            OutsideHumidityPercent = buffer[33] != 0xFF ? (double)buffer[33] : null,

            WindSpeedMph = buffer[14] != 0xFF ? (double)buffer[14] : null,
            WindDirectionDegrees = DecodeWindDir16(buffer, 16),
            WindSpeed10MinAvgMph = DecodeWindSpeedLoop2(buffer, 18),
            WindSpeed2MinAvgMph = DecodeWindSpeedLoop2(buffer, 20),
            WindGust10MinMph = buffer[22] != 0xFF ? (double)buffer[22] : null,
            WindGust10MinDirectionDegrees = DecodeWindDir16(buffer, 24),

            RainRateInchesPerHour = DecodeRain(ReadUshort(buffer, 41), bucketType),
            StormRainInches = DecodeRain(ReadUshort(buffer, 46), bucketType),
            StormStartDate = DecodeStormStart(ReadUshort(buffer, 48)),
            DailyRainInches = DecodeRain(ReadUshort(buffer, 50), bucketType),
            Rain15MinInches = DecodeRain(ReadUshort(buffer, 52), bucketType),
            HourRainInches = DecodeRain(ReadUshort(buffer, 54), bucketType),
            Rain24HourInches = DecodeRain(ReadUshort(buffer, 58), bucketType),

            DailyEtInches = ReadUshort(buffer, 56) / 1000.0,

            UvIndex = buffer[43] != 0xFF ? buffer[43] / 10.0 : null,
            SolarRadiationWm2 = ReadUshort(buffer, 44) is ushort rad and not 0x7FFF
                ? (double)rad : null,

            BarometricTrend = (sbyte)buffer[3] is sbyte trend
                and (>= -3 and <= 3) ? (int)trend : null,
        };
    }

    // ── Decode helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// Parse a LOOP (LOOP1) packet. Field offsets differ from LOOP2.
    /// LOOP2-only fields (dew point, heat index, wind chill, raw pressure, etc.) are returned as null.
    /// </summary>
    private static Loop2Packet ParseLoop1(ReadOnlySpan<byte> buffer, int bucketType) =>
        new()
        {
            RecordedAtUtc = DateTime.UtcNow,

            BarometricPressureInHg = ReadUshort(buffer, 7) is ushort bar and > 0
                ? bar / 1000.0 : null,

            InsideTemperatureF = DecodeSignedTemp(buffer, 9),
            InsideHumidityPercent = buffer[11] != 0xFF ? (double)buffer[11] : null,

            OutsideTemperatureF = DecodeSignedTemp(buffer, 12),

            WindSpeedMph = buffer[14] != 0xFF ? (double)buffer[14] : null,
            WindSpeed10MinAvgMph = buffer[15] != 0xFF ? (double)buffer[15] : null,
            WindDirectionDegrees = DecodeWindDir16(buffer, 16),

            OutsideHumidityPercent = buffer[30] != 0xFF ? (double)buffer[30] : null,

            RainRateInchesPerHour = DecodeRain(ReadUshort(buffer, 38), bucketType),
            UvIndex = buffer[40] != 0xFF ? buffer[40] / 10.0 : null,
            SolarRadiationWm2 = ReadUshort(buffer, 41) is ushort rad and not 0x7FFF
                ? (double)rad : null,

            StormRainInches = DecodeRain(ReadUshort(buffer, 43), bucketType),
            StormStartDate = DecodeStormStart(ReadUshort(buffer, 45)),
            DailyRainInches = DecodeRain(ReadUshort(buffer, 47), bucketType),
            DailyEtInches = ReadUshort(buffer, 53) is ushort et and > 0
                ? et / 1000.0 : null,
        };

    // ── Decode helpers ────────────────────────────────────────────────────────

    private static ushort ReadUshort(ReadOnlySpan<byte> b, int offset) =>
        BinaryPrimitives.ReadUInt16LittleEndian(b[offset..]);

    private static short ReadShort(ReadOnlySpan<byte> b, int offset) =>
        BinaryPrimitives.ReadInt16LittleEndian(b[offset..]);

    /// <summary>Decode a signed 16-bit temperature: value × 10 = °F. 0x7FFF = null.</summary>
    private static double? DecodeSignedTemp(ReadOnlySpan<byte> b, int offset)
    {
        short raw = ReadShort(b, offset);
        return raw != unchecked((short)0x7FFF) ? raw / 10.0 : null;
    }

    /// <summary>Decode a signed 16-bit °F value stored directly (not × 10). 0x7FFF = null.</summary>
    private static double? DecodeSignedFahrenheit(ReadOnlySpan<byte> b, int offset)
    {
        short raw = ReadShort(b, offset);
        return (raw & 0xFFFF) != 0x7FFF ? (double)raw : null;
    }

    /// <summary>Wind chill: 0x00FF in the lower byte = null.</summary>
    private static double? DecodeWindChill(ReadOnlySpan<byte> b, int offset)
    {
        short raw = ReadShort(b, offset);
        return (raw & 0x00FF) != 0xFF ? (double)raw : null;
    }

    private static double? DecodeWindDir16(ReadOnlySpan<byte> b, int offset)
    {
        ushort raw = ReadUshort(b, offset);
        if (raw == 0x7FFF || raw == 0) return null;
        return raw == 360 ? 0.0 : (double)raw;
    }

    /// <summary>LOOP2 wind speed fields (10-min avg, 2-min avg) are × 10. 0xFFFF = null.</summary>
    private static double? DecodeWindSpeedLoop2(ReadOnlySpan<byte> b, int offset)
    {
        ushort raw = ReadUshort(b, offset);
        return raw != 0xFFFF ? raw / 10.0 : null;
    }

    /// <summary>
    /// Decode rain click counts to inches using the configured bucket type.
    /// 0xFFFF = dash/null.
    /// </summary>
    internal static double? DecodeRain(ushort clicks, int bucketType)
    {
        if (clicks == 0xFFFF) return null;
        return bucketType switch
        {
            DavisProtocol.BucketType001Inch => clicks / 100.0,
            DavisProtocol.BucketType02Mm => clicks * 0.0078740157,
            DavisProtocol.BucketType01Mm => clicks * 0.00393700787,
            _ => null
        };
    }

    /// <summary>
    /// Decode the LOOP storm start date encoding.
    /// Bits: [14:12]=month, [11:7]=day, [6:0]=year-2000. 0xFFFF = no storm.
    /// </summary>
    private static DateTime? DecodeStormStart(ushort raw)
    {
        if (raw == 0xFFFF) return null;
        int year = (raw & 0x007F) + 2000;
        int month = (raw & 0xF000) >> 12;
        int day = (raw & 0x0F80) >> 7;
        try { return new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Local); }
        catch { return null; }
    }
}
