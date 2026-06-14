
using HVO.Hardware.DavisVantagePro2.Outbox;
using HVO.Hardware.DavisVantagePro2.Protocol;
using HVO.Hardware.DavisVantagePro2.Station;
using HVO.Hardware.DavisVantagePro2.Station.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using System.Globalization;

namespace HVO.Hardware.DavisVantagePro2.Components.Pages;

public partial class ConsoleSettings : IDisposable
{
    private const string PageHeadingText = "Configuration";
    private const string PageSummaryText = "Grouped workspace for console configuration and local service, outbox, and cache settings.";
    private static readonly TimeSpan ClockSyncConfirmationTolerance = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan ClockSyncConfirmationTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan ClockSyncConfirmationPollInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan ClockOperationTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan LocationSettingsOperationTimeout = TimeSpan.FromSeconds(60);
    private static readonly IReadOnlyList<TimeZoneOption> AvailableTimeZones = Enumerable.Range(0, DavisTimeZoneTable.Count)
        .Select(code => new TimeZoneOption(code, DavisTimeZoneTable.GetLabel(code)))
        .ToArray();
    private static readonly IReadOnlyList<UnitOption> AvailableBarometerUnits =
    [
        new("inHg", "inHg"),
        new("mmHg", "mmHg"),
        new("hPa", "hPa"),
        new("mbar", "mbar"),
    ];
    private static readonly IReadOnlyList<UnitOption> AvailableTemperatureUnits =
    [
        new("°F", "°F (whole degrees)"),
        new("°F×10", "°F (tenths)"),
        new("°C", "°C (whole degrees)"),
        new("°C×10", "°C (tenths)"),
    ];
    private static readonly IReadOnlyList<UnitOption> AvailableRainUnits =
    [
        new("inch", "inch"),
        new("mm", "mm"),
    ];
    private static readonly IReadOnlyList<UnitOption> AvailableWindUnits =
    [
        new("mph", "mph"),
        new("m/s", "m/s"),
        new("km/h", "km/h"),
        new("knots", "knots"),
    ];
    private static readonly IReadOnlyList<NumericOption> AvailableArchiveIntervals =
    [
        new(1, "1 minute"),
        new(5, "5 minutes"),
        new(10, "10 minutes"),
        new(15, "15 minutes"),
        new(30, "30 minutes"),
        new(60, "60 minutes"),
        new(120, "120 minutes"),
    ];
    private static readonly IReadOnlyList<NumericOption> AvailableRainBucketTypes =
    [
        new(0, "0.01 inch"),
        new(1, "0.2 mm"),
        new(2, "0.1 mm"),
    ];
    private static readonly IReadOnlyList<NumericOption> AvailableRainYearMonths = Enumerable.Range(1, 12)
        .Select(month => new NumericOption(month, CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(month)))
        .ToArray();
    private static readonly IReadOnlyList<CalibrationFieldDefinition> TemperatureCalibrationFields =
    [
        new("Inside temp offset (F)", "inTemp", "0.0"),
        new("Outside temp offset (F)", "outTemp", "0.0"),
        new("Extra temp 1 offset (F)", "extraTemp1", "0.0"),
        new("Extra temp 2 offset (F)", "extraTemp2", "0.0"),
        new("Extra temp 3 offset (F)", "extraTemp3", "0.0"),
        new("Extra temp 4 offset (F)", "extraTemp4", "0.0"),
        new("Extra temp 5 offset (F)", "extraTemp5", "0.0"),
        new("Extra temp 6 offset (F)", "extraTemp6", "0.0"),
        new("Extra temp 7 offset (F)", "extraTemp7", "0.0"),
        new("Soil temp 1 offset (F)", "soilTemp1", "0.0"),
        new("Soil temp 2 offset (F)", "soilTemp2", "0.0"),
        new("Soil temp 3 offset (F)", "soilTemp3", "0.0"),
        new("Soil temp 4 offset (F)", "soilTemp4", "0.0"),
        new("Leaf temp 1 offset (F)", "leafTemp1", "0.0"),
        new("Leaf temp 2 offset (F)", "leafTemp2", "0.0"),
        new("Leaf temp 3 offset (F)", "leafTemp3", "0.0"),
        new("Leaf temp 4 offset (F)", "leafTemp4", "0.0"),
    ];
    private static readonly IReadOnlyList<CalibrationFieldDefinition> HumidityCalibrationFields =
    [
        new("Inside humidity offset (%)", "inHumid", "0"),
        new("Outside humidity offset (%)", "outHumid", "0"),
        new("Extra humidity 1 offset (%)", "extraHumid1", "0"),
        new("Extra humidity 2 offset (%)", "extraHumid2", "0"),
        new("Extra humidity 3 offset (%)", "extraHumid3", "0"),
        new("Extra humidity 4 offset (%)", "extraHumid4", "0"),
        new("Extra humidity 5 offset (%)", "extraHumid5", "0"),
        new("Extra humidity 6 offset (%)", "extraHumid6", "0"),
        new("Extra humidity 7 offset (%)", "extraHumid7", "0"),
    ];
    private static readonly IReadOnlyList<OptionDefinition> AvailableTransmitterTypes =
    [
        new(nameof(TransmitterType.Iss), "ISS"),
        new(nameof(TransmitterType.TempOnly), "Temperature only"),
        new(nameof(TransmitterType.HumidityOnly), "Humidity only"),
        new(nameof(TransmitterType.TempHumidity), "Temperature / humidity"),
        new(nameof(TransmitterType.Wind), "Wind"),
        new(nameof(TransmitterType.Rain), "Rain"),
        new(nameof(TransmitterType.Leaf), "Leaf"),
        new(nameof(TransmitterType.Soil), "Soil"),
        new(nameof(TransmitterType.LeafSoil), "Leaf / soil"),
        new(nameof(TransmitterType.SensorLink), "SensorLink"),
        new(nameof(TransmitterType.None), "None"),
    ];
    private static readonly IReadOnlyList<OptionDefinition> AvailableRepeaterOptions =
    [
        new("", "None"),
        new("A", "A"),
        new("B", "B"),
        new("C", "C"),
        new("D", "D"),
        new("E", "E"),
        new("F", "F"),
        new("G", "G"),
        new("H", "H"),
    ];
    private static readonly IReadOnlyList<OptionDefinition> AvailableSensorIdOptions =
    [
        new("", "None"),
        new("1", "1"),
        new("2", "2"),
        new("3", "3"),
        new("4", "4"),
        new("5", "5"),
        new("6", "6"),
        new("7", "7"),
    ];
    private static readonly IReadOnlyList<NumericOption> AvailableRetransmitChannels =
    [
        new(0, "Off"),
        new(1, "Channel 1"),
        new(2, "Channel 2"),
        new(3, "Channel 3"),
        new(4, "Channel 4"),
        new(5, "Channel 5"),
        new(6, "Channel 6"),
        new(7, "Channel 7"),
        new(8, "Channel 8"),
    ];
    private static readonly IReadOnlyList<TransmitterRowDefinition> TransmitterRows = Enumerable.Range(1, 8)
        .Select(channel => new TransmitterRowDefinition(channel, channel == 1 ? nameof(TransmitterType.Iss) : nameof(TransmitterType.None), string.Empty, string.Empty, string.Empty))
        .ToArray();
    private const string PendingSectionWriteMessage = "Section save wiring is still pending.";

