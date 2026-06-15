using HVO.Hardware.DavisVantagePro2.Outbox;
using HVO.Hardware.DavisVantagePro2.Protocol.Packets;
using HVO.Hardware.DavisVantagePro2.Station;
using HVO.WebSite.Themes.Components.Charts;
using HVO.WebSite.Themes.Components.Layout;
using HVO.Astronomy;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Text.Json;

namespace HVO.Hardware.DavisVantagePro2.Components.Pages;

public partial class Status : IDisposable
{
    private const string PageHeadingText = "Davis weather overview";
    private const string PageSummaryText = "Overview template migrated onto the live Davis monitor, keeping the existing instrument cards while adopting the new MudBlazor shell and page framing.";
    private const string StationLocationText = "Hualapai Valley, AZ";
    private const string TemperatureOutsideColor = "#69d3ff";
    private const string TemperatureInsideColor = "#ffb86c";
    private const string SolarColor = "#ffd166";
    private const string WindAverageColor = "#7ae0ff";
    private const string WindGustColor = "#ff9f5a";
    private static readonly TimeSpan LiveLoopFreshnessThreshold = TimeSpan.FromSeconds(10);

    [Inject] private ILogger<Status> Logger { get; set; } = default!;
    [Inject] private IServiceScopeFactory ScopeFactory { get; set; } = default!;

    private Loop2Packet? _reading;
    private bool _disposed;
    private double? _outsidePressure24HourLowInHg;
    private double? _outsidePressure24HourHighInHg;
    private double? _insidePressure24HourLowInHg;
    private double? _insidePressure24HourHighInHg;
    private double? _outsideTemperature24HourLowF;
    private double? _outsideTemperature24HourHighF;
    private double? _todayPeakSolarWm2;
    private List<LiveHistorySample> _liveHistorySamples = [];
    private string[] _chartTimeLabels = [];
    private HvoChartDataset[] _temperatureDatasets = [];
    private HvoChartDataset[] _windDatasets = [];
    private HvoChartDataset[] _solarDatasets = [];
    private string[] _astronomicalLabels = [];
    private HvoChartDataset[] _astronomicalDatasets = [];
    private int _chartRevision;
    private MoonSnapshot _moonContext = MoonSnapshot.Empty;

    protected override async Task OnInitializedAsync()
    {
        _reading = Worker.LatestReading;
        Worker.ReadingUpdated += OnReadingUpdated;
        Worker.WorkerStateChanged += OnStateChanged;
        Forwarder.SweptCompleted += OnStateChanged;
        Logger.LogInformation(
            "Davis status page loaded. Worker errors: {Errors}, Pending outbox: {Pending}",
            Worker.ConsecutiveErrors, Forwarder.PendingCount);

        await LoadStartupReadingAsync();
        await LoadLiveHistoryAsync();
        RefreshVisuals();
    }

    private async Task LoadStartupReadingAsync()
    {
        if (_reading is not null)
        {
            return;
        }

        try
        {
            await using var scope = ScopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<OutboxDbContext>();

            var latestPersisted = await db.OutboxRecords
                .AsNoTracking()
                .Where(record => !record.IsArchiveRecord)
                .OrderByDescending(record => record.RecordedAtUtc)
                .Select(record => new { record.RecordedAtUtc, record.Payload, record.IsArchiveRecord })
                .FirstOrDefaultAsync();

            if (latestPersisted is null)
            {
                return;
            }

            var latestSnapshot = JsonSerializer.Deserialize<PersistedLiveReading>(latestPersisted.Payload);
            if (latestSnapshot is null)
            {
                Logger.LogWarning("Latest persisted Davis reading could not be deserialized for startup fallback");
                return;
            }

            _reading = latestSnapshot.ToLoop2Packet(latestPersisted.RecordedAtUtc);

            Logger.LogInformation(
                "Loaded persisted Davis reading from outbox for startup fallback at {RecordedAtUtc} (archive: {IsArchiveRecord})",
                latestPersisted.RecordedAtUtc,
                latestPersisted.IsArchiveRecord);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Unable to load persisted Davis reading for startup fallback");
        }
    }

