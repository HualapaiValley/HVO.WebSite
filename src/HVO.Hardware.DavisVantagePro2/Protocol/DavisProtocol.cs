namespace HVO.Hardware.DavisVantagePro2.Protocol;

/// <summary>Constants for the Davis serial/TCP protocol.</summary>
internal static class DavisProtocol
{
    // Control bytes
    public const byte Ack = 0x06;
    public const byte Nak = 0x21; // '!'
    public const byte Cancel = 0x18; // Ctrl-X
    public const byte Cr = 0x0D;
    public const byte Lf = 0x0A;
    public const byte Dash16 = 0xFF; // 1-byte null/dash sentinel
    public const ushort Dash16U = 0x7FFF; // 2-byte null/dash for signed temps

    // Wake sequence
    public static readonly byte[] WakeBytes = "\n\n\n"u8.ToArray();
    public static readonly byte[] WakeNl = "\n"u8.ToArray();
    public static readonly byte[] WakeAck = [Lf, Cr]; // \n\r

    // Commands
    public const string CmdLoop = "LOOP";
    public const string CmdLps = "LPS";
    public const string CmdDmpaft = "DMPAFT";
    public const string CmdDmp = "DMP";
    public const string CmdGettime = "GETTIME";
    public const string CmdSettime = "SETTIME";
    public const string CmdBardata = "BARDATA";
    public const string CmdRxcheck = "RXCHECK";
    public const string CmdReceivers = "RECEIVERS";
    public const string CmdNver = "NVER";
    public const string CmdVer = "VER";
    public const string CmdWrd = "WRD";
    public const string CmdNewsetup = "NEWSETUP";
    public const string CmdClrlog = "CLRLOG";
    public const string CmdClralm = "CLRALM";
    public const string CmdClrbits = "CLRBITS";
    public const string CmdEebrd = "EEBRD";
    public const string CmdEebwr = "EEBWR";
    public const string CmdLamps = "LAMPS";
    public const string CmdSetper = "SETPER";
    public const string CmdBar = "BAR=";

    // WRD discriminator bytes (hardware detection)
    public static readonly byte[] WrdBytes = [0x12, 0x4D];
    public const byte HardwareVantagePro = 16;
    public const byte HardwareVantageVue = 17;

    // Packet sizes
    public const int LoopPacketTotalBytes = 99; // 95 data + 2 CRC + 2 end bytes
    public const int LoopPacketDataBytes = 95;
    public const int ArchivePageBytes = 267; // 1 page byte + 5 records × 52 + 4 unused bytes + 2 CRC
    public const int ArchiveRecordBytes = 52;
    public const int ArchiveRecordsPerPage = 5;
    public const int DmpaftResponseBytes = 6;  // 2 pages + 2 start index + 2 CRC

    // LOOP2 packet type
    public const byte PacketTypeLoop1 = 0;
    public const byte PacketTypeLoop2 = 1;

    // TCP send delay required by WeatherLink IP (seconds)
    public const double TcpSendDelaySeconds = 0.05;

    // Rain bucket types (from EEPROM setup bits [5:4])
    public const int BucketType001Inch = 0;  // 0.01 inch bucket (US standard)
    public const int BucketType02Mm = 1;  // 0.2 mm bucket
    public const int BucketType01Mm = 2;  // 0.1 mm bucket

    // EEPROM addresses (from Davis serial protocol doc + weewx vantage driver)
    public const ushort EepromUnitBits = 0x29; // display unit settings
    public const ushort EepromSetupBits = 0x2B; // wind cup, rain bucket, latitude sign
    public const ushort EepromRainYearStart = 0x2C; // rain year start month (1=Jan)
    public const ushort EepromArchiveInterval = 0x2D; // archive interval in minutes
    public const ushort EepromAltitude = 0x0F; // signed short, feet
    public const ushort EepromLatitude = 0x0B; // signed short, × 10 degrees
    public const ushort EepromLongitude = 0x0D; // signed short, × 10 degrees
    public const ushort EepromTimezoneCode = 0x11;
    public const ushort EepromManOrAuto = 0x12; // DST manual/auto
    public const ushort EepromDaylightSavings = 0x13;
    public const ushort EepromGmtOffset = 0x14; // signed short, hundredths of hours
    public const ushort EepromGmtOrZone = 0x16;
    public const ushort EepromUseTx = 0x17; // which channels to receive
    public const ushort EepromRetransmit = 0x18; // retransmit channel
    public const ushort EepromTransmitters = 0x19; // 16 bytes, 2 per channel × 8
    public const ushort EepromTempCalib = 0x32; // "<27bh" — temp calibrations
    public const ushort EepromWindDirCalib = 0x4D; // signed short — wind dir offset
    public const ushort EepromAlarmStart = 0x52; // 94-byte alarm threshold block
    public const int EepromAlarmBlockSize = 94;
    public const ushort EepromInHumidCalib = 0x44; // in humidity offset (signed byte)
    public const ushort EepromOutHumidCalib = 0x45; // out humidity offset
    public const ushort EepromTempLogging = 0xFFC; // 0=AVERAGE, 1=LAST
}