    [Inject] private ILogger<ConsoleSettings> Logger { get; set; } = default!;
    [Inject] private VantageStation Station { get; set; } = default!;
    [Inject] private StationSettingsSnapshotStore StationSettingsSnapshotStore { get; set; } = default!;

    private readonly CancellationTokenSource _clockTickerCancellation = new();
    private DateTime? _consoleTimeSnapshot;
    private DateTimeOffset? _consoleTimeObservedAtUtc;
    private CancellationTokenSource? _locationLoadCancellation;
    private bool _isClockLoading;
    private bool _isClockBusy;
    private bool _isClockStatusError;
    private bool _isLocationSettingsBusy;
    private bool _isLocationSettingsLoading;
    private bool _isLocationStatusError;
    private bool _isPageRefreshBusy;
    private bool _isRainArchiveBusy;
    private bool _isRainArchiveStatusError;
    private StationSettings? _currentStationSettings;
    private string _detectedBarometerUnits = "--";
    private string _detectedTemperatureUnits = "--";
    private string _detectedRainUnits = "--";
    private string _detectedWindUnits = "--";

    private bool IsClockBusy => _isClockBusy;

    private bool IsClockSectionBusy => _isClockLoading || _isClockBusy;

    private bool IsLocationSettingsBusy => _isLocationSettingsBusy;