    private async Task LoadLiveHistoryAsync()
    {
        try
        {
            await using var scope = ScopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<OutboxDbContext>();
            DateTime sinceUtc = (_reading?.RecordedAtUtc ?? DateTime.UtcNow).AddHours(-24);

            var persisted = await db.OutboxRecords
                .AsNoTracking()
                .Where(record => !record.IsArchiveRecord && record.RecordedAtUtc >= sinceUtc)
                .OrderBy(record => record.RecordedAtUtc)
                .Select(record => new { record.RecordedAtUtc, record.Payload })
                .ToListAsync();

            _liveHistorySamples = persisted
                .Select(record =>
                {
                    var snapshot = JsonSerializer.Deserialize<PersistedLiveReading>(record.Payload);
                    if (snapshot is null)
                    {
                        return null;
                    }

                    DateTime localTime = new DateTimeOffset(record.RecordedAtUtc, TimeSpan.Zero)
                        .ToOffset(Station.ConsoleUtcOffset)
                        .DateTime;

                    return CreateLiveHistorySample(localTime, snapshot);
                })
                .Where(sample => sample is not null)
                .Select(sample => sample!)
                .GroupBy(sample => StatusChartBuckets.FloorToBucket(sample.TimeLocal, StatusChartBuckets.DefaultBucketSize))
                .Select(group => group.OrderBy(sample => sample.TimeLocal).Last())
                .OrderBy(sample => sample.TimeLocal)
                .ToList();

            TrimLiveHistorySamples();
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Unable to load persisted live chart history for status dashboard");
        }
    }

    private void OnReadingUpdated(Loop2Packet reading)
    {
        _ = InvokeAsync(() =>
        {
            if (_disposed)
            {
                return;
            }

            _reading = reading;
            UpsertLiveHistorySample(reading);
            RefreshVisuals();
            StateHasChanged();
        });
    }

    private void OnStateChanged()
    {
        _ = InvokeAsync(() =>
        {
            if (!_disposed)
            {
                StateHasChanged();
            }
        });
    }

    public void Dispose()
    {
        _disposed = true;
        Worker.ReadingUpdated -= OnReadingUpdated;
        Worker.WorkerStateChanged -= OnStateChanged;
        Forwarder.SweptCompleted -= OnStateChanged;
    }

    private DateTime? ObservedAtUtc => Worker.LastReadingAt ?? _reading?.RecordedAtUtc;

    private DateTimeOffset? ObservedAtLocal => ObservedAtUtc.HasValue
        ? new DateTimeOffset(ObservedAtUtc.Value, TimeSpan.Zero).ToOffset(Station.ConsoleUtcOffset)
        : null;

    private DateTime? ObservationWindowEndLocal => ObservedAtLocal?.DateTime ?? _liveHistorySamples.LastOrDefault()?.TimeLocal;

    private string HeaderTimestamp => ToConsoleDateTime(ObservedAtUtc) ?? "Waiting for data";

    private string UpdatedClock => ToConsoleClock(ObservedAtUtc) ?? "--";

    private string ConditionSummary => _reading?.ForecastString ?? "Waiting for current console forecast.";

    private string ForecastText => _reading?.ForecastString ?? "No forecast available";

    private string StormStartText => _reading?.StormStartDate?.ToString("dd MMM yyyy", CultureInfo.InvariantCulture) ?? "--";

    private string? BarometricTrendText => DescribeTrend(_reading?.BarometricTrend);

    private double? FeelsLikeF => _reading?.ThswF ?? _reading?.HeatIndexF ?? _reading?.WindChillF ?? _reading?.OutsideTemperatureF;

    private string TemperatureUnitSuffix => DisplayUnitConverter.TemperatureSuffix(Station.TemperatureUnits);

    private string PressureUnitSuffix => DisplayUnitConverter.PressureSuffix(Station.BarometerUnits);

    private string RainUnitSuffix => DisplayUnitConverter.RainSuffix(Station.RainUnits);

    private string RainRateUnitSuffix => DisplayUnitConverter.RainRateSuffix(Station.RainUnits);

    private string WindUnitSuffix => DisplayUnitConverter.WindSuffix(Station.WindUnits);

    private string WindDirectionText => DisplayCompass(_reading?.WindDirectionDegrees);

    private string WindSummary => $"{DisplayWind(_reading?.WindSpeedMph)} {WindUnitSuffix}, gust {DisplayWind(_reading?.WindGust10MinMph)}";

    private string WindDirectionSpreadText => DisplayDirectionRange(_reading?.WindDirectionDegrees, _reading?.WindGust10MinDirectionDegrees);

    private string ArchiveIntervalText => $"{Math.Max(1, Station.ArchiveIntervalSeconds / 60)} min";

    private string WindDirectionRotation => (_reading?.WindDirectionDegrees ?? 0d).ToString("F0", CultureInfo.InvariantCulture);

    private string MoonPhaseText => _moonContext.PhaseName;

