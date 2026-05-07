using HVO.Hardware.DavisVantagePro2.Outbox;
using HVO.Hardware.DavisVantagePro2.Protocol.Packets;
using HVO.Astronomy;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Text.Json;

namespace HVO.Hardware.DavisVantagePro2.Components.Pages;

public partial class Status : IDisposable
{
    [Inject] private ILogger<Status> Logger { get; set; } = default!;
    [Inject] private IServiceScopeFactory ScopeFactory { get; set; } = default!;
    [CascadingParameter] private HVO.Hardware.DavisVantagePro2.Components.Layout.MainLayout? MainLayout { get; set; }

    private Loop2Packet? _reading;
    private double? _outsidePressure24HourLowInHg;
    private double? _outsidePressure24HourHighInHg;
    private double? _insidePressure24HourLowInHg;
    private double? _insidePressure24HourHighInHg;
    private double? _outsideTemperature24HourLowF;
    private double? _outsideTemperature24HourHighF;
    private double? _todayPeakSolarWm2;
    private List<LiveHistorySample> _liveHistorySamples = [];
    private PlotModel _temperaturePlot = PlotModel.Empty;
    private PlotModel _solarPlot = PlotModel.Empty;
    private CelestialMarker _sunMarker = CelestialMarker.Hidden;
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

    private void ToggleNavigation() => MainLayout?.ToggleDrawer();

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