    private bool IsLocationSectionBusy => _isLocationSettingsLoading || _isLocationSettingsBusy;

    private bool IsRainArchiveBusy => _isRainArchiveBusy;

    private bool IsPageRefreshBusy => _isPageRefreshBusy;

    private DateTime? EditableConsoleTime { get; set; }

    private double? EditableLatitude { get; set; }

    private double? EditableLongitude { get; set; }

    private double? EditableAltitudeFeet { get; set; }

    private DstMode EditableDstMode { get; set; } = DstMode.Auto;

    private int? EditableTimeZoneCode { get; set; }

    private TempLogging EditableTemperatureLogging { get; set; } = TempLogging.Last;

    private int EditableArchiveIntervalMinutes { get; set; } = 15;

    private int EditableRainBucketType { get; set; }

    private int? EditableRainYearStartMonth { get; set; }

    private string EditableBarometerUnits { get; set; } = AvailableBarometerUnits[0].Value;

    private string EditableTemperatureUnits { get; set; } = AvailableTemperatureUnits[0].Value;

    private string EditableRainUnits { get; set; } = AvailableRainUnits[0].Value;

    private string EditableWindUnits { get; set; } = AvailableWindUnits[0].Value;

    private string ReferenceTimeLabel => $"Reference time ({Station.ConsoleTimeZoneLabel})";

    private string ReferenceTimeText => FormatClockDisplay(GetReferenceConsoleLocalTime());

    private string DriftText => _consoleTimeSnapshot is null || _consoleTimeObservedAtUtc is null
        ? "Loading..."
        : FormatClockDrift(GetEstimatedConsoleTime() - GetReferenceConsoleLocalTime());

    private string? ClockStatusMessage { get; set; }

    private string? LocationStatusMessage { get; set; }

    private string? RainArchiveStatusMessage { get; set; }

    private string UnitSettingsStatusMessage { get; } = "Console unit write support is not implemented yet.";

    private string? DisplayedClockStatusMessage => _isClockLoading
        ? "Loading current console time..."
        : IsClockBusy
        ? "Syncing clock to reference time..."
        : ClockStatusMessage;

    private string? DisplayedLocationStatusMessage => IsLocationSettingsBusy
        ? "Saving location, time zone, and logging..."
        : _isLocationSettingsLoading
            ? "Loading location, time zone, and logging..."
        : LocationStatusMessage;

    private string? DisplayedRainArchiveStatusMessage => IsRainArchiveBusy
        ? "Saving rain and archive settings..."
        : RainArchiveStatusMessage;

    private string ClockStatusClass => _isClockStatusError
        ? "console-settings-clock-status error"
        : _isClockLoading || IsClockBusy
            ? "console-settings-clock-status busy"
            : "console-settings-clock-status success";

    private string LocationStatusClass => _isLocationStatusError
        ? "console-settings-location-status error"
        : IsLocationSettingsBusy
            ? "console-settings-location-status busy"
            : _isLocationSettingsLoading
                ? "console-settings-location-status busy"
            : "console-settings-location-status success";

    private string RainArchiveStatusClass => _isRainArchiveStatusError
        ? "console-settings-rain-status error"
        : IsRainArchiveBusy
            ? "console-settings-rain-status busy"
            : "console-settings-rain-status success";

    private string UnitSettingsStatusClass => "console-settings-units-status";

    private string DetectedBarometerUnits => _detectedBarometerUnits;

    private string DetectedTemperatureUnits => _detectedTemperatureUnits;

    private string DetectedRainUnits => _detectedRainUnits;

    private string DetectedWindUnits => _detectedWindUnits;

    protected override void OnInitialized()
    {
        UpdateShell();
        _ = RunClockTickerAsync(_clockTickerCancellation.Token);
    }

