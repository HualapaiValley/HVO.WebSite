using HVO.Hardware.DavisVantagePro2.Outbox;
using HVO.Hardware.DavisVantagePro2.Protocol.Packets;
using HVO.Hardware.DavisVantagePro2.Station;
using HVO.Hardware.DavisVantagePro2.Components.Layout;
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
    [CascadingParameter] private HVO.Hardware.DavisVantagePro2.Components.Layout.ShellLayoutState? ShellLayoutState { get; set; }

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
    private PlotModel _temperaturePlot = PlotModel.Empty;
    private PlotModel _solarPlot = PlotModel.Empty;
    private PlotModel _windPlot = PlotModel.Empty;
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

    protected override void OnParametersSet()
    {
        ShellLayoutState?.SetPage("Overview", PageHeadingText, PageSummaryText);
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

    private void UpdateShellFooter()
    {
        ShellLayoutState?.SetFooter(
            BuildLiveFooterItem(),
            new ShellFooterItem(StationLocationText),
            new ShellFooterItem(HeaderTimestamp),
            new ShellFooterItem($"Outbox: {Forwarder.PendingCount} pending - {Forwarder.FailedCount} failed"),
            BuildApiFooterItem());
    }

    private ShellFooterItem BuildLiveFooterItem()
    {
        DateTime? observedAtUtc = ObservedAtUtc;

        if (observedAtUtc.HasValue
            && DateTime.UtcNow - observedAtUtc.Value <= LiveLoopFreshnessThreshold
            && Worker.ConsecutiveErrors == 0)
        {
            return new ShellFooterItem("Live loop active", ShellFooterIndicator.Online);
        }

        if (Worker.ConsecutiveErrors > 0 || !Station.IsConnected)
        {
            return new ShellFooterItem(
                observedAtUtc.HasValue ? "Live loop stalled" : "Station disconnected",
                ShellFooterIndicator.Offline);
        }

        if (observedAtUtc.HasValue)
        {
            return new ShellFooterItem("Live loop stale", ShellFooterIndicator.Warning);
        }

        return new ShellFooterItem("Waiting for live packets", ShellFooterIndicator.Warning);
    }

    private ShellFooterItem BuildApiFooterItem()
    {
        if (Forwarder.PendingCount > 0 && !string.IsNullOrWhiteSpace(Forwarder.LastError))
        {
            return new ShellFooterItem("API sync failing", ShellFooterIndicator.Offline);
        }

        if (Forwarder.PendingCount > 0)
        {
            return new ShellFooterItem("API sync pending", ShellFooterIndicator.Warning);
        }

        if (Forwarder.FailedCount > 0)
        {
            return new ShellFooterItem("API sync degraded", ShellFooterIndicator.Warning);
        }

        if (Forwarder.LastSentAt.HasValue)
        {
            return new ShellFooterItem("API sync healthy", ShellFooterIndicator.Online);
        }

        return new ShellFooterItem("API sync idle", ShellFooterIndicator.Warning);
    }

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

    private string CelestialViewBox => BuildCelestialViewBox(SunMarker, MoonMarker);

    private bool HasTemperaturePlot => _temperaturePlot.HasData;

    private bool HasSolarPlot => _solarPlot.HasData;

    private bool HasWindPlot => _windPlot.HasData;

    private void RefreshVisuals()
    {
        RefreshSummaryMetrics();
        _temperaturePlot = BuildPlotModel(
            clampMinimumToZero: false,
            minimumRange: 8d,
            new PlotSeriesSpec(sample => DisplayTemperatureValue(sample.OutsideTemperatureF), DisplayTemperatureValue(_reading?.OutsideTemperatureF), TemperatureOutsideColor),
            new PlotSeriesSpec(sample => DisplayTemperatureValue(sample.InsideTemperatureF), DisplayTemperatureValue(_reading?.InsideTemperatureF), TemperatureInsideColor));

        _solarPlot = BuildPlotModel(
            clampMinimumToZero: true,
            minimumRange: 300d,
            new PlotSeriesSpec(sample => sample.SolarRadiationWm2, _reading?.SolarRadiationWm2, SolarColor, "rgba(255, 209, 102, 0.18)"));

        _windPlot = BuildPlotModel(
            clampMinimumToZero: true,
            minimumRange: 10d,
            new PlotSeriesSpec(sample => DisplayWindValue(sample.WindSpeed2MinAvgMph), DisplayWindValue(_reading?.WindSpeed2MinAvgMph), WindAverageColor),
            new PlotSeriesSpec(sample => DisplayWindValue(sample.WindGust10MinMph), DisplayWindValue(_reading?.WindGust10MinMph), WindGustColor));

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

    private static string BuildCelestialViewBox(CelestialMarker sunMarker, CelestialMarker moonMarker)
    {
        const double arcMinX = 22d;
        const double arcMaxX = 198d;
        const double arcMinY = 76d;
        const double arcMaxY = 126d;
        const double padding = 4d;
        const double minimumWidth = 182d;
        const double minimumHeight = 72d;

        double minX = arcMinX;
        double maxX = arcMaxX;
        double minY = arcMinY;
        double maxY = arcMaxY;

        if (sunMarker.IsVisible)
        {
            double radius = Math.Max(18d, sunMarker.GlowRadius);
            ExpandBounds(ref minX, ref maxX, ref minY, ref maxY, sunMarker.X, sunMarker.Y, radius);
        }

        if (moonMarker.IsVisible)
        {
            ExpandBounds(ref minX, ref maxX, ref minY, ref maxY, moonMarker.X, moonMarker.Y, 12d);
        }

        minX -= padding;
        maxX += padding;
        minY -= padding;
        maxY += padding;

        EnsureMinimumRange(ref minX, ref maxX, minimumWidth);
        EnsureMinimumRange(ref minY, ref maxY, minimumHeight);

        return string.Create(CultureInfo.InvariantCulture, $"{minX:F1} {minY:F1} {(maxX - minX):F1} {(maxY - minY):F1}");
    }

    private static void ExpandBounds(ref double minX, ref double maxX, ref double minY, ref double maxY, double centerX, double centerY, double radius)
    {
        minX = Math.Min(minX, centerX - radius);
        maxX = Math.Max(maxX, centerX + radius);
        minY = Math.Min(minY, centerY - radius);
        maxY = Math.Max(maxY, centerY + radius);
    }

    private static void EnsureMinimumRange(ref double min, ref double max, double minimumRange)
    {
        double currentRange = max - min;
        if (currentRange >= minimumRange)
        {
            return;
        }

        double padding = (minimumRange - currentRange) / 2d;
        min -= padding;
        max += padding;
    }

    private List<ChartPoint> BuildChartPoints(
        Func<LiveHistorySample, double?> selector,
        double? liveValue,
        DateTime windowStart,
        DateTime windowEnd)
    {
        var points = _liveHistorySamples
            .Where(record => record.TimeLocal >= windowStart && record.TimeLocal <= windowEnd)
            .Select(record => new { record.TimeLocal, Value = selector(record) })
            .Where(point => point.Value.HasValue)
            .Select(point => new ChartPoint(point.TimeLocal, point.Value!.Value))
            .OrderBy(point => point.TimeLocal)
            .ToList();

        if (!liveValue.HasValue)
        {
            return points;
        }

        var livePoint = new ChartPoint(windowEnd, liveValue.Value);
        if (points.Count > 0 && Math.Abs((points[^1].TimeLocal - livePoint.TimeLocal).TotalMinutes) < 1)
        {
            points[^1] = livePoint;
        }
        else
        {
            points.Add(livePoint);
        }

        return points;
    }

    private PlotModel BuildPlotModel(bool clampMinimumToZero, double minimumRange, params PlotSeriesSpec[] specs)
    {
        if (!ObservationWindowEndLocal.HasValue)
        {
            return PlotModel.Empty;
        }

        DateTime windowEnd = ObservationWindowEndLocal.Value;
        DateTime windowStart = windowEnd.AddHours(-24);

        var seriesWithData = specs
            .Select(spec => new { Spec = spec, Points = BuildChartPoints(spec.Selector, spec.LiveValue, windowStart, windowEnd) })
            .Where(series => series.Points.Count > 0)
            .ToList();

        if (seriesWithData.Count == 0)
        {
            return PlotModel.Empty;
        }

        const double left = 40d;
        const double right = 508d;
        const double top = 24d;
        const double bottom = 224d;
        double plotWidth = right - left;
        double plotHeight = bottom - top;

        double minValue = seriesWithData.SelectMany(series => series.Points).Min(point => point.Value);
        double maxValue = seriesWithData.SelectMany(series => series.Points).Max(point => point.Value);

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

        var plotSeries = seriesWithData
            .Select(series => BuildPlotSeries(series.Spec, series.Points, windowStart, minValue, maxValue, left, bottom, plotWidth, plotHeight))
            .Where(series => !string.IsNullOrEmpty(series.LinePath))
            .ToArray();

        if (plotSeries.Length == 0)
        {
            return PlotModel.Empty;
        }

        var axisLabels = Enumerable.Range(0, 5)
            .Select(index =>
            {
                double ratio = index / 4d;
                double value = maxValue - ((maxValue - minValue) * ratio);
                double y = top + (plotHeight * ratio);
                return new PlotLabel(y, FormatAxisValue(value));
            })
            .ToArray();

        var timeLabels = Enumerable.Range(0, 5)
            .Select(index =>
            {
                double ratio = index / 4d;
                DateTime time = windowStart.AddHours(24d * ratio);
                double x = left + (plotWidth * ratio);
                return new TimeLabel(x, time.ToString("HH:mm", CultureInfo.InvariantCulture));
            })
            .ToArray();

        return new PlotModel(axisLabels, timeLabels, plotSeries);
    }

    private static PlotSeries BuildPlotSeries(
        PlotSeriesSpec spec,
        IReadOnlyList<ChartPoint> source,
        DateTime windowStart,
        double minValue,
        double maxValue,
        double left,
        double bottom,
        double plotWidth,
        double plotHeight)
    {
        var pointSegments = SplitContinuousSegments(source);
        var coordinateSegments = pointSegments
            .Select(segment => segment
                .Select(point =>
                {
                    double x = left + (Math.Clamp((point.TimeLocal - windowStart).TotalHours / 24d, 0d, 1d) * plotWidth);
                    double y = bottom - (((point.Value - minValue) / (maxValue - minValue)) * plotHeight);
                    return new PlotPoint(x, y);
                })
                .ToList())
            .Where(segment => segment.Count > 0)
            .ToList();

        if (coordinateSegments.Count == 0)
        {
            return PlotSeries.Empty;
        }

        string linePath = string.Join(' ', coordinateSegments.Select(BuildLinePath));
        string areaPath = string.IsNullOrWhiteSpace(spec.FillColor)
            ? string.Empty
            : string.Join(' ', coordinateSegments.Where(segment => segment.Count > 1).Select(segment => BuildAreaPath(segment, bottom)));

        return new PlotSeries(linePath, areaPath, spec.StrokeColor, spec.FillColor, coordinateSegments[^1][^1]);
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

    private static MarkupString RenderSvgText(double x, double y, string text, string anchor = "start")
    {
        return new MarkupString(
            $"<text x=\"{x.ToString("F1", CultureInfo.InvariantCulture)}\" y=\"{y.ToString("F1", CultureInfo.InvariantCulture)}\" text-anchor=\"{anchor}\">{System.Net.WebUtility.HtmlEncode(text)}</text>");
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

    private sealed record ChartPoint(DateTime TimeLocal, double Value);

    private sealed record PlotSeriesSpec(
        Func<LiveHistorySample, double?> Selector,
        double? LiveValue,
        string StrokeColor,
        string? FillColor = null);

    private sealed record PlotModel(
        IReadOnlyList<PlotLabel> AxisLabels,
        IReadOnlyList<TimeLabel> TimeLabels,
        IReadOnlyList<PlotSeries> Series)
    {
        public static PlotModel Empty { get; } = new([], [], []);
        public bool HasData => Series.Count > 0;
    }

    private sealed record PlotSeries(
        string LinePath,
        string AreaPath,
        string StrokeColor,
        string? FillColor,
        PlotPoint? CurrentPoint)
    {
        public static PlotSeries Empty { get; } = new(string.Empty, string.Empty, string.Empty, null, null);
    }

    private sealed record PlotLabel(double Y, string Text);

    private sealed record TimeLabel(double X, string Text);

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