    private string MoonPhaseIcon => _moonContext.PhaseName switch
    {
        "New moon"       => "🌑",
        "Waxing crescent"=> "🌒",
        "First quarter"  => "🌓",
        "Waxing gibbous" => "🌔",
        "Full moon"      => "🌕",
        "Waning gibbous" => "🌖",
        "Last quarter"   => "🌗",
        "Waning crescent"=> "🌘",
        _                => "🌙"
    };

    private string IlluminationText => _moonContext.IlluminationText;

    private string MoonriseText => _moonContext.MoonriseText;

    private string MoonsetText => _moonContext.MoonsetText;

    private bool HasTemperaturePlot => _temperatureDatasets.Length > 0 && _chartTimeLabels.Length > 0;

    private bool HasSolarPlot => _solarDatasets.Length > 0 && _chartTimeLabels.Length > 0;

    private bool HasWindPlot => _windDatasets.Length > 0 && _chartTimeLabels.Length > 0;

    private bool HasAstronomicalPlot => _astronomicalDatasets.Length > 0 && _astronomicalLabels.Length > 0;

    private void RefreshVisuals()
    {
        RefreshSummaryMetrics();
        BuildChartData();
        RefreshCelestialModels();
    }

    private void BuildChartData()
    {
        if (!ObservationWindowEndLocal.HasValue)
        {
            _chartTimeLabels = [];
            _temperatureDatasets = [];
            _windDatasets = [];
            _solarDatasets = [];
            return;
        }

        DateTime windowEnd = ObservationWindowEndLocal.Value;
        DateTime windowStart = windowEnd.AddHours(-24);
        var bucketSize = TimeSpan.FromMinutes(30);

        // Combine persisted samples with the live reading at the head
        var allSamples = _liveHistorySamples
            .Where(s => s.TimeLocal >= windowStart && s.TimeLocal <= windowEnd)
            .ToList();

        if (_reading is not null)
        {
            var currentSample = CreateLiveHistorySample(windowEnd, _reading);
            if (allSamples.Count > 0 && Math.Abs((allSamples[^1].TimeLocal - windowEnd).TotalMinutes) < 1)
                allSamples[^1] = currentSample;
            else
                allSamples.Add(currentSample);
        }

        // Build per-series chart point lists then bucket into uniform grid
        var outsideTempBuckets = StatusChartBuckets.BuildBuckets(
            allSamples.Where(s => DisplayTemperatureValue(s.OutsideTemperatureF).HasValue)
                      .Select(s => new StatusChartPoint(s.TimeLocal, DisplayTemperatureValue(s.OutsideTemperatureF)!.Value)),
            windowStart, windowEnd, bucketSize);

        var insideTempBuckets = StatusChartBuckets.BuildBuckets(
            allSamples.Where(s => DisplayTemperatureValue(s.InsideTemperatureF).HasValue)
                      .Select(s => new StatusChartPoint(s.TimeLocal, DisplayTemperatureValue(s.InsideTemperatureF)!.Value)),
            windowStart, windowEnd, bucketSize);

        var windAvgBuckets = StatusChartBuckets.BuildBuckets(
            allSamples.Where(s => DisplayWindValue(s.WindSpeed2MinAvgMph).HasValue)
                      .Select(s => new StatusChartPoint(s.TimeLocal, DisplayWindValue(s.WindSpeed2MinAvgMph)!.Value)),
            windowStart, windowEnd, bucketSize);

        var windGustBuckets = StatusChartBuckets.BuildBuckets(
            allSamples.Where(s => DisplayWindValue(s.WindGust10MinMph).HasValue)
                      .Select(s => new StatusChartPoint(s.TimeLocal, DisplayWindValue(s.WindGust10MinMph)!.Value)),
            windowStart, windowEnd, bucketSize);

        var solarBuckets = StatusChartBuckets.BuildBuckets(
            allSamples.Where(s => s.SolarRadiationWm2.HasValue)
                      .Select(s => new StatusChartPoint(s.TimeLocal, s.SolarRadiationWm2!.Value)),
            windowStart, windowEnd, bucketSize);

        // Build time-axis labels from the bucket grid
        int slotCount = outsideTempBuckets.Count;
        var alignedStart = StatusChartBuckets.FloorToBucket(windowStart, bucketSize);
        _chartTimeLabels = Enumerable.Range(0, slotCount)
            .Select(i => alignedStart.Add(bucketSize.Multiply(i)).ToString("HH:mm", CultureInfo.InvariantCulture))
            .ToArray();

        _temperatureDatasets =
        [
            new HvoChartDataset($"Outside {TemperatureUnitSuffix}", outsideTempBuckets.Select(b => b?.Value).ToArray(),
                TemperatureOutsideColor, "rgba(105,211,255,0.15)", Fill: true, Tension: 0.35),
            new HvoChartDataset($"Inside {TemperatureUnitSuffix}", insideTempBuckets.Select(b => b?.Value).ToArray(),
                TemperatureInsideColor, "rgba(255,184,108,0.12)", Fill: true, Tension: 0.35),
        ];

        _windDatasets =
        [
            new HvoChartDataset($"2-min avg {WindUnitSuffix}", windAvgBuckets.Select(b => b?.Value).ToArray(),
                WindAverageColor, Tension: 0.3),
            new HvoChartDataset($"10-min gust {WindUnitSuffix}", windGustBuckets.Select(b => b?.Value).ToArray(),
                WindGustColor, Tension: 0.3),
        ];

        _solarDatasets =
        [
            new HvoChartDataset("Solar W/m²", solarBuckets.Select(b => b?.Value).ToArray(),
                SolarColor, "rgba(255,209,102,0.18)", Fill: true, Tension: 0.3),
        ];

        BuildAstronomicalChartData();
        _chartRevision++;
    }