    protected override Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            _ = InitializeConsoleSettingsAsync();
        }

        return Task.CompletedTask;
    }

    protected override void OnParametersSet()
    {
        UpdateShell();
    }

    public void Dispose()
    {
        CancelPendingLocationLoad();
        _clockTickerCancellation.Cancel();
        _clockTickerCancellation.Dispose();
    }

    private async Task InitializeConsoleSettingsAsync()
    {
        await LoadClockAsync();
        await LoadLocationSettingsAsync();
    }

    private async Task RefreshConfigurationAsync()
    {
        if (_isPageRefreshBusy)
        {
            return;
        }

        _isPageRefreshBusy = true;
        ClockStatusMessage = null;
        LocationStatusMessage = null;
        RainArchiveStatusMessage = null;
        _isClockStatusError = false;
        _isLocationStatusError = false;
        _isRainArchiveStatusError = false;

        await InvokeAsync(StateHasChanged);
        try
        {
            await LoadClockAsync();
            await LoadLocationSettingsAsync();
        }
        finally
        {
            _isPageRefreshBusy = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    private void UpdateShell()
    {
    }

    private async Task LoadClockAsync()
    {
        if (_isClockLoading)
        {
            return;
        }

        _isClockLoading = true;
        await InvokeAsync(StateHasChanged);
        try
        {
            using var timeoutCts = new CancellationTokenSource(ClockOperationTimeout);
            var consoleTime = await Station.GetConsoleTimeAsync(timeoutCts.Token);
            _consoleTimeSnapshot = DateTime.SpecifyKind(consoleTime, DateTimeKind.Unspecified);
            _consoleTimeObservedAtUtc = DateTimeOffset.UtcNow;
            EditableConsoleTime = _consoleTimeSnapshot.Value;
            ClockStatusMessage = null;
            _isClockStatusError = false;
        }
        catch (OperationCanceledException)
        {
            Logger.LogWarning("Clock load timed out on the configuration page.");
            ClockStatusMessage = "Clock data is currently unavailable.";
            _isClockStatusError = true;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Unable to load console clock on the configuration page.");
            ClockStatusMessage = "Clock data is currently unavailable.";
            _isClockStatusError = true;
        }
        finally
        {
            _isClockLoading = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task LoadLocationSettingsAsync()
    {
        var loadCancellation = BeginLocationLoad();
        var hasCachedSettings = await LoadCachedLocationSettingsAsync();

        if (!hasCachedSettings)
        {
            _isLocationSettingsLoading = true;
            await InvokeAsync(StateHasChanged);
        }

        try
        {
            using var timeoutCts = new CancellationTokenSource(LocationSettingsOperationTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, loadCancellation.Token);
            var settings = await Station.GetStationSettingsAsync(linkedCts.Token);
            ApplyLocationSettings(settings);
            await StationSettingsSnapshotStore.SaveAsync(settings, linkedCts.Token);
            LocationStatusMessage = null;
            _isLocationStatusError = false;
            await InvokeAsync(StateHasChanged);
        }
        catch (OperationCanceledException) when (loadCancellation.IsCancellationRequested)
        {
            Logger.LogDebug("Canceled background location settings load on the configuration page.");
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Unable to load live location, timezone, and logging settings on the configuration page.");

            if (EditableLatitude is null && EditableLongitude is null && EditableAltitudeFeet is null)
            {
                LocationStatusMessage = "Location settings unavailable.";
                _isLocationStatusError = true;
                await InvokeAsync(StateHasChanged);
            }
        }
        finally
        {
            EndLocationLoad(loadCancellation);
            _isLocationSettingsLoading = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task<bool> LoadCachedLocationSettingsAsync()
    {
        try
        {
            var cachedSnapshot = await StationSettingsSnapshotStore.GetAsync();
            if (cachedSnapshot is null)
            {
                return false;
            }

            ApplyLocationSettings(cachedSnapshot.Settings);
            await InvokeAsync(StateHasChanged);
            return true;
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Unable to load cached station settings for the configuration page.");
            return false;
        }
    }

    private async Task SyncClockAsync()
    {
        var selectedConsoleTime = DateTime.SpecifyKind(GetReferenceConsoleLocalTime().AddMilliseconds(750), DateTimeKind.Unspecified);
        EditableConsoleTime = selectedConsoleTime;
        ClockStatusMessage = null;
        _isClockStatusError = false;

        await RunClockOperationAsync(async ct =>
        {
            await Station.SetConsoleTimeAsync(selectedConsoleTime, ct);
            await RefreshClockSnapshotAsync(selectedConsoleTime, ct);
            ClockStatusMessage = "Clock synced to reference time.";
            _isClockStatusError = false;
        });
    }

    private async Task SaveLocationSettingsAsync()
    {
        if (!TryValidateLocationSettings(out var validationError))
        {
            LocationStatusMessage = validationError;
            _isLocationStatusError = true;
            return;
        }

        var latitude = EditableLatitude!.Value;
        var longitude = EditableLongitude!.Value;
        var altitudeFeet = EditableAltitudeFeet!.Value;
        var dstMode = EditableDstMode;
        var timeZoneCode = EditableTimeZoneCode!.Value;
        var temperatureLogging = EditableTemperatureLogging;

        if (!HasLocationSettingsChanges(latitude, longitude, altitudeFeet, dstMode, timeZoneCode, temperatureLogging))
        {
            var currentSettings = BuildUpdatedLocationSettings(latitude, longitude, altitudeFeet, dstMode, timeZoneCode, temperatureLogging);
            ApplyLocationSettings(currentSettings);
            await StationSettingsSnapshotStore.SaveAsync(currentSettings);
            LocationStatusMessage = "Location settings are already current.";
            _isLocationStatusError = false;
            return;
        }

        CancelPendingLocationLoad();
        LocationStatusMessage = null;
        _isLocationStatusError = false;

        await RunLocationOperationAsync(async ct =>
        {
            await Station.UpdateLocationSettingsAsync(latitude, longitude, altitudeFeet, dstMode, timeZoneCode, temperatureLogging, ct);

            var settings = BuildUpdatedLocationSettings(latitude, longitude, altitudeFeet, dstMode, timeZoneCode, temperatureLogging);
            ApplyLocationSettings(settings);
            await StationSettingsSnapshotStore.SaveAsync(settings, ct);
            LocationStatusMessage = "Location, time zone, and logging saved.";
            _isLocationStatusError = false;
        });
    }

    private async Task SaveRainArchiveSettingsAsync()
    {
        if (!TryValidateRainArchiveSettings(out var validationError))
        {
            RainArchiveStatusMessage = validationError;
            _isRainArchiveStatusError = true;
            return;
        }

        var archiveIntervalMinutes = EditableArchiveIntervalMinutes;
        var rainBucketType = EditableRainBucketType;
        var rainYearStartMonth = EditableRainYearStartMonth!.Value;

        if (!HasRainArchiveSettingsChanges(archiveIntervalMinutes, rainBucketType, rainYearStartMonth))
        {
            var currentSettings = BuildUpdatedRainArchiveSettings(archiveIntervalMinutes, rainBucketType, rainYearStartMonth);
            ApplyLocationSettings(currentSettings);
            await StationSettingsSnapshotStore.SaveAsync(currentSettings);
            RainArchiveStatusMessage = "Rain and archive settings are already current.";
            _isRainArchiveStatusError = false;
            return;
        }

        RainArchiveStatusMessage = null;
        _isRainArchiveStatusError = false;

        await RunRainArchiveOperationAsync(async ct =>
        {
            await Station.UpdateRainArchiveSettingsAsync(archiveIntervalMinutes, rainBucketType, rainYearStartMonth, ct);

            var settings = BuildUpdatedRainArchiveSettings(archiveIntervalMinutes, rainBucketType, rainYearStartMonth);
            ApplyLocationSettings(settings);
            await StationSettingsSnapshotStore.SaveAsync(settings, ct);
            RainArchiveStatusMessage = "Rain and archive settings saved.";
            _isRainArchiveStatusError = false;
        });
    }

    private async Task RefreshClockSnapshotAsync(DateTime? expectedConsoleTime = null, CancellationToken ct = default)
    {
        var consoleTime = expectedConsoleTime.HasValue
            ? await WaitForConsoleTimeAsync(expectedConsoleTime.Value, ct)
            : DateTime.SpecifyKind(await Station.GetConsoleTimeAsync(ct), DateTimeKind.Unspecified);

        _consoleTimeSnapshot = consoleTime;
        _consoleTimeObservedAtUtc = DateTimeOffset.UtcNow;
        EditableConsoleTime = consoleTime;
    }

    private async Task RunLocationOperationAsync(Func<CancellationToken, Task> operation)
    {
        if (_isLocationSettingsBusy)
        {
            return;
        }

        _isLocationSettingsBusy = true;
        await InvokeAsync(StateHasChanged);
        try
        {
            using var timeoutCts = new CancellationTokenSource(LocationSettingsOperationTimeout);
            await operation(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (_isLocationSettingsBusy)
        {
            Logger.LogWarning("Location settings operation timed out on the configuration page.");
            LocationStatusMessage = "Location settings save timed out.";
            _isLocationStatusError = true;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Unable to refresh or update location, timezone, and logging settings on the configuration page.");
            LocationStatusMessage = "Location settings are currently unavailable.";
            _isLocationStatusError = true;
        }
        finally
        {
            _isLocationSettingsBusy = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task RunRainArchiveOperationAsync(Func<CancellationToken, Task> operation)
    {
        if (_isRainArchiveBusy)
        {
            return;
        }

        _isRainArchiveBusy = true;
        await InvokeAsync(StateHasChanged);
        try
        {
            using var timeoutCts = new CancellationTokenSource(LocationSettingsOperationTimeout);
            await operation(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (_isRainArchiveBusy)
        {
            Logger.LogWarning("Rain/archive operation timed out on the configuration page.");
            RainArchiveStatusMessage = "Rain and archive settings save timed out.";
            _isRainArchiveStatusError = true;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Unable to update rain and archive settings on the configuration page.");
            RainArchiveStatusMessage = "Rain and archive settings are currently unavailable.";
            _isRainArchiveStatusError = true;
        }
        finally
        {
            _isRainArchiveBusy = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task RunClockOperationAsync(Func<CancellationToken, Task> operation)
    {
        if (_isClockBusy)
        {
            return;
        }

        _isClockBusy = true;
        await InvokeAsync(StateHasChanged);
        try
        {
            using var timeoutCts = new CancellationTokenSource(ClockOperationTimeout);
            await operation(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (_isClockBusy)
        {
            Logger.LogWarning("Clock operation timed out on the configuration page.");
            ClockStatusMessage = "Clock sync timed out.";
            _isClockStatusError = true;
        }
        catch (TimeoutException ex)
        {
            Logger.LogWarning(ex, "Clock confirmation timed out on the configuration page.");
            ClockStatusMessage = "Clock sync timed out.";
            _isClockStatusError = true;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Unable to refresh or update console clock on the configuration page.");
            ClockStatusMessage = "Clock data is currently unavailable.";
            _isClockStatusError = true;
        }
        finally
        {
            _isClockBusy = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task RunClockTickerAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await InvokeAsync(StateHasChanged);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private DateTime GetReferenceConsoleLocalTime() =>
        DateTime.SpecifyKind(DateTimeOffset.UtcNow.ToOffset(Station.ConsoleUtcOffset).DateTime, DateTimeKind.Unspecified);

    private DateTime GetEstimatedConsoleTime() =>
        _consoleTimeSnapshot is null || _consoleTimeObservedAtUtc is null
            ? GetReferenceConsoleLocalTime()
            : DateTime.SpecifyKind(_consoleTimeSnapshot.Value + (DateTimeOffset.UtcNow - _consoleTimeObservedAtUtc.Value), DateTimeKind.Unspecified);

    private async Task<DateTime> WaitForConsoleTimeAsync(DateTime expectedConsoleTime, CancellationToken ct = default)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(ClockSyncConfirmationTimeout);
        DateTime lastReadback = expectedConsoleTime;

        while (!timeoutCts.IsCancellationRequested)
        {
            lastReadback = DateTime.SpecifyKind(await Station.GetConsoleTimeAsync(timeoutCts.Token), DateTimeKind.Unspecified);

            if (Math.Abs((lastReadback - expectedConsoleTime).TotalSeconds) <= ClockSyncConfirmationTolerance.TotalSeconds)
            {
                return lastReadback;
            }

            await Task.Delay(ClockSyncConfirmationPollInterval, timeoutCts.Token);
        }

        throw new TimeoutException($"Console clock did not confirm the requested time. Last readback was {FormatClockDisplay(lastReadback)}.");
    }

    private static string FormatClockDisplay(DateTime value) => value.ToString("MM/dd/yyyy, hh:mm:ss tt", CultureInfo.InvariantCulture);

    private void ApplyLocationSettings(StationSettings settings)
    {
        _currentStationSettings = settings;
        EditableLatitude = settings.LatitudeDegrees;
        EditableLongitude = settings.LongitudeDegrees;
        EditableAltitudeFeet = settings.AltitudeFeet;
        EditableDstMode = ParseDstMode(settings.DstSetting);
        EditableTimeZoneCode = ResolveTimeZoneCode(settings);
        EditableTemperatureLogging = ParseTemperatureLogging(settings.TemperatureLogging);
        EditableArchiveIntervalMinutes = Math.Clamp(settings.ArchiveIntervalMinutes, 1, 120);
        EditableRainBucketType = settings.RainBucketType;
        EditableRainYearStartMonth = settings.RainYearStartMonth is >= 1 and <= 12 ? settings.RainYearStartMonth : null;

        _detectedBarometerUnits = settings.BarometerUnits;
        _detectedTemperatureUnits = settings.TemperatureUnits;
        _detectedRainUnits = settings.RainUnits;
        _detectedWindUnits = settings.WindUnits;
        EditableBarometerUnits = MatchUnitValue(settings.BarometerUnits, AvailableBarometerUnits, AvailableBarometerUnits[0].Value);
        EditableTemperatureUnits = MatchUnitValue(settings.TemperatureUnits, AvailableTemperatureUnits, AvailableTemperatureUnits[0].Value);
        EditableRainUnits = MatchUnitValue(settings.RainUnits, AvailableRainUnits, AvailableRainUnits[0].Value);
        EditableWindUnits = MatchUnitValue(settings.WindUnits, AvailableWindUnits, AvailableWindUnits[0].Value);
    }

    private StationSettings BuildUpdatedLocationSettings(
        double latitude,
        double longitude,
        double altitudeFeet,
        DstMode dstMode,
        int timeZoneCode,
        TempLogging temperatureLogging)
    {
        var existingSettings = _currentStationSettings ?? new StationSettings();

        return existingSettings with
        {
            LatitudeDegrees = latitude,
            LongitudeDegrees = longitude,
            AltitudeFeet = altitudeFeet,
            DstSetting = dstMode.ToString().ToUpperInvariant(),
            UseTimezoneCode = true,
            TimezoneCode = timeZoneCode,
            GmtOffsetHours = (DavisTimeZoneTable.GetOffset(timeZoneCode) ?? TimeSpan.Zero).TotalHours,
            TemperatureLogging = temperatureLogging == TempLogging.Last ? "LAST" : "AVERAGE",
        };
    }

    private StationSettings BuildUpdatedRainArchiveSettings(int archiveIntervalMinutes, int rainBucketType, int rainYearStartMonth)
    {
        var existingSettings = _currentStationSettings ?? new StationSettings();

        return existingSettings with
        {
            ArchiveIntervalSeconds = archiveIntervalMinutes * 60,
            RainBucketType = rainBucketType,
            RainYearStartMonth = rainYearStartMonth,
        };
    }

    private bool HasLocationSettingsChanges(
        double latitude,
        double longitude,
        double altitudeFeet,
        DstMode dstMode,
        int timeZoneCode,
        TempLogging temperatureLogging)
    {
        if (_currentStationSettings is null)
        {
            return true;
        }

        return _currentStationSettings.LatitudeDegrees != latitude
            || _currentStationSettings.LongitudeDegrees != longitude
            || _currentStationSettings.AltitudeFeet != altitudeFeet
            || !string.Equals(_currentStationSettings.DstSetting, dstMode.ToString().ToUpperInvariant(), StringComparison.OrdinalIgnoreCase)
            || !_currentStationSettings.UseTimezoneCode
            || _currentStationSettings.TimezoneCode != timeZoneCode
            || !string.Equals(_currentStationSettings.TemperatureLogging, temperatureLogging == TempLogging.Last ? "LAST" : "AVERAGE", StringComparison.OrdinalIgnoreCase);
    }

    private bool HasRainArchiveSettingsChanges(int archiveIntervalMinutes, int rainBucketType, int rainYearStartMonth)
    {
        if (_currentStationSettings is null)
        {
            return true;
        }

        return _currentStationSettings.ArchiveIntervalMinutes != archiveIntervalMinutes
            || _currentStationSettings.RainBucketType != rainBucketType
            || _currentStationSettings.RainYearStartMonth != rainYearStartMonth;
    }

    private CancellationTokenSource BeginLocationLoad()
    {
        CancelPendingLocationLoad();
        _locationLoadCancellation = new CancellationTokenSource();
        return _locationLoadCancellation;
    }

    private void EndLocationLoad(CancellationTokenSource completedCancellation)
    {
        if (!ReferenceEquals(_locationLoadCancellation, completedCancellation))
        {
            completedCancellation.Dispose();
            return;
        }

        _locationLoadCancellation.Dispose();
        _locationLoadCancellation = null;
    }

    private void CancelPendingLocationLoad()
    {
        _locationLoadCancellation?.Cancel();
        _locationLoadCancellation?.Dispose();
        _locationLoadCancellation = null;
    }

    private static DstMode ParseDstMode(string value) =>
        Enum.TryParse<DstMode>(value, ignoreCase: true, out var mode)
            ? mode
            : DstMode.Auto;

    private static TempLogging ParseTemperatureLogging(string value) =>
        Enum.TryParse<TempLogging>(value, ignoreCase: true, out var mode)
            ? mode
            : TempLogging.Last;

    private static string MatchUnitValue(string currentValue, IReadOnlyList<UnitOption> options, string fallbackValue) =>
        options.FirstOrDefault(option => string.Equals(option.Value, currentValue, StringComparison.OrdinalIgnoreCase))?.Value ?? fallbackValue;

    private static int? ResolveTimeZoneCode(StationSettings settings)
    {
        if (settings.UseTimezoneCode)
        {
            return settings.TimezoneCode;
        }

        for (var code = 0; code < DavisTimeZoneTable.Count; code++)
        {
            var offset = DavisTimeZoneTable.GetOffset(code);
            if (offset.HasValue && offset.Value == settings.UtcOffset)
            {
                return code;
            }
        }

        return null;
    }

    private bool TryValidateLocationSettings(out string validationError)
    {
        if (!EditableLatitude.HasValue || EditableLatitude.Value is < -90 or > 90)
        {
            validationError = "Latitude must be between -90 and 90.";
            return false;
        }

        if (!EditableLongitude.HasValue || EditableLongitude.Value is < -180 or > 180)
        {
            validationError = "Longitude must be between -180 and 180.";
            return false;
        }

        if (!EditableAltitudeFeet.HasValue || EditableAltitudeFeet.Value is < 0 or > 20000)
        {
            validationError = "Altitude must be between 0 and 20000 feet.";
            return false;
        }

        if (!EditableTimeZoneCode.HasValue)
        {
            validationError = "Choose a supported time zone before saving.";
            return false;
        }

        validationError = string.Empty;
        return true;
    }

    private bool TryValidateRainArchiveSettings(out string validationError)
    {
        if (!AvailableArchiveIntervals.Any(option => option.Value == EditableArchiveIntervalMinutes))
        {
            validationError = "Choose a supported archive interval before saving.";
            return false;
        }

        if (!AvailableRainBucketTypes.Any(option => option.Value == EditableRainBucketType))
        {
            validationError = "Choose a supported rain bucket type before saving.";
            return false;
        }

        if (!EditableRainYearStartMonth.HasValue || EditableRainYearStartMonth.Value is < 1 or > 12)
        {
            validationError = "Choose a rain year start month before saving.";
            return false;
        }

        validationError = string.Empty;
        return true;
    }

    private static string FormatClockDrift(TimeSpan delta)
    {
        if (delta.Duration() < TimeSpan.FromSeconds(1))
        {
            return "In sync";
        }

        var absolute = delta.Duration();
        var parts = new List<string>(4);

        if (absolute.Days > 0)
        {
            parts.Add($"{absolute.Days}d");
        }

        if (absolute.Hours > 0)
        {
            parts.Add($"{absolute.Hours}h");
        }

        if (absolute.Minutes > 0)
        {
            parts.Add($"{absolute.Minutes}m");
        }

        if (absolute.Seconds > 0 || parts.Count == 0)
        {
            parts.Add($"{absolute.Seconds}s");
        }

        var direction = delta > TimeSpan.Zero ? "ahead" : "behind";
        return $"{string.Join(" ", parts)} {direction}";
    }

    private sealed record TimeZoneOption(int Code, string Label);

    private sealed record UnitOption(string Value, string Label);

    private sealed record NumericOption(int Value, string Label);

    private sealed record OptionDefinition(string Value, string Label);

    private sealed record CalibrationFieldDefinition(string Label, string Variable, string PlaceholderValue);

    private sealed record TransmitterRowDefinition(int Channel, string TypeValue, string RepeaterValue, string ExtraTempValue, string ExtraHumidityValue);
}