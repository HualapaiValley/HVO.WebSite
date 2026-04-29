using System.Buffers.Binary;
using System.Text;
using HVO.Hardware.DavisVantagePro2.Protocol;

namespace HVO.Hardware.DavisVantagePro2.Tests.Fakes;

/// <summary>
/// Factory methods for building valid Davis binary protocol packets and
/// text responses for use in fake-server integration tests.
/// </summary>
public static class PacketBuilder
{
    // ── LOOP1 ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Build a 95-byte LOOP1 data buffer with known field values.
    /// All unset optional sensor fields are initialised to their null/dash sentinels.
    /// </summary>
    public static byte[] BuildLoop1DataBytes(
        double outsideTempF      = 72.5,
        double insideTempF       = 68.0,
        int    outsideHumidity   = 55,
        int    insideHumidity    = 45,
        double baroPressureInHg  = 29.500,
        int    windSpeedMph      = 8,
        int    windDirDeg        = 270,
        int    rainClicks        = 0,
        byte   txBatteryStatus   = 0,          // bitmask: bit N = channel N+1 low
        ushort consoleBatteryRaw = 5460,       // raw → ≈ 0.623 V × 300/51200
        byte   forecastIcons     = 0x08,       // 0x08 = Sunny
        byte   forecastRule      = 7,
        ushort sunriseHhmm       = 638,        // 06:38
        ushort sunsetHhmm        = 2012,       // 20:12
        ushort monthlyRainClicks = 0,
        ushort yearlyRainClicks  = 0)
    {
        var buf = new byte[95];

        // "LOO" header + bar trend (steady = 0) + packet type (LOOP1 = 0)
        buf[0] = (byte)'L'; buf[1] = (byte)'O'; buf[2] = (byte)'O';
        buf[3] = 0;
        buf[4] = DavisProtocol.PacketTypeLoop1;

        // Barometric pressure  [7-8]  uint16 LE × 1000
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(7), (ushort)(baroPressureInHg * 1000));

        // Inside temperature   [9-10] int16 LE × 10
        BinaryPrimitives.WriteInt16LittleEndian(buf.AsSpan(9), (short)(insideTempF * 10));

        // Inside humidity      [11]   byte (0xFF = null)
        buf[11] = (byte)insideHumidity;

        // Outside temperature  [12-13] int16 LE × 10
        BinaryPrimitives.WriteInt16LittleEndian(buf.AsSpan(12), (short)(outsideTempF * 10));

        // Wind speed           [14]   byte
        buf[14] = (byte)windSpeedMph;