    /// <summary>
    /// Builds a full-day (midnight-to-midnight) sun and moon altitude chart using a
    /// sinusoidal approximation from the console's sunrise/sunset and the computed
    /// moonrise/moonset times. Altitude is expressed as 0-100 % of the peak elevation.
    /// </summary>
    private void BuildAstronomicalChartData()
    {
        const int slots = 48;
        const int slotMinutes = 30;

        var labels = new string[slots];
        var sunData = new double?[slots];
        var moonData = new double?[slots];
        var sunNow = new double?[slots];
        var moonNow = new double?[slots];

        for (int i = 0; i < slots; i++)
        {
            labels[i] = new DateTime(2000, 1, 1)
                .AddMinutes(i * slotMinutes)
                .ToString("HH:mm", CultureInfo.InvariantCulture);
        }

        int currentSlot = ObservationWindowEndLocal.HasValue
            ? Math.Clamp((int)(ObservationWindowEndLocal.Value.TimeOfDay.TotalMinutes / slotMinutes), 0, slots - 1)
            : -1;

        // ── Sun ──────────────────────────────────────────────────────────────
        var sunrise = ParseConsoleTime(_reading?.SunriseDisplay);
        var sunset  = ParseConsoleTime(_reading?.SunsetDisplay);

        if (sunrise.HasValue && sunset.HasValue)
        {
            int riseMin = sunrise.Value.Hour * 60 + sunrise.Value.Minute;
            int setMin  = sunset.Value.Hour  * 60 + sunset.Value.Minute;
            int dayLen  = setMin - riseMin;

            if (dayLen > 0)
            {
                for (int i = 0; i < slots; i++)
                {
                    int t = i * slotMinutes;
                    if (t >= riseMin && t <= setMin)
                    {
                        double progress = (double)(t - riseMin) / dayLen;
                        sunData[i] = Math.Round(Math.Sin(progress * Math.PI) * 100.0, 1);
                    }
                }

                if (currentSlot >= 0 && sunData[currentSlot].HasValue)
                {
                    sunNow[currentSlot] = sunData[currentSlot];
                }
            }
        }

        // ── Moon ─────────────────────────────────────────────────────────────
        var moonrise = ParseMoonTime(_moonContext.MoonriseText);
        var moonset  = ParseMoonTime(_moonContext.MoonsetText);

        if (moonrise.HasValue && moonset.HasValue)
        {
            int riseMin  = moonrise.Value.Hour * 60 + moonrise.Value.Minute;
            int setMin   = moonset.Value.Hour  * 60 + moonset.Value.Minute;
            bool crosses = setMin < riseMin;
            int duration = crosses ? (24 * 60 - riseMin) + setMin : setMin - riseMin;

            if (duration > 0)
            {
                for (int i = 0; i < slots; i++)
                {
                    int t = i * slotMinutes;
                    bool above = crosses ? (t >= riseMin || t <= setMin) : (t >= riseMin && t <= setMin);
                    if (above)
                    {
                        int elapsed = crosses && t < riseMin ? (24 * 60 - riseMin) + t : t - riseMin;
                        double progress = (double)elapsed / duration;
                        // Scale moon slightly lower than sun so both fit (0-85%)
                        moonData[i] = Math.Round(Math.Sin(progress * Math.PI) * 85.0, 1);
                    }
                }

                if (currentSlot >= 0 && moonData[currentSlot].HasValue)
                {
                    moonNow[currentSlot] = moonData[currentSlot];
                }
            }
        }

        bool hasMoon = moonData.Any(v => v.HasValue);

        var datasets = new List<HvoChartDataset>
        {
            // Full sun arc (filled amber)
            new("", sunData,   "#ffcf66", "rgba(255,207,102,0.20)",
                Fill: true, BorderWidth: 2, PointRadius: 0, Tension: 0.4),
            // Current sun position — solid amber dot
            new("", sunNow,   "#ffcf66", "#ffcf66",
                Fill: false, BorderWidth: 2, PointRadius: 8, Tension: 0),
        };

        if (hasMoon)
        {
            datasets.Add(new("", moonData, "#9fb8d4", "rgba(159,184,212,0.12)",
                Fill: false, BorderWidth: 1.5, PointRadius: 0, Tension: 0.4));
            // Current moon position — solid silver dot
            datasets.Add(new("", moonNow, "#c8d8ee", "#c8d8ee",
                Fill: false, BorderWidth: 2, PointRadius: 7, Tension: 0));
        }

        _astronomicalLabels  = labels;
        _astronomicalDatasets = datasets.ToArray();
    }

