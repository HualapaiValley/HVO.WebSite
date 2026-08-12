using System.Buffers.Binary;
using System.Globalization;
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
public sealed class VantageStation : IDavisStation, IAsyncDisposable
{
    private enum ConsoleSessionMode
    {
        Unknown,
        Command,
        Loop,
    }

    private readonly DavisConsoleClient _client;
    private readonly ILogger<VantageStation> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly object _loopStateLock = new();
    private readonly int _maxTries;
    private bool _setupHydrated;
    private CancellationTokenSource? _activeLoopInterruption;
    private ConsoleSessionMode _consoleMode = ConsoleSessionMode.Unknown;

    // Cached EEPROM values (populated on Connect)
    public int RainBucketType { get; private set; } = DavisProtocol.BucketType001Inch;
    public int ArchiveIntervalSeconds { get; private set; } = 300;
    public int ModelType { get; private set; } = 2;
    public int HardwareType { get; private set; }
    public double? AltitudeFeet { get; private set; }
    public bool UseTimezoneCode { get; private set; } = true;
    public int TimezoneCode { get; private set; }
    public double GmtOffsetHours { get; private set; }
    public double? LatitudeDegrees { get; private set; }
    public double? LongitudeDegrees { get; private set; }
    public string BarometerUnits { get; private set; } = "inHg";
    public string TemperatureUnits { get; private set; } = "°F";
    public string RainUnits { get; private set; } = "inch";
    public string WindUnits { get; private set; } = "mph";
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
            ResetConsoleState();
            await _client.OpenAsync(ct);
            await _client.WakeAsync(_maxTries, ct);
            SetConsoleMode(ConsoleSessionMode.Command);
            if (!_setupHydrated)
            {
                await ReadSetupFromEepromAsync(ct);
            }
            _logger.LogInformation("Station connected. RainBucket={B}, ArchiveInterval={I}s, Model={M}",
                RainBucketType, ArchiveIntervalSeconds, ModelType);
        }
        finally { _lock.Release(); }
    }

    public void ApplyStationSettings(StationSettings settings)
    {
        ArchiveIntervalSeconds = settings.ArchiveIntervalSeconds;
        RainBucketType = settings.RainBucketType;
        UseTimezoneCode = settings.UseTimezoneCode;
        TimezoneCode = settings.TimezoneCode;
        GmtOffsetHours = settings.GmtOffsetHours;
        LatitudeDegrees = settings.LatitudeDegrees;
        LongitudeDegrees = settings.LongitudeDegrees;
        AltitudeFeet = settings.AltitudeFeet;
        BarometerUnits = settings.BarometerUnits;
        TemperatureUnits = settings.TemperatureUnits;
        RainUnits = settings.RainUnits;
        WindUnits = settings.WindUnits;
        _setupHydrated = true;
    }

    /// <summary>Close the connection gracefully.</summary>
    public async Task DisconnectAsync()
    {
        await _lock.WaitAsync();
        try
        {
            ResetConsoleState();
            _client.Close();
        }
        finally { _lock.Release(); }
    }

    // ── Live data — LOOP2 streaming ───────────────────────────────────────────

    /// <summary>
    /// Request a single LOOP1 packet using <c>LPS 1 1</c>.
    /// Returns console-status fields (battery, forecast, sunrise/sunset, monthly/yearly
    /// rain/ET totals) that are only present in LOOP1 packets.
    /// Called before each LOOP2 streaming batch to refresh the worker's LOOP1 cache.
    /// </summary>
    public async Task<Loop2Packet> GetLoop1Async(CancellationToken ct = default)
    {
        await EnterCommandScopeAsync(ct, interruptLoop: false);
        bool completed = false;
        try
        {
            await _client.SendDataAsync(Encoding.ASCII.GetBytes($"{DavisProtocol.CmdLps} 1 1\n"), ct);
            byte[] raw = await _client.GetDataWithCrc16Async(DavisProtocol.LoopPacketTotalBytes, ct);
            completed = true;
            return Loop2Packet.Parse(raw[..DavisProtocol.LoopPacketDataBytes], RainBucketType);
        }
        finally
        {
            SetConsoleMode(completed ? ConsoleSessionMode.Command : ConsoleSessionMode.Unknown);
            _lock.Release();
        }
    }

    /// <summary>
    /// Request a batch of LOOP2 packets from the console using the LPS 2 command.
    /// Each packet is yielded as it arrives. The caller must release the lock by
    /// cancelling the token or consuming all packets.
    /// </summary>
    public async IAsyncEnumerable<Loop2Packet> StreamLoop2Async(int count,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        await EnterCommandScopeAsync(ct, interruptLoop: false);
        CancellationTokenSource? loopInterruption = null;
        bool completed = false;
        try
        {
            CancellationToken loopCt = BeginLoopSession(ct, out loopInterruption);
            string cmd = $"{DavisProtocol.CmdLps} 2 {count}\n";
            await _client.SendDataAsync(Encoding.ASCII.GetBytes(cmd), loopCt);

            for (int i = 0; i < count && !loopCt.IsCancellationRequested; i++)
            {
                byte[] raw;
                try
                {
                    raw = await _client.GetDataWithCrc16Async(DavisProtocol.LoopPacketTotalBytes, loopCt);
                }
                catch (OperationCanceledException) when (loopInterruption is not null
                    && loopInterruption.IsCancellationRequested
                    && !ct.IsCancellationRequested)
                {
                    yield break;
                }

                yield return Loop2Packet.Parse(raw[..DavisProtocol.LoopPacketDataBytes], RainBucketType);
            }

            completed = !loopCt.IsCancellationRequested;
        }
        finally
        {
            EndLoopSession(loopInterruption, completed);
            _lock.Release();
        }
    }

    /// <summary>
    /// Request one merged LOOP1+LOOP2 reading using <c>LPS 3 2</c>.
    /// The LOOP1 packet provides battery, forecast, sunrise/sunset, and extended rain/ET.
    /// The LOOP2 packet provides dew point, heat index, wind chill, altimeter, and precision wind.
    /// </summary>
    public async Task<Loop2Packet> GetCurrentConditionsAsync(CancellationToken ct = default)
    {
        await EnterCommandScopeAsync(ct);
        try
        {
            // LPS 3 N → N alternating packets: LOOP1, LOOP2, LOOP1, LOOP2, …
            await _client.SendDataAsync(Encoding.ASCII.GetBytes($"{DavisProtocol.CmdLps} 3 2\n"), ct);
            byte[] raw1 = await _client.GetDataWithCrc16Async(DavisProtocol.LoopPacketTotalBytes, ct);
            byte[] raw2 = await _client.GetDataWithCrc16Async(DavisProtocol.LoopPacketTotalBytes, ct);
            var loop1 = Loop2Packet.Parse(raw1[..DavisProtocol.LoopPacketDataBytes], RainBucketType);
            var loop2 = Loop2Packet.Parse(raw2[..DavisProtocol.LoopPacketDataBytes], RainBucketType);
            return MergePackets(loop1, loop2);
        }
        finally { _lock.Release(); }
    }

    /// <summary>
    /// Merge a LOOP1 and LOOP2 packet into a single packet.
    /// LOOP2 fields take precedence for weather data (higher precision for wind, plus derived values).
    /// LOOP1-only fields (battery, forecast, extended rain/ET, extra sensors) come from LOOP1.
    /// </summary>
    internal static Loop2Packet MergePackets(Loop2Packet loop1, Loop2Packet loop2) => new()
    {
        RecordedAtUtc = loop2.RecordedAtUtc,

        // Atmosphere — prefer LOOP2 (altimeter + raw pressure only in LOOP2)
        BarometricPressureInHg = loop2.BarometricPressureInHg ?? loop1.BarometricPressureInHg,
        PressureRawInHg = loop2.PressureRawInHg,
        AltimeterInHg = loop2.AltimeterInHg,
        BarometricTrend = loop2.BarometricTrend ?? loop1.BarometricTrend,

        // Temperature — LOOP2 adds dew point, heat index, wind chill, THSW
        InsideTemperatureF = loop2.InsideTemperatureF ?? loop1.InsideTemperatureF,
        OutsideTemperatureF = loop2.OutsideTemperatureF ?? loop1.OutsideTemperatureF,
        DewPointF = loop2.DewPointF,
        HeatIndexF = loop2.HeatIndexF,
        WindChillF = loop2.WindChillF,
        ThswF = loop2.ThswF,

        // Humidity
        InsideHumidityPercent = loop2.InsideHumidityPercent ?? loop1.InsideHumidityPercent,
        OutsideHumidityPercent = loop2.OutsideHumidityPercent ?? loop1.OutsideHumidityPercent,

        // Wind — LOOP2 has ×10 precision for avg, plus 2-min avg and gust direction
        WindSpeedMph = loop2.WindSpeedMph ?? loop1.WindSpeedMph,
        WindDirectionDegrees = loop2.WindDirectionDegrees ?? loop1.WindDirectionDegrees,
        WindSpeed10MinAvgMph = loop2.WindSpeed10MinAvgMph ?? loop1.WindSpeed10MinAvgMph,
        WindSpeed2MinAvgMph = loop2.WindSpeed2MinAvgMph,
        WindGust10MinMph = loop2.WindGust10MinMph,
        WindGust10MinDirectionDegrees = loop2.WindGust10MinDirectionDegrees,

        // Rain — LOOP2 adds 15-min, hourly, 24-hr buckets
        RainRateInchesPerHour = loop2.RainRateInchesPerHour ?? loop1.RainRateInchesPerHour,
        DailyRainInches = loop2.DailyRainInches ?? loop1.DailyRainInches,
        Rain15MinInches = loop2.Rain15MinInches,
        HourRainInches = loop2.HourRainInches,
        Rain24HourInches = loop2.Rain24HourInches,
        StormRainInches = loop2.StormRainInches ?? loop1.StormRainInches,
        StormStartDate = loop2.StormStartDate ?? loop1.StormStartDate,

        // Solar / UV / ET
        SolarRadiationWm2 = loop2.SolarRadiationWm2 ?? loop1.SolarRadiationWm2,
        UvIndex = loop2.UvIndex ?? loop1.UvIndex,
        DailyEtInches = loop2.DailyEtInches ?? loop1.DailyEtInches,

        // LOOP1-only fields
        MonthlyRainInches = loop1.MonthlyRainInches,
        YearlyRainInches = loop1.YearlyRainInches,
        MonthlyEtInches = loop1.MonthlyEtInches,
        YearlyEtInches = loop1.YearlyEtInches,
        ConsoleBatteryVoltage = loop1.ConsoleBatteryVoltage,
        TransmitterBatteryStatus = loop1.TransmitterBatteryStatus,
        ForecastIcons = loop1.ForecastIcons,
        ForecastRule = loop1.ForecastRule,
        SunriseTime = loop1.SunriseTime,
        SunsetTime = loop1.SunsetTime,
        ExtraTemperaturesF = loop1.ExtraTemperaturesF,
        SoilTemperaturesF = loop1.SoilTemperaturesF,
        ExtraHumiditiesPercent = loop1.ExtraHumiditiesPercent,
        SoilMoisturesCb = loop1.SoilMoisturesCb,
        LeafWetnessScaled = loop1.LeafWetnessScaled,
    };

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
        var records = await ReadArchiveBatchAsync(since, maxRecords, fallbackOnEmpty, ct);
        foreach (var record in records)
        {
            ct.ThrowIfCancellationRequested();
            yield return record;
        }
    }

    private async Task<IReadOnlyList<ArchiveRecord>> ReadArchiveBatchAsync(
        DateTime since,
        int maxRecords,
        bool fallbackOnEmpty,
        CancellationToken ct)
    {
        await EnterCommandScopeAsync(ct);
        var records = new List<ArchiveRecord>(Math.Min(maxRecords, 250));
        bool completed = false;
        try
        {
            bool explicitFullArchiveRequest = since == DateTime.MinValue;
            await _client.SendDataAsync(Encoding.ASCII.GetBytes($"{DavisProtocol.CmdDmpaft}\n"), ct);

            // Encode date/time stamp for DMPAFT
            byte[] dateStamp = EncodeDmpaftDate(since);
            await _client.SendDataWithCrc16Async(dateStamp, ct, maxTries: _maxTries);

            // Read page/index response
            byte[] resp = await _client.GetDataWithCrc16Async(DavisProtocol.DmpaftResponseBytes, ct, maxTries: _maxTries);
            int nPages = BinaryPrimitives.ReadUInt16LittleEndian(resp[0..]);
            int startIndex = BinaryPrimitives.ReadUInt16LittleEndian(resp[2..]);
            _logger.LogDebug("DMPAFT: {Pages} pages, start index {Idx}", nPages, startIndex);

            // Some Davis firmware returns 0 pages when 'since' predates the entire circular
            // buffer (all stored records are newer).  Re-issue with an all-records request so
            // the oldest available records are returned.
            if (nPages == 0 && since != DateTime.MinValue && fallbackOnEmpty)
            {
                _logger.LogDebug("DMPAFT: 0 pages for {Since}; retrying with full archive", since);
                await EnsureCommandModeLockedAsync(ct);
                await _client.SendDataAsync(Encoding.ASCII.GetBytes($"{DavisProtocol.CmdDmpaft}\n"), ct);
                await _client.SendDataWithCrc16Async(EncodeDmpaftDate(DateTime.MinValue), ct, maxTries: _maxTries);
                resp = await _client.GetDataWithCrc16Async(DavisProtocol.DmpaftResponseBytes, ct, maxTries: _maxTries);
                nPages = BinaryPrimitives.ReadUInt16LittleEndian(resp[0..]);
                startIndex = BinaryPrimitives.ReadUInt16LittleEndian(resp[2..]);
                _logger.LogDebug("DMPAFT full archive: {Pages} pages, start index {Idx}", nPages, startIndex);
            }

            if (!explicitFullArchiveRequest && nPages == DavisProtocol.FullArchivePageCount)
            {
                _logger.LogInformation(
                    "DMPAFT returned the full circular buffer for cursor {Since}; cancelling to preserve live LOOP acquisition",
                    since);
                await CancelArchiveDownloadAsync(0, nPages, ct);
                completed = true;
                return records;
            }

            DateTime lastGoodTs = since;
            bool reachedRequestedWindow = since == DateTime.MinValue;

            for (int page = 0; page < nPages && !ct.IsCancellationRequested; page++)
            {
                byte[] pageData = await _client.GetDataWithCrc16Async(
                    DavisProtocol.ArchivePageBytes, ct,
                    prompt: [DavisProtocol.Ack], maxTries: _maxTries);

                for (int idx = startIndex; idx < DavisProtocol.ArchiveRecordsPerPage; idx++)
                {
                    int start = 1 + DavisProtocol.ArchiveRecordBytes * idx;
                    var rec = ArchiveRecord.Parse(
                        pageData.AsSpan(start, DavisProtocol.ArchiveRecordBytes),
                        RainBucketType, ArchiveIntervalSeconds / 60);

                    if (rec is null)
                    {
                        _logger.LogDebug("DMPAFT: empty record at page {P} index {I}", page, idx);
                        await CancelArchiveDownloadAsync(page + 1, nPages, ct);
                        completed = true;
                        return records;
                    }

                    if (!reachedRequestedWindow && rec.DateTimeLocal < since)
                    {
                        _logger.LogDebug(
                            "DMPAFT: skipping stale circular-buffer record at {RecordTime}; requested {Since}",
                            rec.DateTimeLocal,
                            since);
                        continue;
                    }

                    reachedRequestedWindow = true;
                    if (lastGoodTs != DateTime.MinValue && rec.DateTimeLocal <= lastGoodTs.AddSeconds(-7200))
                    {
                        _logger.LogDebug("DMPAFT: timestamp declining, done");
                        await CancelArchiveDownloadAsync(page + 1, nPages, ct);
                        completed = true;
                        return records;
                    }

                    lastGoodTs = rec.DateTimeLocal;
                    records.Add(rec);

                    if (records.Count >= maxRecords)
                    {
                        _logger.LogDebug("DMPAFT: maxRecords {Max} reached, stopping early", maxRecords);
                        await CancelArchiveDownloadAsync(page + 1, nPages, ct);
                        completed = true;
                        return records;
                    }
                }
                startIndex = 0; // Only the first page uses the returned start index
            }
            completed = true;
            return records;
        }
        finally
        {
            try
            {
                SetConsoleMode(completed ? ConsoleSessionMode.Command : ConsoleSessionMode.Unknown);
            }
            finally
            {
                _lock.Release();
            }
        }
    }

    private async Task CancelArchiveDownloadAsync(int nextPage, int pageCount, CancellationToken ct)
    {
        if (nextPage >= pageCount)
            return;

        _logger.LogDebug("DMPAFT: cancelling with {RemainingPages} unread page(s)", pageCount - nextPage);
        await _client.WriteAsync([DavisProtocol.Escape], ct);
    }

    // ── Station interrogation — READ ──────────────────────────────────────────

    /// <summary>Read hardware type, model, firmware version and date, and current console time.</summary>
    public async Task<StationInfo> GetStationInfoAsync(CancellationToken ct = default)
    {
        await EnterCommandScopeAsync(ct);
        try
        {
            // Hardware type: WRD command — response is ACK then 1 hardware-type byte
            await _client.SendDataAsync(Encoding.ASCII.GetBytes($"{DavisProtocol.CmdWrd}{(char)0x12}{(char)0x4D}\n"), ct);
            byte[] hwByte = await _client.ReadExactAsync(1, ct);
            int hwType = hwByte[0];
            int model = hwType switch
            {
                DavisProtocol.HardwareVantagePro => 1,
                DavisProtocol.HardwareVantageVue => 2,
                _ => ModelType,
            };

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
        await EnterCommandScopeAsync(ct);
        try
        {
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
        await EnterCommandScopeAsync(ct);
        try
        {
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
                bool reTx = retransmit == ch;
                useTx >>= 1;

                int? extraTemp = txType is 1 or 3 ? DecodeExtraTemperatureSensorId(upper & 0x0F) : null;
                int? extraHumid = txType is 2 or 3 ? DecodeExtraHumiditySensorId(upper >> 4) : null;

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
        await EnterCommandScopeAsync(ct);
        try
        {
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
        await EnterCommandScopeAsync(ct);
        try
        {
            string[] lines = await _client.SendCommandAsync($"{DavisProtocol.CmdBardata}\n", ct, _maxTries);
            // Lines: "BAR  29.990", "Elevation  4500", "DEW POINT  55", "VIRTUAL TEMP  62",
            //        "C  2.3", "R  1.003", "BARCAL  0.012", "GAIN  1", "OFFSET  0"
            return new BarometerData
            {
                CurrentPressureInHg = ParseBarometerInHg(lines, 0, 1, DavisProtocol.CmdBardata),
                AltitudeFeet = ParseRequiredDouble(lines, 1, 1, DavisProtocol.CmdBardata),
                DewPointF = ParseRequiredDouble(lines, 2, 2, DavisProtocol.CmdBardata),
                VirtualTemperatureF = ParseRequiredDouble(lines, 3, 2, DavisProtocol.CmdBardata),
                CorrectionFactor = ParseRequiredDouble(lines, 4, 1, DavisProtocol.CmdBardata),
                CorrectionRatio = ParseRequiredDouble(lines, 5, 1, DavisProtocol.CmdBardata),
                CorrectionConstantInHg = ParseRequiredDouble(lines, 6, 1, DavisProtocol.CmdBardata),
                Gain = ParseRequiredDouble(lines, 7, 1, DavisProtocol.CmdBardata),
                ErrorOffset = ParseRequiredDouble(lines, 8, 1, DavisProtocol.CmdBardata),
            };
        }
        finally { _lock.Release(); }
    }

    /// <summary>Read ISS reception statistics using the RXCHECK command.</summary>
    public async Task<ReceptionStats> GetReceptionStatsAsync(CancellationToken ct = default)
    {
        await EnterCommandScopeAsync(ct);
        try
        {
            string[] lines = await _client.SendCommandAsync($"{DavisProtocol.CmdRxcheck}\n", ct, _maxTries);
            return new ReceptionStats
            {
                TotalPacketsReceived = ParseRequiredInt(lines, 0, 0, DavisProtocol.CmdRxcheck),
                TotalPacketsMissed = ParseRequiredInt(lines, 0, 1, DavisProtocol.CmdRxcheck),
                NumberOfResynchronizations = ParseRequiredInt(lines, 0, 2, DavisProtocol.CmdRxcheck),
                LongestGoodStretch = ParseRequiredInt(lines, 0, 3, DavisProtocol.CmdRxcheck),
                NumberOfCrcErrors = ParseRequiredInt(lines, 0, 4, DavisProtocol.CmdRxcheck),
            };
        }
        finally { _lock.Release(); }
    }

    /// <summary>Read the transmitter IDs that the console can currently hear.</summary>
    public async Task<IReadOnlyList<int>> GetHeardTransmitterIdsAsync(CancellationToken ct = default)
    {
        await EnterCommandScopeAsync(ct);
        try
        {
            byte[] raw = await _client.SendCommandRawAsync($"{DavisProtocol.CmdReceivers}\n", ct, _maxTries);
            if (raw.Length == 0)
                return [];

            byte heardMask = raw[^1];
            var heard = new List<int>(8);
            for (int ch = 1; ch <= 8; ch++)
            {
                if ((heardMask & (1 << (ch - 1))) != 0)
                    heard.Add(ch);
            }

            return heard.AsReadOnly();
        }
        finally { _lock.Release(); }
    }

    /// <summary>Read the EEPROM-backed console alarm thresholds.</summary>
    public async Task<AlarmThresholds> GetAlarmThresholdsAsync(CancellationToken ct = default)
    {
        await EnterCommandScopeAsync(ct);
        try
        {
            byte[] block = await ReadEepromAsync(DavisProtocol.EepromAlarmStart, DavisProtocol.EepromAlarmBlockSize, ct);
            return DecodeAlarmThresholds(block, RainBucketType);
        }
        finally { _lock.Release(); }
    }

    /// <summary>Write the full EEPROM-backed console alarm threshold block.</summary>
    public async Task SetAlarmThresholdsAsync(AlarmThresholds thresholds, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(thresholds);
        ValidateAlarmThresholds(thresholds, RainBucketType);

        await EnterCommandScopeAsync(ct);
        try
        {
            byte[] payload = EncodeAlarmThresholds(thresholds, RainBucketType);
            await WriteEepromAsync(DavisProtocol.EepromAlarmStart, payload, ct);
        }
        finally { _lock.Release(); }
    }

    /// <summary>Clear all configured console alarm thresholds and wait for DONE.</summary>
    public async Task ClearAlarmThresholdsAsync(CancellationToken ct = default)
    {
        await EnterCommandScopeAsync(ct);
        try
        {
            await _client.SendCommandUntilLineAsync($"{DavisProtocol.CmdClralm}\n", "DONE", ct, maxTries: _maxTries);
        }
        finally { _lock.Release(); }
    }

    /// <summary>Clear any currently-active console alarm bits.</summary>
    public async Task ClearActiveAlarmBitsAsync(CancellationToken ct = default)
    {
        await EnterCommandScopeAsync(ct);
        try
        {
            await _client.SendDataAsync(Encoding.ASCII.GetBytes($"{DavisProtocol.CmdClrbits}\n"), ct);
        }
        finally { _lock.Release(); }
    }

    /// <summary>Read the current console clock time.</summary>
    public async Task<DateTime> GetConsoleTimeAsync(CancellationToken ct = default)
    {
        await EnterCommandScopeAsync(ct);
        try
        {
            return await GetConsoleTimeInternalAsync(ct);
        }
        finally { _lock.Release(); }
    }

    // ── Station configuration — WRITE ─────────────────────────────────────────

    /// <summary>Set the console clock to the current system time.</summary>
    public Task SetConsoleTimeAsync(CancellationToken ct = default) => SetConsoleTimeAsync(DateTime.Now.AddSeconds(0.75), ct);

    /// <summary>Set the console clock to an explicit local date and time.</summary>
    public async Task SetConsoleTimeAsync(DateTime consoleLocalTime, CancellationToken ct = default)
    {
        await EnterCommandScopeAsync(ct);
        try
        {
            await _client.SendDataAsync(Encoding.ASCII.GetBytes($"{DavisProtocol.CmdSettime}\n"), ct);
            DateTime t = DateTime.SpecifyKind(consoleLocalTime, DateTimeKind.Unspecified);
            byte[] payload =
            [
                (byte)t.Second, (byte)t.Minute, (byte)t.Hour,
                (byte)t.Day, (byte)t.Month, (byte)(t.Year - 1900)
            ];
            await _client.SendDataWithCrc16Async(payload, ct, maxTries: _maxTries);
            _logger.LogInformation("Console clock set to {T}", t);
        }
        finally { _lock.Release(); }
    }

    /// <summary>Set barometer calibration to a known-correct pressure and altitude.</summary>
    public async Task SetBarometerAsync(double pressureInHg, double altitudeFt, CancellationToken ct = default)
    {
        await EnterCommandScopeAsync(ct);
        try
        {
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

        await EnterCommandScopeAsync(ct);
        try
        {
            await _client.SendCommandAsync($"{DavisProtocol.CmdSetper} {minutes}\n", ct, _maxTries);
            ArchiveIntervalSeconds = minutes * 60;
        }
        finally { _lock.Release(); }
    }

    /// <summary>Update rain/archive settings in a single command scope.</summary>
    public async Task UpdateRainArchiveSettingsAsync(int archiveIntervalMinutes, int rainBucketType, int rainYearStartMonth, CancellationToken ct = default)
    {
        int[] validArchiveIntervals = [1, 5, 10, 15, 30, 60, 120];
        if (!validArchiveIntervals.Contains(archiveIntervalMinutes))
            throw new ArgumentException($"Invalid archive interval {archiveIntervalMinutes}. Must be one of: {string.Join(", ", validArchiveIntervals)}", nameof(archiveIntervalMinutes));

        if (rainBucketType is < 0 or > 2)
            throw new ArgumentException("Bucket code must be 0, 1, or 2", nameof(rainBucketType));

        if (rainYearStartMonth is < 1 or > 12)
            throw new ArgumentException("Month must be 1-12", nameof(rainYearStartMonth));

        await EnterCommandScopeAsync(ct);
        try
        {
            await _client.SendCommandAsync($"{DavisProtocol.CmdSetper} {archiveIntervalMinutes}\n", ct, _maxTries);

            byte[] setupBits = await ReadEepromAsync(DavisProtocol.EepromSetupBits, 1, ct);
            setupBits[0] = (byte)((setupBits[0] & 0xCF) | (rainBucketType << 4));
            await WriteEepromAsync(DavisProtocol.EepromSetupBits, setupBits, ct);
            await WriteEepromAsync(DavisProtocol.EepromRainYearStart, [(byte)rainYearStartMonth], ct);
            await RunNewSetupAsync(ct);

            ArchiveIntervalSeconds = archiveIntervalMinutes * 60;
            RainBucketType = rainBucketType;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Set the station latitude (decimal degrees, +N/−S).</summary>
    public async Task SetLatitudeAsync(double latitude, CancellationToken ct = default)
    {
        short val = (short)(latitude * 10);
        await WriteEepromShortAsync(DavisProtocol.EepromLatitude, val, ct);
        LatitudeDegrees = val / 10.0;
        await RunNewSetupAsync(ct);
    }

    /// <summary>Set the station longitude (decimal degrees, +E/−W).</summary>
    public async Task SetLongitudeAsync(double longitude, CancellationToken ct = default)
    {
        short val = (short)(longitude * 10);
        await WriteEepromShortAsync(DavisProtocol.EepromLongitude, val, ct);
        LongitudeDegrees = val / 10.0;
        await RunNewSetupAsync(ct);
    }

    /// <summary>Set the station altitude (feet).</summary>
    public async Task SetAltitudeAsync(double feet, CancellationToken ct = default)
    {
        short val = (short)feet;
        await WriteEepromShortAsync(DavisProtocol.EepromAltitude, val, ct);
        AltitudeFeet = val;
    }

    /// <summary>Update the location, timezone, and logging settings in a single command scope.</summary>
    public async Task UpdateLocationSettingsAsync(
        double latitude,
        double longitude,
        double altitudeFeet,
        DstMode dstMode,
        int timeZoneCode,
        TempLogging temperatureLogging,
        CancellationToken ct = default)
    {
        if (timeZoneCode is < 0 or > 31)
            throw new ArgumentException("Timezone code must be 0-31", nameof(timeZoneCode));

        short latitudeValue = (short)(latitude * 10);
        short longitudeValue = (short)(longitude * 10);
        short altitudeValue = (short)altitudeFeet;
        byte dstModeValue = dstMode == DstMode.Auto ? (byte)0 : (byte)1;
        byte dstBitValue = dstMode == DstMode.On ? (byte)1 : (byte)0;
        byte tempLoggingValue = temperatureLogging == TempLogging.Last ? (byte)1 : (byte)0;

        await EnterCommandScopeAsync(ct);
        try
        {
            byte[] latitudeBytes = new byte[2];
            BinaryPrimitives.WriteInt16LittleEndian(latitudeBytes, latitudeValue);
            await WriteEepromAsync(DavisProtocol.EepromLatitude, latitudeBytes, ct);

            byte[] longitudeBytes = new byte[2];
            BinaryPrimitives.WriteInt16LittleEndian(longitudeBytes, longitudeValue);
            await WriteEepromAsync(DavisProtocol.EepromLongitude, longitudeBytes, ct);

            byte[] altitudeBytes = new byte[2];
            BinaryPrimitives.WriteInt16LittleEndian(altitudeBytes, altitudeValue);
            await WriteEepromAsync(DavisProtocol.EepromAltitude, altitudeBytes, ct);

            await WriteEepromAsync(DavisProtocol.EepromManOrAuto, [dstModeValue], ct);
            if (dstMode != DstMode.Auto)
            {
                await WriteEepromAsync(DavisProtocol.EepromDaylightSavings, [dstBitValue], ct);
            }

            await WriteEepromAsync(DavisProtocol.EepromGmtOrZone, [0], ct);
            await WriteEepromAsync(DavisProtocol.EepromTimezoneCode, [(byte)timeZoneCode], ct);
            await WriteEepromAsync(DavisProtocol.EepromTempLogging, [tempLoggingValue], ct);
            await RunNewSetupAsync(ct);

            LatitudeDegrees = latitudeValue / 10.0;
            LongitudeDegrees = longitudeValue / 10.0;
            AltitudeFeet = altitudeValue;
            UseTimezoneCode = true;
            TimezoneCode = timeZoneCode;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Set the rain bucket type (0=0.01in, 1=0.2mm, 2=0.1mm).</summary>
    public async Task SetRainBucketTypeAsync(int bucketCode, CancellationToken ct = default)
    {
        if (bucketCode is < 0 or > 2) throw new ArgumentException("Bucket code must be 0, 1, or 2");
        await EnterCommandScopeAsync(ct);
        try
        {
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
        await EnterCommandScopeAsync(ct);
        try
        {
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
        if (code is < 0 or > 31) throw new ArgumentException("Timezone code must be 0–31");
        await EnterCommandScopeAsync(ct);
        try
        {
            await WriteEepromAsync(DavisProtocol.EepromGmtOrZone, [0], ct);   // Use TIME_ZONE
            await WriteEepromAsync(DavisProtocol.EepromTimezoneCode, [(byte)code], ct);
            UseTimezoneCode = true;
            TimezoneCode = code;
        }
        finally { _lock.Release(); }
    }

    /// <summary>Set a custom GMT offset in hundredths of hours (e.g. -700 = UTC−7:00).</summary>
    public async Task SetTimezoneOffsetAsync(int hundredths, CancellationToken ct = default)
    {
        await EnterCommandScopeAsync(ct);
        try
        {
            await WriteEepromAsync(DavisProtocol.EepromGmtOrZone, [1], ct);   // Use GMT_OFFSET
            byte[] buf = new byte[2];
            BinaryPrimitives.WriteInt16LittleEndian(buf, (short)hundredths);
            await WriteEepromAsync(DavisProtocol.EepromGmtOffset, buf, ct);
            UseTimezoneCode = false;
            GmtOffsetHours = hundredths / 100.0;
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
        await EnterCommandScopeAsync(ct);
        try
        {
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
        ushort addr = variable switch
        {
            "inTemp" => 0x32,
            "outTemp" => 0x34,
            string v when v.StartsWith("extraTemp") && int.TryParse(v[9..], out int ei) && ei >= 1 && ei <= 7 => (ushort)(0x34 + ei),
            string v when v.StartsWith("soilTemp") && int.TryParse(v[8..], out int si) && si >= 1 && si <= 4 => (ushort)(0x3B + si),
            string v when v.StartsWith("leafTemp") && int.TryParse(v[8..], out int li) && li >= 1 && li <= 4 => (ushort)(0x3F + li),
            _ => throw new ArgumentException($"Unknown temperature variable: {variable}")
        };

        await EnterCommandScopeAsync(ct);
        try
        {
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
        ushort addr = variable switch
        {
            "inHumid" => DavisProtocol.EepromInHumidCalib,
            "outHumid" => DavisProtocol.EepromOutHumidCalib,
            string v when v.StartsWith("extraHumid") && int.TryParse(v[10..], out int hi) && hi >= 1 && hi <= 7 =>
                (ushort)(DavisProtocol.EepromOutHumidCalib + hi),
            _ => throw new ArgumentException($"Unknown humidity variable: {variable}")
        };

        await EnterCommandScopeAsync(ct);
        try
        {
            await WriteEepromAsync(addr, [(byte)(sbyte)offsetPct], ct);
        }
        finally { _lock.Release(); }
    }

    /// <summary>Set the transmitter type and configuration for a channel (1–8).</summary>
    public async Task SetTransmitterAsync(int channel, TransmitterType type,
        int? extraTempId, int? extraHumId, string? repeaterId, CancellationToken ct = default)
    {
        if (channel is < 1 or > 8) throw new ArgumentException("Channel must be 1–8");
        if (extraTempId is < 1 or > 7) throw new ArgumentException("Extra temperature sensor ID must be 1–7");
        if (extraHumId is < 1 or > 7) throw new ArgumentException("Extra humidity sensor ID must be 1–7");

        await EnterCommandScopeAsync(ct);
        try
        {
            byte[] useTxByte = await ReadEepromAsync(DavisProtocol.EepromUseTx, 1, ct);
            byte useTx = useTxByte[0];

            int repeaterCode = 0;
            if (repeaterId is not null)
                repeaterCode = (char.ToUpper(repeaterId[0]) - 'A') + 8;

            int typeBits = (int)type & 0x0F;
            if (repeaterCode != 0) typeBits |= repeaterCode << 4;

            byte extraIdBits = 0xFF;
            if (extraTempId.HasValue) extraIdBits = (byte)((extraIdBits & 0xF0) | ((extraTempId.Value - 1) & 0x0F));
            if (extraHumId.HasValue) extraIdBits = (byte)((extraIdBits & 0x0F) | (extraHumId.Value << 4));

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
        await WriteEepromByteAsync(DavisProtocol.EepromRetransmit, (byte)channel, ct);
        await RunNewSetupAsync(ct);
    }

    /// <summary>Turn the console lamp on or off.</summary>
    public async Task SetLampAsync(bool on, CancellationToken ct = default)
    {
        await EnterCommandScopeAsync(ct);
        try
        {
            await _client.SendCommandAsync($"{DavisProtocol.CmdLamps} {(on ? '1' : '0')}\n", ct, _maxTries);
        }
        finally { _lock.Release(); }
    }

    /// <summary>Clear the console's archive memory (irreversible).</summary>
    public async Task ClearArchiveAsync(CancellationToken ct = default)
    {
        await EnterCommandScopeAsync(ct);
        try
        {
            await _client.SendDataAsync(Encoding.ASCII.GetBytes($"{DavisProtocol.CmdClrlog}\n"), ct);
            _logger.LogWarning("Archive memory cleared on Davis console");
        }
        finally { _lock.Release(); }
    }

    // ── Internal helpers ──────────────────────────────────────────────────────

    private async Task ReadSetupFromEepromAsync(CancellationToken ct)
    {
        byte[] unitBits = await ReadEepromAsync(DavisProtocol.EepromUnitBits, 1, ct);
        byte[] setupBits = await ReadEepromAsync(DavisProtocol.EepromSetupBits, 1, ct);
        byte[] archByte = await ReadEepromAsync(DavisProtocol.EepromArchiveInterval, 1, ct);
        byte[] gmtOrZone = await ReadEepromAsync(DavisProtocol.EepromGmtOrZone, 1, ct);
        byte[] tzCode = await ReadEepromAsync(DavisProtocol.EepromTimezoneCode, 1, ct);
        byte[] gmtOffB = await ReadEepromAsync(DavisProtocol.EepromGmtOffset, 2, ct);
        byte[] latBytes = await ReadEepromAsync(DavisProtocol.EepromLatitude, 2, ct);
        byte[] lonBytes = await ReadEepromAsync(DavisProtocol.EepromLongitude, 2, ct);

        RainBucketType = (setupBits[0] & 0x30) >> 4;
        ArchiveIntervalSeconds = archByte[0] * 60;
        UseTimezoneCode = gmtOrZone[0] == 0;
        TimezoneCode = tzCode[0];
        GmtOffsetHours = BinaryPrimitives.ReadInt16LittleEndian(gmtOffB) / 100.0;
        LatitudeDegrees = BinaryPrimitives.ReadInt16LittleEndian(latBytes) / 10.0;
        LongitudeDegrees = BinaryPrimitives.ReadInt16LittleEndian(lonBytes) / 10.0;
        BarometerUnits = BaroUnitName(unitBits[0] & 0x03);
        TemperatureUnits = TempUnitName((unitBits[0] & 0x0C) >> 2);
        RainUnits = (unitBits[0] & 0x20) != 0 ? "mm" : "inch";
        WindUnits = WindUnitName((unitBits[0] & 0xC0) >> 6);
        _setupHydrated = true;
    }

    private async Task<DateTime> GetConsoleTimeInternalAsync(CancellationToken ct)
    {
        await _client.SendDataAsync(Encoding.ASCII.GetBytes($"{DavisProtocol.CmdGettime}\n"), ct);
        byte[] buf = await _client.GetDataWithCrc16Async(8, ct, maxTries: _maxTries);
        // Layout: sec, min, hr, day, mon, yr (since 1900), [2 CRC]
        int sec = buf[0], min = buf[1], hr = buf[2], day = buf[3], mon = buf[4], yr = buf[5] + 1900;
        return new DateTime(yr, mon, day, hr, min, sec, DateTimeKind.Local);
    }

    private async Task<byte[]> ReadEepromAsync(ushort address, int bytes, CancellationToken ct)
    {
        string cmd = $"{DavisProtocol.CmdEebrd} {address:X} {bytes:X}\n";
        byte[] cmdBytes = Encoding.ASCII.GetBytes(cmd);
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                await _client.SendDataAsync(cmdBytes, ct);
                break;
            }
            catch (DavisException ex) when (attempt < _maxTries)
            {
                _logger.LogDebug(ex, "EEPROM read command {Command} attempt {Attempt} failed", cmd.TrimEnd(), attempt);
            }
        }

        byte[] data = await _client.GetDataWithCrc16Async(bytes + 2, ct, maxTries: _maxTries);
        return data[..bytes];
    }

    private async Task WriteEepromAsync(ushort address, byte[] data, CancellationToken ct)
    {
        string cmd = $"{DavisProtocol.CmdEebwr} {address:X} {data.Length:X}\n";
        await _client.SendDataAsync(Encoding.ASCII.GetBytes(cmd), ct);
        await _client.SendDataWithCrc16Async(data, ct, maxTries: _maxTries);
    }

    private async Task WriteEepromByteAsync(ushort address, byte value, CancellationToken ct)
    {
        await EnterCommandScopeAsync(ct);
        try
        {
            await WriteEepromAsync(address, [value], ct);
        }
        finally { _lock.Release(); }
    }

    private async Task WriteEepromShortAsync(ushort address, short value, CancellationToken ct)
    {
        byte[] buf = new byte[2];
        BinaryPrimitives.WriteInt16LittleEndian(buf, value);
        await EnterCommandScopeAsync(ct);
        try
        {
            await WriteEepromAsync(address, buf, ct);
        }
        finally { _lock.Release(); }
    }

    private async Task RunNewSetupAsync(CancellationToken ct) =>
        await _client.SendDataAsync(Encoding.ASCII.GetBytes($"{DavisProtocol.CmdNewsetup}\n"), ct);

    private async Task EnterCommandScopeAsync(CancellationToken ct, bool interruptLoop = true)
    {
        if (interruptLoop)
        {
            RequestLoopInterruption();
        }

        await _lock.WaitAsync(ct);
        try
        {
            await EnsureCommandModeLockedAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "EnsureCommandMode failed; releasing lock and rethrowing");
            _lock.Release();
            throw;
        }
    }

    private async Task EnsureCommandModeLockedAsync(CancellationToken ct)
    {
        switch (GetConsoleMode())
        {
            case ConsoleSessionMode.Command:
                return;
            case ConsoleSessionMode.Loop:
                await _client.CancelLoopAsync(ct);
                SetConsoleMode(ConsoleSessionMode.Command);
                return;
            default:
                await _client.WakeAsync(_maxTries, ct);
                SetConsoleMode(ConsoleSessionMode.Command);
                return;
        }
    }

    private CancellationToken BeginLoopSession(CancellationToken ct, out CancellationTokenSource interruptionCts)
    {
        interruptionCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        lock (_loopStateLock)
        {
            _activeLoopInterruption?.Dispose();
            _activeLoopInterruption = interruptionCts;
            _consoleMode = ConsoleSessionMode.Loop;
        }

        return interruptionCts.Token;
    }

    private void EndLoopSession(CancellationTokenSource? interruptionCts, bool completedNormally)
    {
        lock (_loopStateLock)
        {
            if (interruptionCts is not null && ReferenceEquals(_activeLoopInterruption, interruptionCts))
            {
                _activeLoopInterruption = null;
            }

            _consoleMode = completedNormally ? ConsoleSessionMode.Command : ConsoleSessionMode.Unknown;
        }

        interruptionCts?.Dispose();
    }

    private void RequestLoopInterruption()
    {
        CancellationTokenSource? interruptionCts;
        lock (_loopStateLock)
        {
            interruptionCts = _activeLoopInterruption;
        }

        if (interruptionCts is null || interruptionCts.IsCancellationRequested)
        {
            return;
        }

        try
        {
            _client.CancelLoop();
        }
        catch (Exception ex)
        {
            // Best-effort cancellation so queued commands do not wait for a full loop batch.
            _logger.LogDebug(ex, "Best-effort CancelLoop ignored an exception");
        }

        interruptionCts.Cancel();
    }

    private void SetConsoleMode(ConsoleSessionMode mode)
    {
        lock (_loopStateLock)
        {
            _consoleMode = mode;
        }
    }

    private ConsoleSessionMode GetConsoleMode()
    {
        lock (_loopStateLock)
        {
            return _consoleMode;
        }
    }

    private void ResetConsoleState()
    {
        CancellationTokenSource? interruptionCts;
        lock (_loopStateLock)
        {
            interruptionCts = _activeLoopInterruption;
            _activeLoopInterruption = null;
            _consoleMode = ConsoleSessionMode.Unknown;
        }

        if (interruptionCts is null)
        {
            return;
        }

        try
        {
            interruptionCts.Cancel();
        }
        catch (Exception ex)
        {
            // Ignore cancellation races during shutdown/reconnect.
            _logger.LogDebug(ex, "Ignored exception during CancellationTokenSource.Cancel during reset");
        }

        interruptionCts.Dispose();
    }

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

    private static double ParseRequiredDouble(string[] lines, int lineIdx, int wordIdx, string command)
    {
        string[] parts = ParseRequiredParts(lines, lineIdx, wordIdx + 1, command);
        if (double.TryParse(parts[wordIdx], NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            return value;

        throw new DavisProtocolException($"Command '{command}' returned non-numeric value '{parts[wordIdx]}' at line {lineIdx + 1}, field {wordIdx + 1}.");
    }

    private static double ParseBarometerInHg(string[] lines, int lineIdx, int wordIdx, string command)
    {
        double value = ParseRequiredDouble(lines, lineIdx, wordIdx, command);
        return value > 1000 ? value / 1000.0 : value;
    }

    private static int ParseRequiredInt(string[] lines, int lineIdx, int wordIdx, string command)
    {
        string[] parts = ParseRequiredParts(lines, lineIdx, wordIdx + 1, command);
        if (int.TryParse(parts[wordIdx], NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
            return value;

        throw new DavisProtocolException($"Command '{command}' returned non-integer value '{parts[wordIdx]}' at line {lineIdx + 1}, field {wordIdx + 1}.");
    }

    private static string[] ParseRequiredParts(string[] lines, int lineIdx, int minWordCount, string command)
    {
        if (lineIdx >= lines.Length)
            throw new DavisProtocolException($"Command '{command}' returned {lines.Length} line(s); expected at least {lineIdx + 1}.");

        string[] parts = lines[lineIdx].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < minWordCount)
            throw new DavisProtocolException($"Command '{command}' returned too few fields on line {lineIdx + 1}: '{lines[lineIdx]}'.");

        return parts;
    }

    private static ushort ReadUshort(byte[] buffer, int offset) =>
        BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(offset, 2));

    private static string BaroUnitName(int code) => code switch { 1 => "mmHg", 2 => "hPa", 3 => "mbar", _ => "inHg" };
    private static string TempUnitName(int code) => code switch { 1 => "°F×10", 2 => "°C", 3 => "°C×10", _ => "°F" };
    private static string WindUnitName(int code) => code switch { 1 => "m/s", 2 => "km/h", 3 => "knots", _ => "mph" };
    private static int? DecodeExtraTemperatureSensorId(int raw) => raw == 0x0F ? null : raw + 1;
    private static int? DecodeExtraHumiditySensorId(int raw) => raw == 0x0F ? null : raw;
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

    private static AlarmThresholds DecodeAlarmThresholds(byte[] block, int bucketType)
    {
        return new AlarmThresholds
        {
            RisingBarTrendInHg = block[0] == 0 ? null : block[0] / 1000.0,
            FallingBarTrendInHg = block[1] == 0 ? null : block[1] / 1000.0,
            TimeAlarm = DecodeTimeAlarm(BinaryPrimitives.ReadUInt16LittleEndian(block.AsSpan(2, 2))),
            LowInsideTemperatureF = DecodeOffsetAlarm(block[6], 90),
            HighInsideTemperatureF = DecodeOffsetAlarm(block[7], 90),
            LowOutsideTemperatureF = DecodeOffsetAlarm(block[8], 90),
            HighOutsideTemperatureF = DecodeOffsetAlarm(block[9], 90),
            LowExtraTemperaturesF = DecodeOffsetArray(block, 10, 7, 90),
            LowSoilTemperaturesF = DecodeOffsetArray(block, 17, 4, 90),
            LowLeafTemperaturesF = DecodeOffsetArray(block, 21, 4, 90),
            HighExtraTemperaturesF = DecodeOffsetArray(block, 25, 7, 90),
            HighSoilTemperaturesF = DecodeOffsetArray(block, 32, 4, 90),
            HighLeafTemperaturesF = DecodeOffsetArray(block, 36, 4, 90),
            LowInsideHumidityPercent = DecodeDirectAlarm(block[40]),
            HighInsideHumidityPercent = DecodeDirectAlarm(block[41]),
            LowOutsideHumidityPercent = DecodeDirectAlarm(block[42]),
            LowExtraHumidityPercent = DecodeDirectArray(block, 43, 7),
            HighOutsideHumidityPercent = DecodeDirectAlarm(block[50]),
            HighExtraHumidityPercent = DecodeDirectArray(block, 51, 7),
            LowDewPointF = DecodeOffsetAlarm(block[58], 120),
            HighDewPointF = DecodeOffsetAlarm(block[59], 120),
            LowWindChillF = DecodeOffsetAlarm(block[60], 120),
            HighHeatIndexF = DecodeOffsetAlarm(block[61], 90),
            HighThswF = DecodeOffsetAlarm(block[62], 90),
            WindSpeedMph = DecodeDirectAlarm(block[63]),
            WindSpeed10MinuteMph = DecodeDirectAlarm(block[64]),
            UvIndex = block[65] == 0xFF ? null : block[65] / 10.0,
            UvDoseMeds = block[66] == 0xFF ? null : block[66] / 10.0,
            LowSoilMoistureCb = DecodeDirectArray(block, 67, 4),
            HighSoilMoistureCb = DecodeDirectArray(block, 71, 4),
            LowLeafWetness = DecodeDirectArray(block, 75, 4),
            HighLeafWetness = DecodeDirectArray(block, 79, 4),
            SolarRadiationWm2 = DecodeNullableUshort(block, 83),
            RainRateInchesPerHour = Loop2Packet.DecodeRain(ReadUshort(block, 85), bucketType),
            Rain15MinuteInches = Loop2Packet.DecodeRain(ReadUshort(block, 87), bucketType),
            Rain24HourInches = Loop2Packet.DecodeRain(ReadUshort(block, 89), bucketType),
            RainStormInches = Loop2Packet.DecodeRain(ReadUshort(block, 91), bucketType),
            DailyEtInches = block[93] == 0xFF ? null : block[93] / 1000.0,
        };
    }

    private static byte[] EncodeAlarmThresholds(AlarmThresholds thresholds, int bucketType)
    {
        var block = Enumerable.Repeat((byte)0xFF, DavisProtocol.EepromAlarmBlockSize).ToArray();

        block[0] = thresholds.RisingBarTrendInHg is double rise ? checked((byte)Math.Clamp((int)Math.Round(rise * 1000), 1, 255)) : (byte)0;
        block[1] = thresholds.FallingBarTrendInHg is double fall ? checked((byte)Math.Clamp((int)Math.Round(fall * 1000), 1, 255)) : (byte)0;

        ushort time = EncodeTimeAlarm(thresholds.TimeAlarm);
        BinaryPrimitives.WriteUInt16LittleEndian(block.AsSpan(2, 2), time);
        BinaryPrimitives.WriteUInt16LittleEndian(block.AsSpan(4, 2), (ushort)~time);

        block[6] = EncodeOffsetAlarm(thresholds.LowInsideTemperatureF, 90);
        block[7] = EncodeOffsetAlarm(thresholds.HighInsideTemperatureF, 90);
        block[8] = EncodeOffsetAlarm(thresholds.LowOutsideTemperatureF, 90);
        block[9] = EncodeOffsetAlarm(thresholds.HighOutsideTemperatureF, 90);
        EncodeOffsetArray(block, 10, thresholds.LowExtraTemperaturesF, 7, 90);
        EncodeOffsetArray(block, 17, thresholds.LowSoilTemperaturesF, 4, 90);
        EncodeOffsetArray(block, 21, thresholds.LowLeafTemperaturesF, 4, 90);
        EncodeOffsetArray(block, 25, thresholds.HighExtraTemperaturesF, 7, 90);
        EncodeOffsetArray(block, 32, thresholds.HighSoilTemperaturesF, 4, 90);
        EncodeOffsetArray(block, 36, thresholds.HighLeafTemperaturesF, 4, 90);

        block[40] = EncodeDirectAlarm(thresholds.LowInsideHumidityPercent);
        block[41] = EncodeDirectAlarm(thresholds.HighInsideHumidityPercent);
        block[42] = EncodeDirectAlarm(thresholds.LowOutsideHumidityPercent);
        EncodeDirectArray(block, 43, thresholds.LowExtraHumidityPercent, 7);
        block[50] = EncodeDirectAlarm(thresholds.HighOutsideHumidityPercent);
        EncodeDirectArray(block, 51, thresholds.HighExtraHumidityPercent, 7);

        block[58] = EncodeOffsetAlarm(thresholds.LowDewPointF, 120);
        block[59] = EncodeOffsetAlarm(thresholds.HighDewPointF, 120);
        block[60] = EncodeOffsetAlarm(thresholds.LowWindChillF, 120);
        block[61] = EncodeOffsetAlarm(thresholds.HighHeatIndexF, 90);
        block[62] = EncodeOffsetAlarm(thresholds.HighThswF, 90);
        block[63] = EncodeDirectAlarm(thresholds.WindSpeedMph);
        block[64] = EncodeDirectAlarm(thresholds.WindSpeed10MinuteMph);
        block[65] = thresholds.UvIndex is double uv ? checked((byte)Math.Round(uv * 10)) : (byte)0xFF;
        block[66] = thresholds.UvDoseMeds is double dose ? checked((byte)Math.Round(dose * 10)) : (byte)0xFF;
        EncodeDirectArray(block, 67, thresholds.LowSoilMoistureCb, 4);
        EncodeDirectArray(block, 71, thresholds.HighSoilMoistureCb, 4);
        EncodeDirectArray(block, 75, thresholds.LowLeafWetness, 4);
        EncodeDirectArray(block, 79, thresholds.HighLeafWetness, 4);
        EncodeNullableUshort(block, 83, thresholds.SolarRadiationWm2);
        EncodeNullableRain(block, 85, thresholds.RainRateInchesPerHour, bucketType);
        EncodeNullableRain(block, 87, thresholds.Rain15MinuteInches, bucketType);
        EncodeNullableRain(block, 89, thresholds.Rain24HourInches, bucketType);
        EncodeNullableRain(block, 91, thresholds.RainStormInches, bucketType);
        block[93] = thresholds.DailyEtInches is double et ? checked((byte)Math.Round(et * 1000)) : (byte)0xFF;

        return block;
    }

    private static TimeOnly? DecodeTimeAlarm(ushort raw)
    {
        if (raw == 0xFFFF) return null;
        int hour = raw / 100;
        int minute = raw % 100;
        return hour is >= 0 and <= 23 && minute is >= 0 and <= 59 ? new TimeOnly(hour, minute) : null;
    }

    private static ushort EncodeTimeAlarm(TimeOnly? value) => value is null
        ? (ushort)0xFFFF
        : checked((ushort)(value.Value.Hour * 100 + value.Value.Minute));

    private static int? DecodeOffsetAlarm(byte raw, int offset) => raw == 0xFF ? null : raw - offset;
    private static byte EncodeOffsetAlarm(int? value, int offset) => value.HasValue
        ? checked((byte)(value.Value + offset))
        : (byte)0xFF;

    private static int? DecodeDirectAlarm(byte raw) => raw == 0xFF ? null : raw;
    private static byte EncodeDirectAlarm(int? value) => value.HasValue ? checked((byte)value.Value) : (byte)0xFF;

    private static IReadOnlyList<int?> DecodeOffsetArray(byte[] block, int offset, int count, int bias) =>
        Enumerable.Range(0, count).Select(index => DecodeOffsetAlarm(block[offset + index], bias)).ToArray();

    private static IReadOnlyList<int?> DecodeDirectArray(byte[] block, int offset, int count) =>
        Enumerable.Range(0, count).Select(index => DecodeDirectAlarm(block[offset + index])).ToArray();

    private static void EncodeOffsetArray(byte[] block, int offset, IReadOnlyList<int?> values, int count, int bias)
    {
        values ??= Array.Empty<int?>();
        for (int index = 0; index < count; index++)
            block[offset + index] = EncodeOffsetAlarm(index < values.Count ? values[index] : null, bias);
    }

    private static void EncodeDirectArray(byte[] block, int offset, IReadOnlyList<int?> values, int count)
    {
        values ??= Array.Empty<int?>();
        for (int index = 0; index < count; index++)
            block[offset + index] = EncodeDirectAlarm(index < values.Count ? values[index] : null);
    }

    private static void ValidateAlarmThresholds(AlarmThresholds thresholds, int bucketType)
    {
        ValidateRange(thresholds.RisingBarTrendInHg, 0.001, 0.255, nameof(thresholds.RisingBarTrendInHg));
        ValidateRange(thresholds.FallingBarTrendInHg, 0.001, 0.255, nameof(thresholds.FallingBarTrendInHg));

        ValidateOffsetRange(thresholds.LowInsideTemperatureF, 90, nameof(thresholds.LowInsideTemperatureF));
        ValidateOffsetRange(thresholds.HighInsideTemperatureF, 90, nameof(thresholds.HighInsideTemperatureF));
        ValidateOffsetRange(thresholds.LowOutsideTemperatureF, 90, nameof(thresholds.LowOutsideTemperatureF));
        ValidateOffsetRange(thresholds.HighOutsideTemperatureF, 90, nameof(thresholds.HighOutsideTemperatureF));
        ValidateOffsetList(thresholds.LowExtraTemperaturesF, 90, nameof(thresholds.LowExtraTemperaturesF));
        ValidateOffsetList(thresholds.HighExtraTemperaturesF, 90, nameof(thresholds.HighExtraTemperaturesF));
        ValidateOffsetList(thresholds.LowSoilTemperaturesF, 90, nameof(thresholds.LowSoilTemperaturesF));
        ValidateOffsetList(thresholds.HighSoilTemperaturesF, 90, nameof(thresholds.HighSoilTemperaturesF));
        ValidateOffsetList(thresholds.LowLeafTemperaturesF, 90, nameof(thresholds.LowLeafTemperaturesF));
        ValidateOffsetList(thresholds.HighLeafTemperaturesF, 90, nameof(thresholds.HighLeafTemperaturesF));

        ValidateDirectRange(thresholds.LowInsideHumidityPercent, nameof(thresholds.LowInsideHumidityPercent));
        ValidateDirectRange(thresholds.HighInsideHumidityPercent, nameof(thresholds.HighInsideHumidityPercent));
        ValidateDirectRange(thresholds.LowOutsideHumidityPercent, nameof(thresholds.LowOutsideHumidityPercent));
        ValidateDirectRange(thresholds.HighOutsideHumidityPercent, nameof(thresholds.HighOutsideHumidityPercent));
        ValidateDirectList(thresholds.LowExtraHumidityPercent, nameof(thresholds.LowExtraHumidityPercent));
        ValidateDirectList(thresholds.HighExtraHumidityPercent, nameof(thresholds.HighExtraHumidityPercent));

        ValidateOffsetRange(thresholds.LowDewPointF, 120, nameof(thresholds.LowDewPointF));
        ValidateOffsetRange(thresholds.HighDewPointF, 120, nameof(thresholds.HighDewPointF));
        ValidateOffsetRange(thresholds.LowWindChillF, 120, nameof(thresholds.LowWindChillF));
        ValidateOffsetRange(thresholds.HighHeatIndexF, 90, nameof(thresholds.HighHeatIndexF));
        ValidateOffsetRange(thresholds.HighThswF, 90, nameof(thresholds.HighThswF));
        ValidateDirectRange(thresholds.WindSpeedMph, nameof(thresholds.WindSpeedMph));
        ValidateDirectRange(thresholds.WindSpeed10MinuteMph, nameof(thresholds.WindSpeed10MinuteMph));
        ValidateRange(thresholds.UvIndex, 0.0, 25.4, nameof(thresholds.UvIndex));
        ValidateRange(thresholds.UvDoseMeds, 0.0, 25.4, nameof(thresholds.UvDoseMeds));

        ValidateDirectList(thresholds.LowSoilMoistureCb, nameof(thresholds.LowSoilMoistureCb));
        ValidateDirectList(thresholds.HighSoilMoistureCb, nameof(thresholds.HighSoilMoistureCb));
        ValidateDirectList(thresholds.LowLeafWetness, nameof(thresholds.LowLeafWetness));
        ValidateDirectList(thresholds.HighLeafWetness, nameof(thresholds.HighLeafWetness));
        ValidateRange(thresholds.SolarRadiationWm2, 0, ushort.MaxValue - 1, nameof(thresholds.SolarRadiationWm2));

        ValidateRainRange(thresholds.RainRateInchesPerHour, bucketType, nameof(thresholds.RainRateInchesPerHour));
        ValidateRainRange(thresholds.Rain15MinuteInches, bucketType, nameof(thresholds.Rain15MinuteInches));
        ValidateRainRange(thresholds.Rain24HourInches, bucketType, nameof(thresholds.Rain24HourInches));
        ValidateRainRange(thresholds.RainStormInches, bucketType, nameof(thresholds.RainStormInches));
        ValidateRange(thresholds.DailyEtInches, 0.0, 0.254, nameof(thresholds.DailyEtInches));
    }

    private static void ValidateOffsetList(IReadOnlyList<int?>? values, int bias, string fieldName)
    {
        if (values is null)
            return;

        for (int index = 0; index < values.Count; index++)
            ValidateOffsetRange(values[index], bias, $"{fieldName}[{index}]");
    }

    private static void ValidateDirectList(IReadOnlyList<int?>? values, string fieldName)
    {
        if (values is null)
            return;

        for (int index = 0; index < values.Count; index++)
            ValidateDirectRange(values[index], $"{fieldName}[{index}]");
    }

    private static void ValidateOffsetRange(int? value, int bias, string fieldName)
    {
        int min = -bias;
        int max = 254 - bias;
        ValidateRange(value, min, max, fieldName);
    }

    private static void ValidateDirectRange(int? value, string fieldName) =>
        ValidateRange(value, 0, 254, fieldName);

    private static void ValidateRainRange(double? inches, int bucketType, string fieldName)
    {
        if (!inches.HasValue)
            return;

        if (inches.Value < 0)
            throw new ArgumentException($"{fieldName} must be between 0 and the maximum encodable rainfall for the selected bucket type.", fieldName);

        double clicks = bucketType switch
        {
            DavisProtocol.BucketType001Inch => inches.Value * 100.0,
            DavisProtocol.BucketType02Mm => inches.Value / 0.0078740157,
            DavisProtocol.BucketType01Mm => inches.Value / 0.00393700787,
            _ => throw new ArgumentOutOfRangeException(nameof(bucketType), bucketType, "Unknown rain bucket type")
        };

        if (clicks > ushort.MaxValue - 1)
            throw new ArgumentException($"{fieldName} is too large for the selected rain bucket type.", fieldName);
    }

    private static void ValidateRange(int? value, int min, int max, string fieldName)
    {
        if (value.HasValue && (value.Value < min || value.Value > max))
            throw new ArgumentException($"{fieldName} must be between {min} and {max}.", fieldName);
    }

    private static void ValidateRange(double? value, double min, double max, string fieldName)
    {
        if (value.HasValue && (value.Value < min || value.Value > max))
            throw new ArgumentException($"{fieldName} must be between {min.ToString(CultureInfo.InvariantCulture)} and {max.ToString(CultureInfo.InvariantCulture)}.", fieldName);
    }

    private static int? DecodeNullableUshort(byte[] block, int offset)
    {
        ushort raw = ReadUshort(block, offset);
        return raw == 0xFFFF ? null : raw;
    }

    private static void EncodeNullableUshort(byte[] block, int offset, int? value)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(block.AsSpan(offset, 2), value.HasValue ? checked((ushort)value.Value) : (ushort)0xFFFF);
    }

    private static void EncodeNullableRain(byte[] block, int offset, double? inches, int bucketType)
    {
        ushort raw = inches.HasValue ? EncodeRainClicks(inches.Value, bucketType) : (ushort)0xFFFF;
        BinaryPrimitives.WriteUInt16LittleEndian(block.AsSpan(offset, 2), raw);
    }

    private static ushort EncodeRainClicks(double inches, int bucketType)
    {
        double clicks = bucketType switch
        {
            DavisProtocol.BucketType001Inch => inches * 100.0,
            DavisProtocol.BucketType02Mm => inches / 0.0078740157,
            DavisProtocol.BucketType01Mm => inches / 0.00393700787,
            _ => throw new ArgumentOutOfRangeException(nameof(bucketType), bucketType, "Unknown rain bucket type")
        };

        return checked((ushort)Math.Round(clicks));
    }

    public async ValueTask DisposeAsync()
    {
        try { await DisconnectAsync(); } catch { /* best-effort */ }
        _client.Dispose();
        _lock.Dispose();
    }
}
