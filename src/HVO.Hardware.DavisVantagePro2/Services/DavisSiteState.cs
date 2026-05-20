using HVO.Hardware.DavisVantagePro2.Outbox;
using HVO.Hardware.DavisVantagePro2.Protocol.Packets;
using HVO.Hardware.DavisVantagePro2.Station;
using HVO.Hardware.DavisVantagePro2.Station.Models;
using HVO.Hardware.DavisVantagePro2.Workers;
using Microsoft.Extensions.Logging;

namespace HVO.Hardware.DavisVantagePro2.Services;

public sealed class DavisSiteState : IDisposable
{
    private readonly VantageStation _station;
    private readonly WeatherStationWorker _worker;
    private readonly OutboxForwarder _forwarder;
    private readonly StationInfoSnapshotStore _stationInfoSnapshotStore;
    private readonly ILogger<DavisSiteState> _logger;
    private Task? _initializationTask;

    public DavisSiteState(
        VantageStation station,
        WeatherStationWorker worker,
        OutboxForwarder forwarder,
        StationInfoSnapshotStore stationInfoSnapshotStore,
        ILogger<DavisSiteState> logger)
    {
        _station = station;
        _worker = worker;
        _forwarder = forwarder;
        _stationInfoSnapshotStore = stationInfoSnapshotStore;
        _logger = logger;

        LatestReading = _worker.LatestReading;
        _worker.ReadingUpdated += OnReadingUpdated;
        _worker.WorkerStateChanged += OnWorkerStateChanged;
        _forwarder.SweptCompleted += OnWorkerStateChanged;
    }

    public event Action? Changed;

    public bool IsInitializing { get; private set; }

    public bool IsInitialized { get; private set; }

    public bool IsRefreshingStationInfo { get; private set; }

    public string? InitializationError { get; private set; }

    public StationInfo? StationInfo { get; private set; }

    public DateTime? StationInfoSavedAtUtc { get; private set; }

    public Loop2Packet? LatestReading { get; private set; }

    public DateTime? ObservedAtUtc => _worker.LastReadingAt ?? LatestReading?.RecordedAtUtc;

    public bool HasBlockingInitializationFailure => !IsInitialized && !IsInitializing && !string.IsNullOrWhiteSpace(InitializationError);

    public bool IsStationConnected => _station.IsConnected;

    public string ConsoleTimeZoneLabel => _station.ConsoleTimeZoneLabel;

    public TimeSpan ConsoleUtcOffset => _station.ConsoleUtcOffset;

    public int PendingOutboxCount => _forwarder.PendingCount;

    public int FailedOutboxCount => _forwarder.FailedCount;

    public string? LastOutboxError => _forwarder.LastError;

    public DateTime? LastOutboxSentAt => _forwarder.LastSentAt;

    public string StationIdentityText => StationInfo?.HardwareDescription
        ?? (IsInitializing || IsRefreshingStationInfo ? "Loading console identity" : "Console identity unavailable");

    public Task EnsureInitializedAsync()
    {
        return _initializationTask ??= InitializeCoreAsync();
    }

    public async Task RetryInitializationAsync()
    {
        _initializationTask = null;
        await EnsureInitializedAsync();
    }

    public async Task RefreshStationInfoAsync(CancellationToken ct = default)
    {
        try
        {
            await RefreshStationInfoCoreAsync(ct);
            IsInitialized = true;
            InitializationError = null;
        }
        catch (Exception ex)
        {
            InitializationError = ex.Message;
            _logger.LogWarning(ex, "Unable to refresh station info snapshot");
        }
        finally
        {
            NotifyChanged();
        }
    }

    public void Dispose()
    {
        _worker.ReadingUpdated -= OnReadingUpdated;
        _worker.WorkerStateChanged -= OnWorkerStateChanged;
        _forwarder.SweptCompleted -= OnWorkerStateChanged;
    }

    private async Task InitializeCoreAsync()
    {
        if (IsInitialized)
        {
            return;
        }

        IsInitializing = true;
        InitializationError = null;
        LatestReading = _worker.LatestReading;
        NotifyChanged();

        try
        {
            var cachedSnapshot = await _stationInfoSnapshotStore.GetAsync();
            if (cachedSnapshot is not null)
            {
                StationInfo = cachedSnapshot.StationInfo;
                StationInfoSavedAtUtc = cachedSnapshot.SavedAtUtc;
                IsInitialized = true;
                NotifyChanged();

                _ = RefreshStationInfoAsync();
                return;
            }

            await RefreshStationInfoCoreAsync(CancellationToken.None);
            IsInitialized = true;
        }
        catch (Exception ex)
        {
            InitializationError = ex.Message;
            if (StationInfo is not null)
            {
                IsInitialized = true;
            }

            _logger.LogWarning(ex, "Dashboard initialization could not refresh station info from the console");
        }
        finally
        {
            IsInitializing = false;
            NotifyChanged();
        }
    }

    private async Task RefreshStationInfoCoreAsync(CancellationToken ct)
    {
        if (IsRefreshingStationInfo)
        {
            return;
        }

        IsRefreshingStationInfo = true;
        NotifyChanged();

        try
        {
            var stationInfo = await _station.GetStationInfoAsync(ct);
            var snapshot = await _stationInfoSnapshotStore.SaveAsync(stationInfo, ct);

            StationInfo = snapshot.StationInfo;
            StationInfoSavedAtUtc = snapshot.SavedAtUtc;
            InitializationError = null;
        }
        finally
        {
            IsRefreshingStationInfo = false;
            NotifyChanged();
        }
    }

    private void OnReadingUpdated(Loop2Packet reading)
    {
        LatestReading = reading;
        NotifyChanged();
    }

    private void OnWorkerStateChanged()
    {
        NotifyChanged();
    }

    private void NotifyChanged()
    {
        Changed?.Invoke();
    }
}