    private static TimeOnly? ParseConsoleTime(string? text) =>
        text is not null &&
        TimeOnly.TryParseExact(text, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)
            ? t : null;

    private static TimeOnly? ParseMoonTime(string? text) =>
        text is not null &&
        TimeOnly.TryParseExact(text, "h:mm tt", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)
            ? t : null;

    private void RefreshSummaryMetrics()
    {
        _outsidePressure24HourLowInHg = null;
        _outsidePressure24HourHighInHg = null;
        _insidePressure24HourLowInHg = null;
        _insidePressure24HourHighInHg = null;
        _outsideTemperature24HourLowF = null;
        _outsideTemperature24HourHighF = null;
        _todayPeakSolarWm2 = null;

        if (!ObservationWindowEndLocal.HasValue)
        {
            return;
        }

        DateTime windowEnd = ObservationWindowEndLocal.Value;
        DateTime windowStart = windowEnd.AddHours(-24);
        DateTime consoleToday = windowEnd.Date;

        var summarySamples = _liveHistorySamples
            .Where(sample => sample.TimeLocal >= windowStart && sample.TimeLocal <= windowEnd)
            .ToList();

        if (_reading is not null)
        {
            var currentSample = CreateLiveHistorySample(windowEnd, _reading);

            if (summarySamples.Count > 0 && Math.Abs((summarySamples[^1].TimeLocal - currentSample.TimeLocal).TotalMinutes) < 1)
            {
                summarySamples[^1] = currentSample;
            }
            else
            {
                summarySamples.Add(currentSample);
            }
        }

        _outsideTemperature24HourLowF = summarySamples
            .Where(sample => sample.OutsideTemperatureF.HasValue)
            .Select(sample => sample.OutsideTemperatureF!.Value)
            .DefaultIfEmpty()
            .Min();

        if (_outsideTemperature24HourLowF == 0d && summarySamples.All(sample => !sample.OutsideTemperatureF.HasValue))
        {
            _outsideTemperature24HourLowF = null;
        }

        _outsideTemperature24HourHighF = summarySamples
            .Where(sample => sample.OutsideTemperatureF.HasValue)
            .Select(sample => sample.OutsideTemperatureF!.Value)
            .DefaultIfEmpty()
            .Max();

        if (_outsideTemperature24HourHighF == 0d && summarySamples.All(sample => !sample.OutsideTemperatureF.HasValue))
        {
            _outsideTemperature24HourHighF = null;
        }

        _outsidePressure24HourLowInHg = GetMinimum(summarySamples.Select(sample => sample.OutsidePressureInHg));
        _outsidePressure24HourHighInHg = GetMaximum(summarySamples.Select(sample => sample.OutsidePressureInHg));
        _insidePressure24HourLowInHg = GetMinimum(summarySamples.Select(sample => sample.InsidePressureInHg));
        _insidePressure24HourHighInHg = GetMaximum(summarySamples.Select(sample => sample.InsidePressureInHg));

        _todayPeakSolarWm2 = summarySamples
            .Where(sample => sample.TimeLocal.Date == consoleToday)
            .Select(sample => sample.SolarRadiationWm2)
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .DefaultIfEmpty()
            .Max();

        if (_todayPeakSolarWm2 <= 0)
        {
            _todayPeakSolarWm2 = null;
        }
    }