                    return new LiveHistorySample(
                        localTime,
                        snapshot.TemperatureF,
                        snapshot.SolarRadiationWm2,
                        snapshot.BarometricPressureInHg,
                        snapshot.PressureRawInHg);
                })
                .Where(sample => sample is not null)
                .Select(sample => sample!)
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
        _reading = reading;
        UpsertLiveHistorySample(reading);
        RefreshVisuals();
        InvokeAsync(StateHasChanged);
    }

    private void OnStateChanged() => InvokeAsync(StateHasChanged);

    public void Dispose()
    {
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

    private string LiveStatusText => Station.IsConnected ? "Live loop active" : "Station disconnected";

    private string ConnectionState => Station.IsConnected ? "Online" : "Offline";

    private string ConditionSummary => _reading?.ForecastString ?? "Waiting for current console forecast.";

    private string ForecastText => _reading?.ForecastString ?? "No forecast available";

    private string StormStartText => _reading?.StormStartDate?.ToString("dd MMM yyyy", CultureInfo.InvariantCulture) ?? "--";

    private string? BarometricTrendText => DescribeTrend(_reading?.BarometricTrend);

    private double? FeelsLikeF => _reading?.HeatIndexF ?? _reading?.OutsideTemperatureF;

    private string WindDirectionText => DisplayCompass(_reading?.WindDirectionDegrees);

    private string WindSummary => $"{DisplayWhole(_reading?.WindSpeedMph)} mph, gust {DisplayWhole(_reading?.WindGust10MinMph)}";

    private string WindDirectionSpreadText => DisplayDirectionRange(_reading?.WindDirectionDegrees, _reading?.WindGust10MinDirectionDegrees);

    private string ArchiveIntervalText => $"{Math.Max(1, Station.ArchiveIntervalSeconds / 60)} min";

    private string WindDirectionRotation => (_reading?.WindDirectionDegrees ?? 0d).ToString("F0", CultureInfo.InvariantCulture);

    private string TemperatureLinePath => _temperaturePlot.LinePath;

    private string TemperatureAreaPath => _temperaturePlot.AreaPath;

    private IReadOnlyList<PlotLabel> TemperatureAxisLabels => _temperaturePlot.AxisLabels;

    private bool HasTemperaturePlot => _temperaturePlot.HasData;

    private double TemperatureCurrentX => _temperaturePlot.CurrentPoint?.X ?? 0d;

    private double TemperatureCurrentY => _temperaturePlot.CurrentPoint?.Y ?? 0d;

    private string SolarLinePath => _solarPlot.LinePath;

    private string SolarAreaPath => _solarPlot.AreaPath;

    private IReadOnlyList<PlotLabel> SolarAxisLabels => _solarPlot.AxisLabels;

    private bool HasSolarPlot => _solarPlot.HasData;

    private double SolarCurrentX => _solarPlot.CurrentPoint?.X ?? 0d;

    private double SolarCurrentY => _solarPlot.CurrentPoint?.Y ?? 0d;

    private string MoonPhaseText => _moonContext.PhaseName;

    private double MoonPhaseShadowX
    {
        get
        {
            const double centerX = 60d;
            const double radius = 42d;
            double offset = Math.Clamp(_moonContext.IlluminationPercent / 100d, 0d, 1d) * radius * 2d;
            return centerX + (_moonContext.IsWaxing ? -offset : offset);
        }
    }

    private string IlluminationText => _moonContext.IlluminationText;

    private string MoonriseText => _moonContext.MoonriseText;

    private string MoonsetText => _moonContext.MoonsetText;

    private CelestialMarker SunMarker => _sunMarker;

    private CelestialMarker MoonMarker => _moonContext.Marker;

    private void RefreshVisuals()
    {
        RefreshSummaryMetrics();
        _temperaturePlot = BuildPlotModel(BuildChartSamples(record => record.OutsideTemperatureF, _reading?.OutsideTemperatureF), clampMinimumToZero: false, minimumRange: 8d);
        _solarPlot = BuildPlotModel(BuildChartSamples(record => record.SolarRadiationWm2, _reading?.SolarRadiationWm2), clampMinimumToZero: true, minimumRange: 300d);
        RefreshCelestialModels();
    }

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
            var currentSample = new LiveHistorySample(
                windowEnd,
                _reading.OutsideTemperatureF,
                _reading.SolarRadiationWm2,
                _reading.BarometricPressureInHg,
                _reading.PressureRawInHg);

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
            _sunMarker = CelestialMarker.Hidden;
            _moonContext = MoonSnapshot.Empty;
            return;
        }

        var observedAtLocal = new DateTimeOffset(ObservationWindowEndLocal.Value, Station.ConsoleUtcOffset);

        _sunMarker = CelestialArcCalculations.BuildSunMarker(observedAtLocal, _reading?.SunriseDisplay, _reading?.SunsetDisplay);
        _moonContext = CelestialArcCalculations.BuildMoonSnapshot(
            observedAtLocal,
            Station.ConsoleUtcOffset,
            Station.LatitudeDegrees,
            Station.LongitudeDegrees);
    }

    private IReadOnlyList<ChartPoint> BuildChartSamples(Func<LiveHistorySample, double?> selector, double? liveValue)
    {
        if (!ObservationWindowEndLocal.HasValue)
        {
            return [];
        }

        DateTime windowEnd = ObservationWindowEndLocal.Value;
        DateTime windowStart = windowEnd.AddHours(-24);

        var points = _liveHistorySamples
            .Where(record => record.TimeLocal >= windowStart && record.TimeLocal <= windowEnd)
            .Select(record => new { record.TimeLocal, Value = selector(record) })
            .Where(point => point.Value.HasValue)
            .Select(point => new ChartPoint(point.TimeLocal, point.Value!.Value))
            .ToList();

        if (liveValue.HasValue)
        {
            var livePoint = new ChartPoint(windowEnd, liveValue.Value);
            if (points.Count > 0 && Math.Abs((points[^1].TimeLocal - livePoint.TimeLocal).TotalMinutes) < 1)
            {
                points[^1] = livePoint;
            }
            else
            {
                points.Add(livePoint);
            }
        }

        return points;
    }

    private void UpsertLiveHistorySample(Loop2Packet reading)
    {
        DateTime observedAtUtc = Worker.LastReadingAt ?? reading.RecordedAtUtc;
        DateTime localTime = new DateTimeOffset(observedAtUtc, TimeSpan.Zero)
            .ToOffset(Station.ConsoleUtcOffset)
            .DateTime;

        var sample = new LiveHistorySample(
            localTime,
            reading.OutsideTemperatureF,
            reading.SolarRadiationWm2,
            reading.BarometricPressureInHg,
            reading.PressureRawInHg);

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

    private static PlotModel BuildPlotModel(IReadOnlyList<ChartPoint> source, bool clampMinimumToZero, double minimumRange)
    {
        if (source.Count == 0)
        {
            return PlotModel.Empty;
        }

        const double left = 32d;
        const double right = 516d;
        const double top = 24d;
        const double bottom = 240d;
        double plotWidth = right - left;
        double plotHeight = bottom - top;

        DateTime end = source[^1].TimeLocal;
        DateTime start = end.AddHours(-24);
        double minValue = source.Min(point => point.Value);
        double maxValue = source.Max(point => point.Value);

        if (clampMinimumToZero)
        {
            minValue = Math.Min(0d, minValue);
        }

        if (maxValue - minValue < minimumRange)
        {
            double padding = (minimumRange - (maxValue - minValue)) / 2d;
            minValue -= padding;
            maxValue += padding;
        }

        if (clampMinimumToZero)
        {
            minValue = Math.Max(0d, minValue);
        }

        if (Math.Abs(maxValue - minValue) < 0.001d)
        {
            maxValue = minValue + minimumRange;
        }

        var pointSegments = SplitContinuousSegments(source);
        var coordinateSegments = pointSegments
            .Select(segment => segment
                .Select(point =>
                {
                    double x = left + (Math.Clamp((point.TimeLocal - start).TotalHours / 24d, 0d, 1d) * plotWidth);
                    double y = bottom - (((point.Value - minValue) / (maxValue - minValue)) * plotHeight);
                    return new PlotPoint(x, y);
                })
                .ToList())
            .Where(segment => segment.Count > 0)
            .ToList();

        string linePath = string.Join(' ', coordinateSegments.Select(BuildLinePath));
        string areaPath = string.Join(' ', coordinateSegments.Where(segment => segment.Count > 1).Select(segment => BuildAreaPath(segment, bottom)));

        var axisLabels = Enumerable.Range(0, 5)
            .Select(index =>
            {
                double ratio = index / 4d;
                double value = maxValue - ((maxValue - minValue) * ratio);
                double y = top + (plotHeight * ratio);
                return new PlotLabel(y, FormatAxisValue(value));
            })
            .ToArray();

        return new PlotModel(linePath, areaPath, axisLabels, coordinateSegments[^1][^1]);
    }

    private static IReadOnlyList<List<ChartPoint>> SplitContinuousSegments(IReadOnlyList<ChartPoint> source)
    {
        if (source.Count == 0)
        {
            return [];
        }

        double gapThresholdMinutes = DetermineGapThresholdMinutes(source);
        var segments = new List<List<ChartPoint>>();
        var currentSegment = new List<ChartPoint> { source[0] };

        for (int index = 1; index < source.Count; index++)
        {
            ChartPoint point = source[index];
            double gapMinutes = (point.TimeLocal - source[index - 1].TimeLocal).TotalMinutes;

            if (gapMinutes > gapThresholdMinutes)
            {
                segments.Add(currentSegment);
                currentSegment = [];
            }

            currentSegment.Add(point);
        }

        segments.Add(currentSegment);
        return segments;
    }

    private static double DetermineGapThresholdMinutes(IReadOnlyList<ChartPoint> source)
    {
        var intervals = source
            .Zip(source.Skip(1), (previous, current) => (current.TimeLocal - previous.TimeLocal).TotalMinutes)
            .Where(interval => interval > 0d)
            .OrderBy(interval => interval)
            .ToList();

        if (intervals.Count == 0)
        {
            return 15d;
        }

        double median = intervals[intervals.Count / 2];
        return Math.Clamp(median * 3d, 15d, 45d);
    }

    private static string BuildLinePath(IReadOnlyList<PlotPoint> segment) =>
        string.Join(' ', segment.Select((point, index) =>
            $"{(index == 0 ? 'M' : 'L')}{point.X.ToString("F1", CultureInfo.InvariantCulture)} {point.Y.ToString("F1", CultureInfo.InvariantCulture)}"));

    private static string BuildAreaPath(IReadOnlyList<PlotPoint> segment, double bottom) =>
        string.Concat(
            BuildLinePath(segment),
            $" L{segment[^1].X.ToString("F1", CultureInfo.InvariantCulture)} {bottom.ToString("F1", CultureInfo.InvariantCulture)}",
            $" L{segment[0].X.ToString("F1", CultureInfo.InvariantCulture)} {bottom.ToString("F1", CultureInfo.InvariantCulture)} Z");

    private static string FormatAxisValue(double value)
    {
        double rounded = Math.Abs(value) >= 10d ? Math.Round(value) : Math.Round(value, 1);
        return rounded.ToString(Math.Abs(rounded % 1d) < 0.001d ? "F0" : "F1", CultureInfo.InvariantCulture);
    }

    private static string PressureMarkerLeft(double? pressure)
    {
        if (pressure is null)
        {
            return "50%";
        }

        const double minimum = 28.5;
        const double maximum = 30.5;
        double clamped = Math.Clamp(pressure.Value, minimum, maximum);
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
                  .ToString("dd MMM yyyy · h:mm tt", CultureInfo.InvariantCulture)
            : null;

    private string? ToConsoleClock(DateTime? utc) =>
        utc.HasValue
            ? new DateTimeOffset(utc.Value, TimeSpan.Zero)
                  .ToOffset(Station.ConsoleUtcOffset)
                  .ToString("h:mm:ss tt", CultureInfo.InvariantCulture)
            : null;

    private static string DisplayTemperature(double? value) => value?.ToString("F1", CultureInfo.InvariantCulture) ?? "--";

    private static string DisplayWhole(double? value) => value?.ToString("F0", CultureInfo.InvariantCulture) ?? "--";

    private static string DisplayPressure(double? value) => value?.ToString("F2", CultureInfo.InvariantCulture) ?? "--";

    private static string DisplayRain(double? value) => value?.ToString("F2", CultureInfo.InvariantCulture) ?? "--";

    private static string DisplayEt(double? value) => value?.ToString("F3", CultureInfo.InvariantCulture) ?? "--";

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

    private sealed record ChartPoint(DateTime TimeLocal, double Value);

    private sealed record LiveHistorySample(
        DateTime TimeLocal,
        double? OutsideTemperatureF,
        double? SolarRadiationWm2,
        double? OutsidePressureInHg,
        double? InsidePressureInHg);

    private sealed record PlotModel(string LinePath, string AreaPath, IReadOnlyList<PlotLabel> AxisLabels, PlotPoint? CurrentPoint)
    {
        public static PlotModel Empty { get; } = new(string.Empty, string.Empty, [], null);
        public bool HasData => CurrentPoint is not null;
    }

    private sealed record PlotLabel(double Y, string Text);

    private sealed record PlotPoint(double X, double Y);

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
