using System.Buffers.Binary;
using System.Text;
using HVO.Hardware.DavisVantagePro2.Protocol;
using HVO.Hardware.DavisVantagePro2.Protocol.Packets;
using HVO.Hardware.DavisVantagePro2.Station.Models;
using Microsoft.Extensions.Logging;

namespace HVO.Hardware.DavisVantagePro2.Station;

/// <summary>
/// High-level interface to the Davis Vantage Pro 2 console.
/// Wraps <see cref="DavisConsoleClient"/> with all read/write operations
/// described in the Davis Serial Communication Reference Manual.
///
/// All console operations are serialized via an internal semaphore.
/// The worker that polls LOOP packets must release the lock between batches
/// so admin commands can interleave.
/// </summary>
public sealed class VantageStation : IAsyncDisposable
{
    private readonly DavisConsoleClient _client;
    private readonly ILogger<VantageStation> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly int _maxTries;

    // Cached EEPROM values (populated on Connect)
    public int RainBucketType { get; private set; } = DavisProtocol.BucketType001Inch;
    public int ArchiveIntervalSeconds { get; private set; } = 300;
    public int ModelType { get; private set; } = 2;
    public int HardwareType { get; private set; }
    public bool UseTimezoneCode { get; private set; } = true;
    public int TimezoneCode { get; private set; }
    public double GmtOffsetHours { get; private set; }
    public bool IsConnected => _client.IsConnected;

    /// <summary>
    /// UTC offset of the console's configured timezone, derived from EEPROM at connect time.
    /// Uses the timezone code table when <see cref="UseTimezoneCode"/> is <c>true</c>;
    /// otherwise uses the manual GMT offset.
    /// </summary>
    public TimeSpan ConsoleUtcOffset => UseTimezoneCode
        ? DavisTimeZoneTable.GetOffset(TimezoneCode) ?? TimeSpan.Zero
        : TimeSpan.FromHours(GmtOffsetHours);

    /// <summary>Human-readable timezone label, e.g. "Mountain (UTC-7)".</summary>
    public string ConsoleTimeZoneLabel => UseTimezoneCode
        ? DavisTimeZoneTable.GetLabel(TimezoneCode)
        : $"GMT {(GmtOffsetHours >= 0 ? "+" : "")}{GmtOffsetHours:F2} h";

    public VantageStation(DavisConsoleClient client, ILogger<VantageStation> logger, int maxTries = 4)
    {
        _client = client;
        _logger = logger;
        _maxTries = maxTries;
    }

    // ── Connection ────────────────────────────────────────────────────────────