    private static double? GetMinimum(IEnumerable<double?> source)
    {
        var values = source.Where(value => value.HasValue).Select(value => value!.Value).ToArray();
        return values.Length > 0 ? values.Min() : null;
    }

    private static double? GetMaximum(IEnumerable<double?> source)
    {
        var values = source.Where(value => value.HasValue).Select(value => value!.Value).ToArray();
        return values.Length > 0 ? values.Max() : null;
    }

    private void RefreshCelestialModels()
    {
        if (!ObservationWindowEndLocal.HasValue)
        {
            _moonContext = MoonSnapshot.Empty;
            return;
        }

        var observedAtLocal = new DateTimeOffset(ObservationWindowEndLocal.Value, Station.ConsoleUtcOffset);

        _moonContext = CelestialArcCalculations.BuildMoonSnapshot(
            observedAtLocal,
            Station.ConsoleUtcOffset,
            Station.LatitudeDegrees,
            Station.LongitudeDegrees);
    }

    private void UpsertLiveHistorySample(Loop2Packet reading)
    {
        DateTime observedAtUtc = Worker.LastReadingAt ?? reading.RecordedAtUtc;
        DateTime localTime = new DateTimeOffset(observedAtUtc, TimeSpan.Zero)
            .ToOffset(Station.ConsoleUtcOffset)
            .DateTime;

        var sample = CreateLiveHistorySample(localTime, reading);

        if (_liveHistorySamples.Count > 0 && Math.Abs((_liveHistorySamples[^1].TimeLocal - localTime).TotalMinutes) < 1)
        {
            _liveHistorySamples[^1] = sample;
        }
        else
        {
            _liveHistorySamples.Add(sample);
        }

        TrimLiveHistorySamples(localTime);
    }

    private void TrimLiveHistorySamples(DateTime? windowEndLocal = null)
    {
        DateTime end = windowEndLocal
            ?? ObservationWindowEndLocal
            ?? (_liveHistorySamples.Count > 0 ? _liveHistorySamples[^1].TimeLocal : DateTime.MinValue);

        if (end == DateTime.MinValue)
        {
            return;
        }

        DateTime windowStart = end.AddHours(-24);
        _liveHistorySamples = _liveHistorySamples
            .Where(sample => sample.TimeLocal >= windowStart && sample.TimeLocal <= end)
            .OrderBy(sample => sample.TimeLocal)
            .ToList();
    }

    private static LiveHistorySample CreateLiveHistorySample(DateTime localTime, PersistedLiveReading snapshot)
    {
        return new LiveHistorySample(
            localTime,
            snapshot.TemperatureF,
            snapshot.InsideTemperatureF,
            snapshot.SolarRadiationWm2,
            snapshot.BarometricPressureInHg,
            snapshot.PressureRawInHg,
            snapshot.WindSpeed2MinAvgMph,
            snapshot.WindGust10MinMph);
    }

    private static LiveHistorySample CreateLiveHistorySample(DateTime localTime, Loop2Packet reading)
    {
        return new LiveHistorySample(
            localTime,
            reading.OutsideTemperatureF,
            reading.InsideTemperatureF,
            reading.SolarRadiationWm2,
            reading.BarometricPressureInHg,
            reading.PressureRawInHg,
            reading.WindSpeed2MinAvgMph,
            reading.WindGust10MinMph);
    }

    private string PressureMarkerLeft(double? pressure)
    {
        double? displayPressure = DisplayPressureValue(pressure);
        if (displayPressure is null)
        {
            return "50%";
        }

        double minimum = DisplayPressureValue(26.5) ?? 26.5;
        double maximum = DisplayPressureValue(30.5) ?? 30.5;
        double clamped = Math.Clamp(displayPressure.Value, minimum, maximum);
        double percent = ((clamped - minimum) / (maximum - minimum)) * 100.0;
        return $"{percent.ToString("F0", CultureInfo.InvariantCulture)}%";
    }

    private string? ToConsoleTime(DateTime? utc) =>
        utc.HasValue
            ? new DateTimeOffset(utc.Value, TimeSpan.Zero)
                  .ToOffset(Station.ConsoleUtcOffset)
                  .ToString("HH:mm:ss")
            : null;

    private string? ToConsoleDateTime(DateTime? utc) =>
        utc.HasValue
            ? new DateTimeOffset(utc.Value, TimeSpan.Zero)
                  .ToOffset(Station.ConsoleUtcOffset)
                  .ToString("dd MMM yyyy - h:mm tt", CultureInfo.InvariantCulture)
            : null;