        // Wind direction       [16-17] uint16 LE degrees
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(16), (ushort)windDirDeg);

        // Extra temps [18-24] all 0xFF (absent)
        for (int i = 18; i <= 24; i++) buf[i] = 0xFF;

        // Soil temps [25-28] all 0xFF (absent)
        for (int i = 25; i <= 28; i++) buf[i] = 0xFF;

        // Leaf temps [29-32] all 0xFF (absent — 4 leaf temp bytes per Davis spec)
        for (int i = 29; i <= 32; i++) buf[i] = 0xFF;

        // Outside humidity     [33]   byte
        buf[33] = (byte)outsideHumidity;

        // Extra humidities [34-40] all 0xFF (absent)
        for (int i = 34; i <= 40; i++) buf[i] = 0xFF;

        // Rain rate   [41-42] uint16 LE clicks
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(41), 0);

        // UV [43] = 0xFF (null), Solar [44-45] = 0x7FFF (null)
        buf[43] = 0xFF;
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(44), 0x7FFF);

        // Storm rain [46-47] = 0xFFFF (null), storm start [48-49] = 0xFFFF
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(46), 0xFFFF);
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(48), 0xFFFF);

        // Daily rain  [50-51]
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(50), (ushort)rainClicks);

        // Monthly rain [52-53]
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(52), monthlyRainClicks);

        // Yearly rain  [54-55]
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(54), yearlyRainClicks);

        // Daily ET    [56-57] = 0 (uint16 LE × 1000)
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(56), 0);

        // Monthly ET  [58-59] = 0
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(58), 0);

        // Yearly ET   [60-61] = 0
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(60), 0);

        // Soil moistures [62-65] all 0xFF
        for (int i = 62; i <= 65; i++) buf[i] = 0xFF;

        // Leaf wetness   [66-69] all 0xFF
        for (int i = 66; i <= 69; i++) buf[i] = 0xFF;

        // Alarm bytes [70-85] default to 0 (no alarms)

        // Transmitter battery [86] single byte (bit N = channel N+1 low)
        buf[86] = txBatteryStatus;

        // Console battery     [87-88] uint16 LE
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(87), consoleBatteryRaw);

        // Forecast icons [89], rule [90]
        buf[89] = forecastIcons;
        buf[90] = forecastRule;

        // Sunrise [91-92], sunset [93-94] uint16 LE HHMM
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(91), sunriseHhmm);
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(93), sunsetHhmm);

        return buf;
    }

    // ── LOOP2 ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Build a 95-byte LOOP2 data buffer with known field values.
    /// All unset optional fields are initialised to their null/dash sentinels.
    /// </summary>
    public static byte[] BuildLoop2DataBytes(
        double outsideTempF        = 72.5,
        double insideTempF         = 68.0,
        int    outsideHumidity     = 55,
        int    insideHumidity      = 45,
        double baroPressureInHg    = 29.500,
        int    windSpeedMph        = 8,
        int    windDirDeg          = 270,
        double dewPointF           = 55.0,
        int    rainClicks          = 0)
    {
        var buf = new byte[95];

        // "LOO" header + bar trend (steady = 0) + packet type (LOOP2 = 1)
        buf[0] = (byte)'L'; buf[1] = (byte)'O'; buf[2] = (byte)'O';
        buf[3] = 0;
        buf[4] = DavisProtocol.PacketTypeLoop2;

        // Barometric pressure  [7-8]  uint16 LE × 1000
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(7), (ushort)(baroPressureInHg * 1000));

        // Inside temperature   [9-10] int16 LE × 10
        BinaryPrimitives.WriteInt16LittleEndian(buf.AsSpan(9), (short)(insideTempF * 10));

        // Inside humidity      [11]   byte (0xFF = null)
        buf[11] = (byte)insideHumidity;

        // Outside temperature  [12-13] int16 LE × 10
        BinaryPrimitives.WriteInt16LittleEndian(buf.AsSpan(12), (short)(outsideTempF * 10));

        // Wind speed           [14]   byte (0xFF = null)
        buf[14] = (byte)windSpeedMph;

        // Wind direction       [16-17] uint16 LE, degrees 1–360 (0 or 0x7FFF = null)
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(16), (ushort)windDirDeg);

        // 10-min avg wind      [18-19] uint16 LE × 10 (0xFFFF = null)
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(18), 0xFFFF);

        // 2-min avg wind       [20-21] uint16 LE × 10 (0xFFFF = null)
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(20), 0xFFFF);

        // Wind gust 10-min     [22]   byte (0xFF = null)
        buf[22] = 0xFF;

        // Wind gust dir 10-min [24-25] uint16 LE (0x7FFF = null)
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(24), 0x7FFF);

        // Dew point            [30-31] int16 LE, direct °F (0x7FFF = null)
        BinaryPrimitives.WriteInt16LittleEndian(buf.AsSpan(30), (short)dewPointF);

        // Outside humidity     [33]   byte (0xFF = null)
        buf[33] = (byte)outsideHumidity;

        // Heat index           [35-36] int16 LE (0x7FFF = null)
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(35), 0x7FFF);

        // Wind chill           [37-38] int16 LE, lower byte 0xFF = null
        buf[37] = 0xFF; buf[38] = 0;

        // Rain rate            [41-42] uint16 LE clicks
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(41), (ushort)rainClicks);

        // UV index             [43]   byte × 10 (0xFF = null)
        buf[43] = 0xFF;

        // Solar radiation      [44-45] uint16 LE (0x7FFF = null)
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(44), 0x7FFF);

        // Storm rain           [46-47] uint16 LE clicks (0xFFFF = null)
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(46), 0xFFFF);

        // Storm start date     [48-49] uint16 LE (0xFFFF = no storm)
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(48), 0xFFFF);

        // Daily rain           [50-51] uint16 LE clicks
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(50), (ushort)rainClicks);

        // 15-min rain          [52-53] uint16 LE clicks
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(52), 0);

        // Hour rain            [54-55] uint16 LE clicks
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(54), 0);

        // Daily ET             [56-57] uint16 LE × 1000
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(56), 0);

        // 24-hr rain           [58-59] uint16 LE clicks
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(58), 0);

        return buf;
    }

    /// <summary>
    /// Build a valid 99-byte LOOP2 packet:
    /// 95 data bytes + 2 zero end bytes + 2 CRC bytes
    /// (CRC is computed over the full 97-byte data frame).
    /// </summary>
    /// <summary>
    /// Build a valid 99-byte LOOP1 packet:
    /// 95 data bytes + 2 end bytes (0x0A, 0x0D) + 2 CRC bytes.
    /// </summary>
    public static byte[] BuildLoop1Packet(
        double outsideTempF      = 72.5,
        double insideTempF       = 68.0,
        int    outsideHumidity   = 55,
        int    insideHumidity    = 45,
        double baroPressureInHg  = 29.500,
        int    windSpeedMph      = 8,
        int    windDirDeg        = 270,
        int    rainClicks        = 0,
        byte   txBatteryStatus   = 0,
        ushort consoleBatteryRaw = 5460,
        byte   forecastIcons     = 0x08,
        byte   forecastRule      = 7,
        ushort sunriseHhmm       = 638,
        ushort sunsetHhmm        = 2012)
    {
        byte[] data = BuildLoop1DataBytes(outsideTempF, insideTempF, outsideHumidity,
            insideHumidity, baroPressureInHg, windSpeedMph, windDirDeg, rainClicks,
            txBatteryStatus, consoleBatteryRaw, forecastIcons, forecastRule,
            sunriseHhmm, sunsetHhmm);

        var frame = new byte[97];
        data.CopyTo(frame, 0);
        frame[95] = 0x0A;
        frame[96] = 0x0D;

        return CrcCalculator.AppendCrc(frame); // 99 bytes
    }

    public static byte[] BuildLoop2Packet(
        double outsideTempF     = 72.5,
        double insideTempF      = 68.0,
        int    outsideHumidity  = 55,
        int    insideHumidity   = 45,
        double baroPressureInHg = 29.500,
        int    windSpeedMph     = 8,
        int    windDirDeg       = 270,
        double dewPointF        = 55.0,
        int    rainClicks       = 0)
    {
        byte[] data = BuildLoop2DataBytes(outsideTempF, insideTempF, outsideHumidity,
            insideHumidity, baroPressureInHg, windSpeedMph, windDirDeg, dewPointF, rainClicks);

        // 97-byte frame: 95 data + 2 zero end bytes (exact value not used by parser)
        var frame = new byte[97];
        data.CopyTo(frame, 0);
        // frame[95] = 0x0A, frame[96] = 0x0D per Davis protocol
        frame[95] = 0x0A;
        frame[96] = 0x0D;

        return CrcCalculator.AppendCrc(frame); // 99 bytes, CRC over all 97 preceding bytes
    }

    // ── Archive record ────────────────────────────────────────────────────────

    /// <summary>Build a valid 52-byte rec_B archive record buffer.</summary>
    public static byte[] BuildArchiveDataBytes(
        DateTime dateTime                = default,
        double   outsideTempF            = 65.5,
        double   highOutsideTempF        = 70.0,
        double   lowOutsideTempF         = 60.0,
        double   insideTempF             = 72.0,
        int      outsideHumidity         = 60,
        int      insideHumidity          = 40,
        double   barometricPressureInHg  = 29.800,
        int      windSpeedMph            = 5,
        int      windGustMph             = 10,
        int      windDirOctet            = 8,    // 8 × 22.5 = 180° (S)
        int      windGustDirOctet        = 8,
        int      rainClicks              = 0,
        // Extra sensors per rec_B_schema (0xFF = absent)
        byte     leafTemp1Raw            = 0xFF,  // byte 34
        byte     leafTemp2Raw            = 0xFF,  // byte 35
        byte[]?  leafWetnessRaw          = null,   // 2 bytes [36-37]
        byte[]?  soilTempsRaw            = null,   // 4 bytes [38-41]
        byte[]?  extraHumiditiesRaw      = null,   // 2 bytes [43-44]
        byte[]?  extraTempsRaw           = null,   // 3 bytes [45-47]
        byte[]?  soilMoisturesRaw        = null)   // 4 bytes [48-51]
    {
        if (dateTime == default)
            dateTime = new DateTime(2024, 6, 15, 14, 30, 0, DateTimeKind.Local);

        var buf = new byte[52];

        // Date stamp [0-1]:  year[15:9] | month[8:5] | day[4:0]
        int year      = dateTime.Year - 2000;
        int dateStamp = dateTime.Day | (dateTime.Month << 5) | (year << 9);
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(0), (ushort)dateStamp);

        // Time stamp [2-3]:  hour × 100 + minute
        int timeStamp = dateTime.Hour * 100 + dateTime.Minute;
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(2), (ushort)timeStamp);

        // Outside temperatures [4-9] int16 LE × 10
        BinaryPrimitives.WriteInt16LittleEndian(buf.AsSpan(4), (short)(outsideTempF     * 10));
        BinaryPrimitives.WriteInt16LittleEndian(buf.AsSpan(6), (short)(highOutsideTempF * 10));
        BinaryPrimitives.WriteInt16LittleEndian(buf.AsSpan(8), (short)(lowOutsideTempF  * 10));

        // Rain [10-13] uint16 LE clicks
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(10), (ushort)rainClicks);
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(12), 0); // rain rate

        // Barometric pressure [14-15] uint16 LE × 1000
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(14), (ushort)(barometricPressureInHg * 1000));

        // Solar radiation [16-17] uint16 LE (0x7FFF = null)
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(16), 0x7FFF);

        // Wind samples [18-19]
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(18), 100);

        // Inside temperature [20-21] int16 LE × 10
        BinaryPrimitives.WriteInt16LittleEndian(buf.AsSpan(20), (short)(insideTempF * 10));

        // Inside humidity [22], outside humidity [23]
        buf[22] = (byte)insideHumidity;
        buf[23] = (byte)outsideHumidity;

        // Wind speed [24], gust [25], gust dir [26], dir [27]
        buf[24] = (byte)windSpeedMph;
        buf[25] = (byte)windGustMph;
        buf[26] = (byte)windGustDirOctet;
        buf[27] = (byte)windDirOctet;

        // UV [28] = 0xFF (null), ET [29] = 0
        buf[28] = 0xFF;
        buf[29] = 0;

        // High solar radiation [30-31] = 0x7FFF (null)
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(30), 0x7FFF);

        // High UV [32] = 0xFF (null), ForecastRule [33] = 0
        buf[32] = 0xFF;
        buf[33] = 0;

        // Extra sensors per rec_B_schema:
        // [34] leafTemp1  [35] leafTemp2
        buf[34] = leafTemp1Raw;
        buf[35] = leafTemp2Raw;

        // [36-37] leafWet1-2
        var leafWetness = leafWetnessRaw ?? new byte[2];
        if (leafWetness.Length < 2) Array.Resize(ref leafWetness, 2);
        for (int i = 0; i < 2; i++) buf[36 + i] = leafWetness[i] == 0 ? (byte)0xFF : leafWetness[i];

        // [38-41] soilTemp1-4
        var soilTemps = soilTempsRaw ?? new byte[4];
        if (soilTemps.Length < 4) Array.Resize(ref soilTemps, 4);
        for (int i = 0; i < 4; i++) buf[38 + i] = soilTemps[i] == 0 ? (byte)0xFF : soilTemps[i];

        // [42] download_record_type = 1 (valid)
        buf[42] = 1;

        // [43-44] extraHumid1-2
        var extraHumids = extraHumiditiesRaw ?? new byte[2];
        if (extraHumids.Length < 2) Array.Resize(ref extraHumids, 2);
        for (int i = 0; i < 2; i++) buf[43 + i] = extraHumids[i] == 0 ? (byte)0xFF : extraHumids[i];

        // [45-47] extraTemp1-3
        var extraTemps = extraTempsRaw ?? new byte[3];
        if (extraTemps.Length < 3) Array.Resize(ref extraTemps, 3);
        for (int i = 0; i < 3; i++) buf[45 + i] = extraTemps[i] == 0 ? (byte)0xFF : extraTemps[i];

        // [48-51] soilMoist1-4
        var soilMoistures = soilMoisturesRaw ?? new byte[4];
        if (soilMoistures.Length < 4) Array.Resize(ref soilMoistures, 4);
        for (int i = 0; i < 4; i++) buf[48 + i] = soilMoistures[i] == 0 ? (byte)0xFF : soilMoistures[i];

        return buf;
    }

    // ── Binary response builders ──────────────────────────────────────────────

    /// <summary>Build a valid 8-byte GETTIME response (6 time bytes + 2 CRC bytes).</summary>
    public static byte[] BuildGetTimeResponse(DateTime t)
    {
        byte[] data =
        [
            (byte)t.Second, (byte)t.Minute, (byte)t.Hour,
            (byte)t.Day,    (byte)t.Month,  (byte)(t.Year - 1900)
        ];
        return CrcCalculator.AppendCrc(data);
    }

    /// <summary>Build a valid EEPROM response: N data bytes + 2 CRC bytes.</summary>
    public static byte[] BuildEepromResponse(params byte[] data) =>
        CrcCalculator.AppendCrc(data);

    /// <summary>
    /// Build a 6-byte DMPAFT header response:
    /// nPages (2 bytes LE) + startIndex (2 bytes LE) + 2 CRC bytes.
    /// </summary>
    public static byte[] BuildDmpaftHeader(int nPages, int startIndex = 0)
    {
        var data = new byte[4];
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0), (ushort)nPages);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(2), (ushort)startIndex);
        return CrcCalculator.AppendCrc(data); // 6 bytes total
    }

    /// <summary>
    /// Build a 267-byte archive page:
    /// 1 seq byte + 5 × 52-byte records + 4 unused + 2 CRC.
    /// Pass up to 5 record buffers (52 bytes each).
    /// Unset record slots are zeroed (first 4 bytes = 0x00 = null sentinel).
    /// </summary>
    public static byte[] BuildArchivePage(int seqByte = 0, params byte[][] records)
    {
        // 265 bytes of payload (without 2 CRC bytes)
        var page = new byte[265];
        page[0] = (byte)seqByte;
        for (int i = 0; i < Math.Min(records.Length, DavisProtocol.ArchiveRecordsPerPage); i++)
            records[i].CopyTo(page, 1 + i * DavisProtocol.ArchiveRecordBytes);
        return CrcCalculator.AppendCrc(page); // 267 bytes total
    }

    /// <summary>
    /// Prepend a single ACK byte (0x06) before a CRC-appended data block.
    /// The client reads the ACK via SendDataAsync then the data via GetDataWithCrc16Async.
    /// </summary>
    public static byte[] AckThenData(byte[] crcAppendedData)
    {
        var result = new byte[1 + crcAppendedData.Length];
        result[0] = DavisProtocol.Ack;
        crcAppendedData.CopyTo(result, 1);
        return result;
    }

    // ── Text response builder ─────────────────────────────────────────────────

    /// <summary>
    /// Build a Davis text-command response:  "\nOK\n\r{line}\n\r" for each line.
    /// After ASCII decoding and Trim() the client sees "OK\n\r..." which it splits
    /// to get the data lines.
    /// </summary>
    public static byte[] BuildTextResponse(params string[] lines)
    {
        var sb = new StringBuilder();
        sb.Append("\nOK\n\r");
        foreach (string line in lines)
            sb.Append(line).Append("\n\r");
        return Encoding.ASCII.GetBytes(sb.ToString());
    }
}