    /// <summary>Open the TCP connection, wake the console, and read EEPROM setup values.</summary>
    public async Task ConnectAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            await _client.OpenAsync(ct);
            await _client.WakeAsync(_maxTries, ct);
            await ReadSetupFromEepromAsync(ct);
            _logger.LogInformation("Station connected. RainBucket={B}, ArchiveInterval={I}s, Model={M}",
                RainBucketType, ArchiveIntervalSeconds, ModelType);
        }
        finally { _lock.Release(); }
    }

    /// <summary>Close the connection gracefully.</summary>
    public async Task DisconnectAsync()
    {
        await _lock.WaitAsync();
        try { _client.Close(); }
        finally { _lock.Release(); }
    }

    // ── Live data — LOOP2 streaming ───────────────────────────────────────────

    /// <summary>
    /// Request a batch of LOOP2 packets from the console using the LPS command.
    /// Each packet is yielded as it arrives. The caller must release the lock by
    /// cancelling the token or consuming all packets.
    /// </summary>
    public async IAsyncEnumerable<Loop2Packet> StreamLoop2Async(int count,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            await _client.WakeAsync(_maxTries, ct);
            string cmd = $"{DavisProtocol.CmdLoop} {count}\n";
            await _client.SendDataAsync(Encoding.ASCII.GetBytes(cmd), ct);

            for (int i = 0; i < count && !ct.IsCancellationRequested; i++)
            {
                byte[] raw = await _client.GetDataWithCrc16Async(DavisProtocol.LoopPacketTotalBytes, ct);
                yield return Loop2Packet.Parse(raw[..DavisProtocol.LoopPacketDataBytes], RainBucketType);
            }
        }
        finally { _lock.Release(); }
    }

    /// <summary>Request exactly one LOOP2 packet.</summary>
    public async Task<Loop2Packet> GetCurrentConditionsAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            await _client.WakeAsync(_maxTries, ct);
            string cmd = $"{DavisProtocol.CmdLoop} 1\n";
            await _client.SendDataAsync(Encoding.ASCII.GetBytes(cmd), ct);
            byte[] raw = await _client.GetDataWithCrc16Async(DavisProtocol.LoopPacketTotalBytes, ct);
            return Loop2Packet.Parse(raw[..DavisProtocol.LoopPacketDataBytes], RainBucketType);
        }
        finally { _lock.Release(); }
    }

    // ── Archive — DMPAFT ─────────────────────────────────────────────────────

    /// <summary>
    /// Yield archive records since <paramref name="since"/> using DMPAFT.
    /// Pass <c>DateTime.MinValue</c> to get all records.
    /// </summary>
    /// <param name="since">Earliest record timestamp to return (local time).</param>
    /// <param name="maxRecords">
    /// Maximum number of records to yield before releasing the console lock.
    /// Callers can issue a second request starting from the last yielded record's
    /// timestamp to fetch the next batch. Defaults to <see cref="int.MaxValue"/>.
    /// </param>
    /// <param name="fallbackOnEmpty">
    /// When <c>true</c> and the console reports 0 pages for the given <paramref name="since"/>
    /// timestamp, automatically retry with a full-archive request (<c>DateTime.MinValue</c>).
    /// Some Davis firmware versions return 0 pages when <paramref name="since"/> predates the
    /// entire circular buffer (all records are newer than <paramref name="since"/>).
    /// Defaults to <c>false</c> to preserve existing catchup-worker behaviour.
    /// </param>
    public async IAsyncEnumerable<ArchiveRecord> GetArchiveSinceAsync(DateTime since,
        int maxRecords = int.MaxValue,
        bool fallbackOnEmpty = false,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        int yielded = 0;
        try
        {
            await _client.WakeAsync(_maxTries, ct);
            await _client.SendDataAsync(Encoding.ASCII.GetBytes($"{DavisProtocol.CmdDmpaft}\n"), ct);

            // Encode date/time stamp for DMPAFT
            byte[] dateStamp = EncodeDmpaftDate(since);
            await _client.SendDataWithCrc16Async(dateStamp, ct, maxTries: 1);

            // Read page/index response
            byte[] resp = await _client.GetDataWithCrc16Async(DavisProtocol.DmpaftResponseBytes, ct, maxTries: 1);
            int nPages = BinaryPrimitives.ReadUInt16LittleEndian(resp[0..]);
            int startIndex = BinaryPrimitives.ReadUInt16LittleEndian(resp[2..]);
            _logger.LogDebug("DMPAFT: {Pages} pages, start index {Idx}", nPages, startIndex);

            // Some Davis firmware returns 0 pages when 'since' predates the entire circular
            // buffer (all stored records are newer).  Re-issue with an all-records request so
            // the oldest available records are returned.
            if (nPages == 0 && since != DateTime.MinValue && fallbackOnEmpty)
            {
                _logger.LogDebug("DMPAFT: 0 pages for {Since}; retrying with full archive", since);
                await _client.WakeAsync(_maxTries, ct);
                await _client.SendDataAsync(Encoding.ASCII.GetBytes($"{DavisProtocol.CmdDmpaft}\n"), ct);
                await _client.SendDataWithCrc16Async(EncodeDmpaftDate(DateTime.MinValue), ct, maxTries: 1);
                resp = await _client.GetDataWithCrc16Async(DavisProtocol.DmpaftResponseBytes, ct, maxTries: 1);
                nPages = BinaryPrimitives.ReadUInt16LittleEndian(resp[0..]);
                startIndex = BinaryPrimitives.ReadUInt16LittleEndian(resp[2..]);
                _logger.LogDebug("DMPAFT full archive: {Pages} pages, start index {Idx}", nPages, startIndex);
            }

            DateTime lastGoodTs = since;

            for (int page = 0; page < nPages && !ct.IsCancellationRequested; page++)
            {
                byte[] pageData = await _client.GetDataWithCrc16Async(
                    DavisProtocol.ArchivePageBytes, ct,
                    prompt: [DavisProtocol.Ack], maxTries: 1);

                for (int idx = startIndex; idx < DavisProtocol.ArchiveRecordsPerPage; idx++)
                {
                    int start = 1 + DavisProtocol.ArchiveRecordBytes * idx;
                    var rec = ArchiveRecord.Parse(
                        pageData.AsSpan(start, DavisProtocol.ArchiveRecordBytes),
                        RainBucketType, ArchiveIntervalSeconds / 60);

                    if (rec is null)
                    {
                        _logger.LogDebug("DMPAFT: empty record at page {P} index {I}", page, idx);
                        yield break;
                    }

                    if (lastGoodTs != DateTime.MinValue && rec.DateTimeLocal <= lastGoodTs.AddSeconds(-7200))
                    {
                        _logger.LogDebug("DMPAFT: timestamp declining, done");
                        yield break;
                    }

                    lastGoodTs = rec.DateTimeLocal;
                    yield return rec;

                    if (++yielded >= maxRecords)
                    {
                        _logger.LogDebug("DMPAFT: maxRecords {Max} reached, stopping early", maxRecords);
                        yield break;
                    }
                }
                startIndex = 0; // Only the first page uses the returned start index
            }
        }
        finally { _lock.Release(); }
    }

    // ── Station interrogation — READ ──────────────────────────────────────────

    /// <summary>Read hardware type, model, firmware version and date, and current console time.</summary>
    public async Task<StationInfo> GetStationInfoAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            await _client.WakeAsync(_maxTries, ct);

            // Hardware type: WRD command — response is ACK then 1 hardware-type byte
            await _client.SendDataAsync(Encoding.ASCII.GetBytes($"{DavisProtocol.CmdWrd}{(char)0x12}{(char)0x4D}\n"), ct);
            byte[] hwByte = await _client.ReadExactAsync(1, ct);
            int hwType = hwByte[0];
            int model = hwType == DavisProtocol.HardwareVantageVue ? 2 : ModelType;

            // Firmware version
            string[] nverLines = await _client.SendCommandAsync($"{DavisProtocol.CmdNver}\n", ct, _maxTries);
            string fwVer = nverLines.Length > 0 ? nverLines[0].Trim() : "?";

            // Firmware date
            string[] verLines = await _client.SendCommandAsync($"{DavisProtocol.CmdVer}\n", ct, _maxTries);
            string fwDate = verLines.Length > 0 ? verLines[0].Trim() : "?";

            // Console time
            DateTime consoleTime = await GetConsoleTimeInternalAsync(ct);

            return new StationInfo
            {
                HardwareName = hwType == DavisProtocol.HardwareVantageVue ? "Vantage Vue"
                                : model == 1 ? "Vantage Pro" : "Vantage Pro 2",
                HardwareType = hwType,
                ModelType = model,
                FirmwareVersion = fwVer,
                FirmwareDate = fwDate,
                ConsoleTime = consoleTime,
            };
        }
        finally { _lock.Release(); }
    }

    /// <summary>Read all user-configurable settings from EEPROM.</summary>
    public async Task<StationSettings> GetStationSettingsAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            await _client.WakeAsync(_maxTries, ct);

            byte unitBits = (await ReadEepromAsync(DavisProtocol.EepromUnitBits, 1, ct))[0];
            byte setupBits = (await ReadEepromAsync(DavisProtocol.EepromSetupBits, 1, ct))[0];
            byte ryStart = (await ReadEepromAsync(DavisProtocol.EepromRainYearStart, 1, ct))[0];
            byte archMin = (await ReadEepromAsync(DavisProtocol.EepromArchiveInterval, 1, ct))[0];
            byte gmtOrZone = (await ReadEepromAsync(DavisProtocol.EepromGmtOrZone, 1, ct))[0];
            byte manOrAuto = (await ReadEepromAsync(DavisProtocol.EepromManOrAuto, 1, ct))[0];
            byte dst = (await ReadEepromAsync(DavisProtocol.EepromDaylightSavings, 1, ct))[0];
            byte tzCode = (await ReadEepromAsync(DavisProtocol.EepromTimezoneCode, 1, ct))[0];
            byte tempLog = (await ReadEepromAsync(DavisProtocol.EepromTempLogging, 1, ct))[0];

            byte[] latBytes = await ReadEepromAsync(DavisProtocol.EepromLatitude, 2, ct);
            byte[] lonBytes = await ReadEepromAsync(DavisProtocol.EepromLongitude, 2, ct);
            byte[] altBytes = await ReadEepromAsync(DavisProtocol.EepromAltitude, 2, ct);
            byte[] gmtOffB = await ReadEepromAsync(DavisProtocol.EepromGmtOffset, 2, ct);

            short lat = BinaryPrimitives.ReadInt16LittleEndian(latBytes);
            short lon = BinaryPrimitives.ReadInt16LittleEndian(lonBytes);
            short alt = BinaryPrimitives.ReadInt16LittleEndian(altBytes);
            short gmt = BinaryPrimitives.ReadInt16LittleEndian(gmtOffB);

            int bucketType = (setupBits & 0x30) >> 4;
            string dstMode = manOrAuto == 0 ? "AUTO" : (dst != 0 ? "ON" : "OFF");

            return new StationSettings
            {
                ArchiveIntervalSeconds = archMin * 60,
                LatitudeDegrees = lat / 10.0,
                LongitudeDegrees = lon / 10.0,
                AltitudeFeet = (double)alt,
                RainYearStartMonth = ryStart,
                RainBucketType = bucketType,
                DstSetting = dstMode,
                UseTimezoneCode = gmtOrZone == 0,
                TimezoneCode = tzCode,
                GmtOffsetHours = gmt / 100.0,
                TemperatureLogging = tempLog != 0 ? "LAST" : "AVERAGE",
                BarometerUnits = BaroUnitName(unitBits & 0x03),
                TemperatureUnits = TempUnitName((unitBits & 0x0C) >> 2),
                RainUnits = (unitBits & 0x20) != 0 ? "mm" : "inch",
                WindUnits = WindUnitName((unitBits & 0xC0) >> 6),
            };
        }
        finally { _lock.Release(); }
    }

    /// <summary>Read all 8-channel transmitter configurations from EEPROM.</summary>
    public async Task<IReadOnlyList<TransmitterConfig>> GetTransmittersAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            await _client.WakeAsync(_maxTries, ct);
            byte useTx = (await ReadEepromAsync(DavisProtocol.EepromUseTx, 1, ct))[0];
            byte retransmit = (await ReadEepromAsync(DavisProtocol.EepromRetransmit, 1, ct))[0];
            byte[] txData = await ReadEepromAsync(DavisProtocol.EepromTransmitters, 16, ct);

            var configs = new List<TransmitterConfig>(8);
            for (int ch = 1; ch <= 8; ch++)
            {
                byte lower = txData[(ch - 1) * 2];
                byte upper = txData[(ch - 1) * 2 + 1];
                int txType = lower & 0x0F;
                int repeaterNo = lower >> 4;
                string? repeaterId = repeaterNo != 0 ? ((char)(repeaterNo - 8 + 'A')).ToString() : null;
                bool active = (useTx & 1) != 0;
                bool reTx = (retransmit & 1) != 0;
                useTx >>= 1;
                retransmit >>= 1;

                int? extraTemp = txType is 1 or 3 ? (upper & 0x0F) + 1 : null;
                int? extraHumid = txType is 2 or 3 ? (upper >> 4) + 1 : null;

                configs.Add(new TransmitterConfig
                {
                    Channel = ch,
                    TransmitterType = TxTypeName(txType),
                    RepeaterId = repeaterId,
                    IsActive = active,
                    IsRetransmitting = reTx,
                    ExtraTemperatureId = extraTemp,
                    ExtraHumidityId = extraHumid,
                });
            }
            return configs.AsReadOnly();
        }
        finally { _lock.Release(); }
    }

    /// <summary>Read on-board temperature/humidity/wind calibration offsets from EEPROM.</summary>
    public async Task<CalibrationData> GetCalibrationAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            await _client.WakeAsync(_maxTries, ct);
            // 27 signed bytes: inTemp, inTempComp, outTemp, extra×7, soil×4, leaf×4
            byte[] temps = await ReadEepromAsync(DavisProtocol.EepromTempCalib, 27, ct);
            byte[] inHumB = await ReadEepromAsync(DavisProtocol.EepromInHumidCalib, 1, ct);
            byte[] outHumB = await ReadEepromAsync(DavisProtocol.EepromOutHumidCalib, 1, ct);
            byte[] windB = await ReadEepromAsync(DavisProtocol.EepromWindDirCalib, 2, ct);
            // Extra humidity calibrations (7 channels after outHumid)
            byte[] exHumB = await ReadEepromAsync((ushort)(DavisProtocol.EepromOutHumidCalib + 1), 7, ct);

            return new CalibrationData
            {
                InsideTempOffsetF = (sbyte)temps[0] / 10.0,
                OutsideTempOffsetF = (sbyte)temps[2] / 10.0,
                InsideHumidOffsetPct = (sbyte)inHumB[0],
                OutsideHumidOffsetPct = (sbyte)outHumB[0],
                WindDirOffsetDegrees = BinaryPrimitives.ReadInt16LittleEndian(windB),
                ExtraTempOffsets = [.. Enumerable.Range(0, 7).Select(i => (sbyte)temps[3 + i] / 10.0)],
                SoilTempOffsets = [.. Enumerable.Range(0, 4).Select(i => (sbyte)temps[10 + i] / 10.0)],
                LeafTempOffsets = [.. Enumerable.Range(0, 4).Select(i => (sbyte)temps[14 + i] / 10.0)],
                ExtraHumidOffsets = [.. Enumerable.Range(0, 7).Select(i => (sbyte)exHumB[i] * 1.0)],
            };
        }
        finally { _lock.Release(); }
    }

    /// <summary>Read barometer calibration data using the BARDATA command.</summary>
    public async Task<BarometerData> GetBarometerDataAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            await _client.WakeAsync(_maxTries, ct);
            string[] lines = await _client.SendCommandAsync($"{DavisProtocol.CmdBardata}\n", ct, _maxTries);
            // Lines: "BAR  29.990", "Elevation  4500", "DEW POINT  55", "VIRTUAL TEMP  62",
            //        "C  2.3", "R  1.003", "BARCAL  0.012", "GAIN  1", "OFFSET  0"
            return new BarometerData
            {
                CurrentPressureInHg = ParseDouble(lines, 0, 1),
                AltitudeFeet = ParseDouble(lines, 1, 1),
                DewPointF = ParseDouble(lines, 2, 2),
                VirtualTemperatureF = ParseDouble(lines, 3, 2),
                CorrectionFactor = ParseDouble(lines, 4, 1),
                CorrectionRatio = ParseDouble(lines, 5, 1),
                CorrectionConstantInHg = ParseDouble(lines, 6, 1),
                Gain = ParseDouble(lines, 7, 1),
                ErrorOffset = ParseDouble(lines, 8, 1),
            };
        }
        finally { _lock.Release(); }
    }

    /// <summary>Read ISS reception statistics using the RXCHECK command.</summary>
    public async Task<ReceptionStats> GetReceptionStatsAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            await _client.WakeAsync(_maxTries, ct);
            string[] lines = await _client.SendCommandAsync($"{DavisProtocol.CmdRxcheck}\n", ct, _maxTries);
            // Single space-separated line: packets missed resync longest crc
            string[] parts = (lines.Length > 0 ? lines[0] : "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return new ReceptionStats
            {
                TotalPacketsReceived = parts.Length > 0 ? int.Parse(parts[0]) : 0,
                TotalPacketsMissed = parts.Length > 1 ? int.Parse(parts[1]) : 0,
                NumberOfResynchronizations = parts.Length > 2 ? int.Parse(parts[2]) : 0,
                LongestGoodStretch = parts.Length > 3 ? int.Parse(parts[3]) : 0,
                NumberOfCrcErrors = parts.Length > 4 ? int.Parse(parts[4]) : 0,
            };
        }
        finally { _lock.Release(); }
    }

    /// <summary>Read the current console clock time.</summary>
    public async Task<DateTime> GetConsoleTimeAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            await _client.WakeAsync(_maxTries, ct);
            return await GetConsoleTimeInternalAsync(ct);
        }
        finally { _lock.Release(); }
    }

    // ── Station configuration — WRITE ─────────────────────────────────────────

    /// <summary>Set the console clock to the current system time.</summary>
    public async Task SetConsoleTimeAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            await _client.WakeAsync(_maxTries, ct);
            await _client.SendDataAsync(Encoding.ASCII.GetBytes($"{DavisProtocol.CmdSettime}\n"), ct);
            // Round up by 0.75s to account for protocol overhead
            DateTime t = DateTime.Now.AddSeconds(0.75);
            byte[] payload =
            [
                (byte)t.Second, (byte)t.Minute, (byte)t.Hour,
                (byte)t.Day, (byte)t.Month, (byte)(t.Year - 1900)
            ];
            await _client.SendDataWithCrc16Async(payload, ct, maxTries: 1);
            _logger.LogInformation("Console clock set to {T}", t);
        }
        finally { _lock.Release(); }
    }

    /// <summary>Set barometer calibration to a known-correct pressure and altitude.</summary>
    public async Task SetBarometerAsync(double pressureInHg, double altitudeFt, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            await _client.WakeAsync(_maxTries, ct);
            int bar = (int)(pressureInHg * 1000);
            int alt = (int)altitudeFt;
            await _client.SendCommandAsync($"{DavisProtocol.CmdBar}{bar} {alt}\n", ct, _maxTries);
            await ReadSetupFromEepromAsync(ct);
        }
        finally { _lock.Release(); }
    }

    /// <summary>Set the archive interval (1, 5, 10, 15, 30, 60, or 120 minutes).</summary>
    public async Task SetArchiveIntervalAsync(int minutes, CancellationToken ct = default)
    {
        int[] valid = [1, 5, 10, 15, 30, 60, 120];
        if (!valid.Contains(minutes))
            throw new ArgumentException($"Invalid archive interval {minutes}. Must be one of: {string.Join(", ", valid)}");

        await _lock.WaitAsync(ct);
        try
        {
            await _client.WakeAsync(_maxTries, ct);
            await _client.SendCommandAsync($"{DavisProtocol.CmdSetper} {minutes}\n", ct, _maxTries);
            ArchiveIntervalSeconds = minutes * 60;
        }
        finally { _lock.Release(); }
    }

    /// <summary>Set the station latitude (decimal degrees, +N/−S).</summary>
    public async Task SetLatitudeAsync(double latitude, CancellationToken ct = default)
    {
        short val = (short)(latitude * 10);
        await WriteEepromShortAsync(DavisProtocol.EepromLatitude, val, ct);
    }

    /// <summary>Set the station longitude (decimal degrees, +E/−W).</summary>
    public async Task SetLongitudeAsync(double longitude, CancellationToken ct = default)
    {
        short val = (short)(longitude * 10);
        await WriteEepromShortAsync(DavisProtocol.EepromLongitude, val, ct);
    }

    /// <summary>Set the station altitude (feet).</summary>
    public async Task SetAltitudeAsync(double feet, CancellationToken ct = default)
    {
        short val = (short)feet;
        await WriteEepromShortAsync(DavisProtocol.EepromAltitude, val, ct);
    }

    /// <summary>Set the rain bucket type (0=0.01in, 1=0.2mm, 2=0.1mm).</summary>
    public async Task SetRainBucketTypeAsync(int bucketCode, CancellationToken ct = default)
    {
        if (bucketCode is < 0 or > 2) throw new ArgumentException("Bucket code must be 0, 1, or 2");
        await _lock.WaitAsync(ct);
        try
        {
            await _client.WakeAsync(_maxTries, ct);
            byte[] bits = await ReadEepromAsync(DavisProtocol.EepromSetupBits, 1, ct);
            bits[0] = (byte)((bits[0] & 0xCF) | (bucketCode << 4));
            await WriteEepromAsync(DavisProtocol.EepromSetupBits, bits, ct);
            await RunNewSetupAsync(ct);
            RainBucketType = bucketCode;
        }
        finally { _lock.Release(); }
    }

    /// <summary>Set the rain year start month (1=January … 12=December).</summary>
    public async Task SetRainYearStartAsync(int month, CancellationToken ct = default)
    {
        if (month is < 1 or > 12) throw new ArgumentException("Month must be 1–12");
        await WriteEepromByteAsync(DavisProtocol.EepromRainYearStart, (byte)month, ct);
    }

    /// <summary>Set DST mode.</summary>
    public async Task SetDstAsync(DstMode mode, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            await _client.WakeAsync(_maxTries, ct);
            byte manAuto = mode == DstMode.Auto ? (byte)0 : (byte)1;
            await WriteEepromAsync(DavisProtocol.EepromManOrAuto, [manAuto], ct);
            if (mode != DstMode.Auto)
            {
                byte dstBit = mode == DstMode.On ? (byte)1 : (byte)0;
                await WriteEepromAsync(DavisProtocol.EepromDaylightSavings, [dstBit], ct);
            }
        }
        finally { _lock.Release(); }
    }

    /// <summary>Set the timezone to a Davis standard timezone code.</summary>
    public async Task SetTimezoneCodeAsync(int code, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            await _client.WakeAsync(_maxTries, ct);
            await WriteEepromAsync(DavisProtocol.EepromGmtOrZone, [0], ct);   // Use TIME_ZONE
            await WriteEepromAsync(DavisProtocol.EepromTimezoneCode, [(byte)code], ct);
        }
        finally { _lock.Release(); }
    }

    /// <summary>Set a custom GMT offset in hundredths of hours (e.g. -700 = UTC−7:00).</summary>
    public async Task SetTimezoneOffsetAsync(int hundredths, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            await _client.WakeAsync(_maxTries, ct);
            await WriteEepromAsync(DavisProtocol.EepromGmtOrZone, [1], ct);   // Use GMT_OFFSET
            byte[] buf = new byte[2];
            BinaryPrimitives.WriteInt16LittleEndian(buf, (short)hundredths);
            await WriteEepromAsync(DavisProtocol.EepromGmtOffset, buf, ct);
        }
        finally { _lock.Release(); }
    }

    /// <summary>Set temperature logging mode (LAST or AVERAGE).</summary>
    public async Task SetTemperatureLoggingAsync(TempLogging mode, CancellationToken ct = default)
    {
        byte val = mode == TempLogging.Last ? (byte)1 : (byte)0;
        await WriteEepromByteAsync(DavisProtocol.EepromTempLogging, val, ct);
    }

    /// <summary>Set wind direction calibration offset (−359 to +359 degrees).</summary>
    public async Task SetCalibrationWindDirAsync(int offsetDegrees, CancellationToken ct = default)
    {
        if (offsetDegrees is < -359 or > 359) throw new ArgumentException("Wind dir offset must be –359 to +359");
        await _lock.WaitAsync(ct);
        try
        {
            await _client.WakeAsync(_maxTries, ct);
            byte[] buf = new byte[2];
            BinaryPrimitives.WriteInt16LittleEndian(buf, (short)offsetDegrees);
            await WriteEepromAsync(DavisProtocol.EepromWindDirCalib, buf, ct);
        }
        finally { _lock.Release(); }
    }

    /// <summary>Set a temperature calibration offset for a named sensor variable.</summary>
    /// <param name="variable">One of: inTemp, outTemp, extraTemp1–7, soilTemp1–4, leafTemp1–4</param>
    /// <param name="offsetF">Offset in °F, range −12.8 to +12.7</param>
    public async Task SetCalibrationTempAsync(string variable, double offsetF, CancellationToken ct = default)
    {
        if (offsetF is < -12.8 or > 12.7) throw new ArgumentException("Temp offset must be –12.8 to +12.7 °F");
        sbyte raw = (sbyte)Math.Round(offsetF * 10);

        await _lock.WaitAsync(ct);
        try
        {
            await _client.WakeAsync(_maxTries, ct);
            ushort addr = variable switch
            {
                "inTemp" => 0x32,
                "outTemp" => 0x34,
                string v when v.StartsWith("extraTemp") && int.TryParse(v[9..], out int ei) && ei >= 1 && ei <= 7 => (ushort)(0x34 + ei),
                string v when v.StartsWith("soilTemp") && int.TryParse(v[8..], out int si) && si >= 1 && si <= 4 => (ushort)(0x3B + si),
                string v when v.StartsWith("leafTemp") && int.TryParse(v[8..], out int li) && li >= 1 && li <= 4 => (ushort)(0x3F + li),
                _ => throw new ArgumentException($"Unknown temperature variable: {variable}")
            };

            if (variable == "inTemp")
            {
                // Inside temp requires 1's complement in adjacent byte
                byte comp = (byte)(~raw & 0xFF);
                await WriteEepromAsync(addr, [(byte)raw, comp], ct);
            }
            else
            {
                await WriteEepromAsync(addr, [(byte)raw], ct);
            }
        }
        finally { _lock.Release(); }
    }

    /// <summary>Set a humidity calibration offset for a named sensor variable.</summary>
    /// <param name="variable">One of: inHumid, outHumid, extraHumid1–7</param>
    /// <param name="offsetPct">Offset in % RH, range −100 to +100</param>
    public async Task SetCalibrationHumidityAsync(string variable, int offsetPct, CancellationToken ct = default)
    {
        if (offsetPct is < -100 or > 100) throw new ArgumentException("Humidity offset must be –100 to +100 %");
        await _lock.WaitAsync(ct);
        try
        {
            await _client.WakeAsync(_maxTries, ct);
            ushort addr = variable switch
            {
                "inHumid" => DavisProtocol.EepromInHumidCalib,
                "outHumid" => DavisProtocol.EepromOutHumidCalib,
                string v when v.StartsWith("extraHumid") && int.TryParse(v[10..], out int hi) && hi >= 1 && hi <= 7 =>
                    (ushort)(DavisProtocol.EepromOutHumidCalib + hi),
                _ => throw new ArgumentException($"Unknown humidity variable: {variable}")
            };
            await WriteEepromAsync(addr, [(byte)(sbyte)offsetPct], ct);
        }
        finally { _lock.Release(); }
    }

    /// <summary>Set the transmitter type and configuration for a channel (1–8).</summary>
    public async Task SetTransmitterAsync(int channel, TransmitterType type,
        int? extraTempId, int? extraHumId, string? repeaterId, CancellationToken ct = default)
    {
        if (channel is < 1 or > 8) throw new ArgumentException("Channel must be 1–8");

        await _lock.WaitAsync(ct);
        try
        {
            await _client.WakeAsync(_maxTries, ct);
            byte[] useTxByte = await ReadEepromAsync(DavisProtocol.EepromUseTx, 1, ct);
            byte useTx = useTxByte[0];

            int repeaterCode = 0;
            if (repeaterId is not null)
                repeaterCode = (char.ToUpper(repeaterId[0]) - 'A') + 8;

            int typeBits = (int)type & 0x0F;
            if (repeaterCode != 0) typeBits |= repeaterCode << 4;

            byte extraIdBits = 0xFF;
            if (extraTempId.HasValue) extraIdBits = (byte)((extraIdBits & 0xF0) | ((extraTempId.Value - 1) & 0x0F));
            if (extraHumId.HasValue) extraIdBits = (byte)((extraIdBits & 0x0F) | ((extraHumId.Value - 1) << 4));

            ushort startByte = (ushort)(DavisProtocol.EepromTransmitters + (channel - 1) * 2);
            await WriteEepromAsync(startByte, [(byte)typeBits, extraIdBits], ct);

            if (type == TransmitterType.None)
                useTx &= (byte)~(1 << (channel - 1));
            else
                useTx |= (byte)(1 << (channel - 1));

            await WriteEepromAsync(DavisProtocol.EepromUseTx, [useTx], ct);
            await RunNewSetupAsync(ct);
        }
        finally { _lock.Release(); }
    }

    /// <summary>Set which channel to retransmit (0 = off).</summary>
    public async Task SetRetransmitAsync(int channel, CancellationToken ct = default)
    {
        if (channel is < 0 or > 8) throw new ArgumentException("Channel must be 0–8 (0=off)");
        await WriteEepromByteAsync(DavisProtocol.EepromRetransmit,
            channel == 0 ? (byte)0 : (byte)(1 << (channel - 1)), ct);
        await RunNewSetupAsync(ct);
    }

    /// <summary>Turn the console lamp on or off.</summary>
    public async Task SetLampAsync(bool on, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            await _client.WakeAsync(_maxTries, ct);
            await _client.SendCommandAsync($"{DavisProtocol.CmdLamps} {(on ? '1' : '0')}\n", ct, _maxTries);
        }
        finally { _lock.Release(); }
    }

    /// <summary>Clear the console's archive memory (irreversible).</summary>
    public async Task ClearArchiveAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            await _client.WakeAsync(_maxTries, ct);
            await _client.SendDataAsync(Encoding.ASCII.GetBytes($"{DavisProtocol.CmdClrlog}\n"), ct);
            _logger.LogWarning("Archive memory cleared on Davis console");
        }
        finally { _lock.Release(); }
    }

    // ── Internal helpers ──────────────────────────────────────────────────────

    private async Task ReadSetupFromEepromAsync(CancellationToken ct)
    {
        byte[] setupBits = await ReadEepromAsync(DavisProtocol.EepromSetupBits, 1, ct);
        byte[] archByte  = await ReadEepromAsync(DavisProtocol.EepromArchiveInterval, 1, ct);
        byte[] gmtOrZone = await ReadEepromAsync(DavisProtocol.EepromGmtOrZone, 1, ct);
        byte[] tzCode    = await ReadEepromAsync(DavisProtocol.EepromTimezoneCode, 1, ct);
        byte[] gmtOffB   = await ReadEepromAsync(DavisProtocol.EepromGmtOffset, 2, ct);

        RainBucketType        = (setupBits[0] & 0x30) >> 4;
        ArchiveIntervalSeconds = archByte[0] * 60;
        UseTimezoneCode       = gmtOrZone[0] == 0;
        TimezoneCode          = tzCode[0];
        GmtOffsetHours        = BinaryPrimitives.ReadInt16LittleEndian(gmtOffB) / 100.0;
    }

    private async Task<DateTime> GetConsoleTimeInternalAsync(CancellationToken ct)
    {
        await _client.SendDataAsync(Encoding.ASCII.GetBytes($"{DavisProtocol.CmdGettime}\n"), ct);
        byte[] buf = await _client.GetDataWithCrc16Async(8, ct, maxTries: 1);
        // Layout: sec, min, hr, day, mon, yr (since 1900), [2 CRC]
        int sec = buf[0], min = buf[1], hr = buf[2], day = buf[3], mon = buf[4], yr = buf[5] + 1900;
        return new DateTime(yr, mon, day, hr, min, sec, DateTimeKind.Local);
    }

    private async Task<byte[]> ReadEepromAsync(ushort address, int bytes, CancellationToken ct)
    {
        string cmd = $"{DavisProtocol.CmdEebrd} {address:X} {bytes:X}\n";
        await _client.SendDataAsync(Encoding.ASCII.GetBytes(cmd), ct);
        byte[] data = await _client.GetDataWithCrc16Async(bytes + 2, ct, maxTries: 1);
        return data[..bytes];
    }

    private async Task WriteEepromAsync(ushort address, byte[] data, CancellationToken ct)
    {
        string cmd = $"{DavisProtocol.CmdEebwr} {address:X} {data.Length:X}\n";
        await _client.SendDataAsync(Encoding.ASCII.GetBytes(cmd), ct);
        await _client.SendDataWithCrc16Async(data, ct, maxTries: 1);
    }

    private async Task WriteEepromByteAsync(ushort address, byte value, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            await _client.WakeAsync(_maxTries, ct);
            await WriteEepromAsync(address, [value], ct);
        }
        finally { _lock.Release(); }
    }

    private async Task WriteEepromShortAsync(ushort address, short value, CancellationToken ct)
    {
        byte[] buf = new byte[2];
        BinaryPrimitives.WriteInt16LittleEndian(buf, value);
        await _lock.WaitAsync(ct);
        try
        {
            await _client.WakeAsync(_maxTries, ct);
            await WriteEepromAsync(address, buf, ct);
        }
        finally { _lock.Release(); }
    }

    private async Task RunNewSetupAsync(CancellationToken ct) =>
        await _client.SendDataAsync(Encoding.ASCII.GetBytes($"{DavisProtocol.CmdNewsetup}\n"), ct);

    private static byte[] EncodeDmpaftDate(DateTime since)
    {
        if (since == DateTime.MinValue) return [0, 0, 0, 0];
        int dateStamp = since.Day | (since.Month << 5) | ((since.Year - 2000) << 9);
        int timeStamp = since.Hour * 100 + since.Minute;
        byte[] buf = new byte[4];
        BinaryPrimitives.WriteUInt16LittleEndian(buf[0..], (ushort)dateStamp);
        BinaryPrimitives.WriteUInt16LittleEndian(buf[2..], (ushort)timeStamp);
        return buf;
    }

    private static double ParseDouble(string[] lines, int lineIdx, int wordIdx)
    {
        if (lineIdx >= lines.Length) return 0;
        string[] parts = lines[lineIdx].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return wordIdx < parts.Length && double.TryParse(parts[wordIdx], out double v) ? v : 0;
    }

    private static string BaroUnitName(int code) => code switch { 1 => "mmHg", 2 => "hPa", 3 => "mbar", _ => "inHg" };
    private static string TempUnitName(int code) => code switch { 1 => "°F×10", 2 => "°C", 3 => "°C×10", _ => "°F" };
    private static string WindUnitName(int code) => code switch { 1 => "m/s", 2 => "km/h", 3 => "knots", _ => "mph" };
    private static string TxTypeName(int code) => code switch
    {
        0 => "iss",
        1 => "temp",
        2 => "hum",
        3 => "temp_hum",
        4 => "wind",
        5 => "rain",
        6 => "leaf",
        7 => "soil",
        8 => "leaf_soil",
        9 => "sensorlink",
        _ => "none"
    };

    public async ValueTask DisposeAsync()
    {
        try { await DisconnectAsync(); } catch { /* best-effort */ }
        _client.Dispose();
        _lock.Dispose();
    }
}