    private string? ToConsoleClock(DateTime? utc) =>
        utc.HasValue
            ? new DateTimeOffset(utc.Value, TimeSpan.Zero)
                  .ToOffset(Station.ConsoleUtcOffset)
                  .ToString("h:mm:ss tt", CultureInfo.InvariantCulture)
            : null;

    private double? DisplayTemperatureValue(double? fahrenheit) =>
        DisplayUnitConverter.Temperature(fahrenheit, Station.TemperatureUnits);

    private double? DisplayPressureValue(double? inHg) =>
        DisplayUnitConverter.Pressure(inHg, Station.BarometerUnits);

    private double? DisplayRainValue(double? inches) =>
        DisplayUnitConverter.Rain(inches, Station.RainUnits);

    private double? DisplayWindValue(double? mph) =>
        DisplayUnitConverter.WindSpeed(mph, Station.WindUnits);

    private string DisplayTemperature(double? value) => DisplayTemperatureValue(value)?.ToString("F1", CultureInfo.InvariantCulture) ?? "--";

    private static string DisplayWhole(double? value) => value?.ToString("F0", CultureInfo.InvariantCulture) ?? "--";

    private string DisplayPressure(double? value) => DisplayPressureValue(value)?.ToString(PressureUnitSuffix is "inHg" ? "F2" : "F1", CultureInfo.InvariantCulture) ?? "--";

    private string DisplayRain(double? value) => DisplayRainValue(value)?.ToString(RainUnitSuffix is "in" ? "F2" : "F1", CultureInfo.InvariantCulture) ?? "--";

    private string DisplayEt(double? value) => DisplayRainValue(value)?.ToString(RainUnitSuffix is "in" ? "F3" : "F2", CultureInfo.InvariantCulture) ?? "--";

    private string DisplayWind(double? value) => DisplayWindValue(value)?.ToString(WindUnitSuffix is "mph" or "km/h" or "knots" ? "F0" : "F1", CultureInfo.InvariantCulture) ?? "--";

    private static string DisplayUv(double? value) => value?.ToString("F1", CultureInfo.InvariantCulture) ?? "--";

    private static string DisplaySolar(double? value) => value is null ? "--" : $"{value:F0} W/m^2";

    private static string DisplayBatteryVoltage(double? value) => value is null ? "--" : $"{value:F2} V";

    private static string? DescribeTrend(int? value) => value switch
    {
        null => null,
        <= -60 => "Falling fast",
        <= -20 => "Falling",
        -2 or -3 => "Falling fast",
        -1 => "Falling",
        0 => "Steady",
        1 => "Rising",
        2 or 3 => "Rising fast",
        >= 60 => "Rising fast",
        >= 20 => "Rising",
        _ => null
    };

    private static string DisplayTrend(string? value) => string.IsNullOrWhiteSpace(value) ? "--" : value;

    private static string FormatDisplayTime(string? time)
    {
        if (string.IsNullOrWhiteSpace(time) ||
            !TimeOnly.TryParseExact(time, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            return "--";
        }

        return parsed.ToString("h:mm tt", CultureInfo.InvariantCulture);
    }

    private string TransmitterStatus
    {
        get
        {
            var reading = _reading;
            if (reading?.TransmitterBatteryStatus is null)
            {
                return "Unknown";
            }

            return reading.TransmitterLowBatteryChannels.Count == 0
                ? "All OK"
                : $"Low: ch {string.Join(", ", reading.TransmitterLowBatteryChannels)}";
        }
    }

    private static string DisplayCompass(double? degrees)
    {
        if (degrees is null)
        {
            return "--";
        }

        string[] points = ["N", "NNE", "NE", "ENE", "E", "ESE", "SE", "SSE", "S", "SSW", "SW", "WSW", "W", "WNW", "NW", "NNW"];
        int index = (int)Math.Round(degrees.Value / 22.5, MidpointRounding.AwayFromZero) % points.Length;
        return $"{points[index]} {degrees.Value:F0}°";
    }

    private static string DisplayDirectionRange(double? first, double? second)
    {
        if (first is null && second is null)
        {
            return "--";
        }

        if (first is null || second is null)
        {
            return $"{(first ?? second):F0}°";
        }

        var low = Math.Min(first.Value, second.Value);
        var high = Math.Max(first.Value, second.Value);
        return $"{low:F0}°-{high:F0}°";
    }

    private static int? EncodeDisplayTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            !TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            return null;
        }

        return (parsed.Hour * 100) + parsed.Minute;
    }

    private sealed record LiveHistorySample(
        DateTime TimeLocal,
        double? OutsideTemperatureF,
        double? InsideTemperatureF,
        double? SolarRadiationWm2,
        double? OutsidePressureInHg,
        double? InsidePressureInHg,
        double? WindSpeed2MinAvgMph,
        double? WindGust10MinMph);

    private sealed class PersistedLiveReading
    {
        public DateTime? RecordedAt { get; init; }
        public double? TemperatureF { get; init; }
        public double? InsideTemperatureF { get; init; }
        public double? DewPointF { get; init; }
        public double? HeatIndexF { get; init; }
        public double? WindChillF { get; init; }
        public double? ThswF { get; init; }
        public double? HumidityPercent { get; init; }
        public double? InsideHumidityPercent { get; init; }
        public double? BarometricPressureInHg { get; init; }
        public double? PressureRawInHg { get; init; }
        public double? AltimeterInHg { get; init; }
        public int? BarometricTrend { get; init; }
        public double? WindSpeedMph { get; init; }
        public int? WindDirectionDegrees { get; init; }
        public double? WindSpeed10MinAvgMph { get; init; }
        public double? WindSpeed2MinAvgMph { get; init; }
        public double? WindGust10MinMph { get; init; }
        public int? WindGust10MinDirectionDegrees { get; init; }
        public double? RainRateInchesPerHour { get; init; }
        public double? DailyRainInches { get; init; }
        public double? Rain15MinInches { get; init; }
        public double? HourRainInches { get; init; }
        public double? Rain24HourInches { get; init; }
        public double? StormRainInches { get; init; }
        public DateTime? StormStartDate { get; init; }
        public double? MonthlyRainInches { get; init; }
        public double? YearlyRainInches { get; init; }
        public double? SolarRadiationWm2 { get; init; }
        public double? UvIndex { get; init; }
        public double? DailyEtInches { get; init; }
        public double? MonthlyEtInches { get; init; }
        public double? YearlyEtInches { get; init; }
        public double? ConsoleBatteryVoltage { get; init; }
        public ushort? TransmitterBatteryStatus { get; init; }
        public int? ForecastRule { get; init; }
        public string? SunriseTime { get; init; }
        public string? SunsetTime { get; init; }

        public Loop2Packet ToLoop2Packet(DateTime recordedAtUtc) => new()
        {
            RecordedAtUtc = RecordedAt ?? recordedAtUtc,
            OutsideTemperatureF = TemperatureF,
            InsideTemperatureF = InsideTemperatureF,
            DewPointF = DewPointF,
            HeatIndexF = HeatIndexF,
            WindChillF = WindChillF,
            ThswF = ThswF,
            OutsideHumidityPercent = HumidityPercent,
            InsideHumidityPercent = InsideHumidityPercent,
            BarometricPressureInHg = BarometricPressureInHg,
            PressureRawInHg = PressureRawInHg,
            AltimeterInHg = AltimeterInHg,
            BarometricTrend = BarometricTrend,
            WindSpeedMph = WindSpeedMph,
            WindDirectionDegrees = WindDirectionDegrees,
            WindSpeed10MinAvgMph = WindSpeed10MinAvgMph,
            WindSpeed2MinAvgMph = WindSpeed2MinAvgMph,
            WindGust10MinMph = WindGust10MinMph,
            WindGust10MinDirectionDegrees = WindGust10MinDirectionDegrees,
            RainRateInchesPerHour = RainRateInchesPerHour,
            DailyRainInches = DailyRainInches,
            Rain15MinInches = Rain15MinInches,
            HourRainInches = HourRainInches,
            Rain24HourInches = Rain24HourInches,
            StormRainInches = StormRainInches,
            StormStartDate = StormStartDate,
            MonthlyRainInches = MonthlyRainInches,
            YearlyRainInches = YearlyRainInches,
            SolarRadiationWm2 = SolarRadiationWm2,
            UvIndex = UvIndex,
            DailyEtInches = DailyEtInches,
            MonthlyEtInches = MonthlyEtInches,
            YearlyEtInches = YearlyEtInches,
            ConsoleBatteryVoltage = ConsoleBatteryVoltage,
            TransmitterBatteryStatus = TransmitterBatteryStatus,
            ForecastRule = ForecastRule,
            SunriseTime = EncodeDisplayTime(SunriseTime),
            SunsetTime = EncodeDisplayTime(SunsetTime)
        };
    }

    private static string? F(double? v) => v?.ToString("F1");
    private static string? F0(double? v) => v?.ToString("F0");
}
