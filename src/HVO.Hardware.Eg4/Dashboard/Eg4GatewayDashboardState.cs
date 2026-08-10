using HVO.Edge.Contracts.PowerSystem;
using HVO.Hardware.Eg4.Configuration;
using HVO.Hardware.Eg4.Protocol;
using Microsoft.Extensions.Options;

namespace HVO.Hardware.Eg4.Dashboard;

public enum Eg4DashboardDeviceState { Waiting, Online, Degraded, Offline, Disabled, Unavailable }
public enum Eg4DashboardHealthState { Healthy, Degraded, Offline, Misconfigured }
public enum Eg4DashboardSeriesKind { Pv, Battery }

public sealed record Eg4DashboardPowerPoint(
    DateTime ObservedAtUtc,
    string SeriesId,
    string Label,
    Eg4DashboardSeriesKind Kind,
    double PowerW);

public sealed record Eg4DashboardDevice(
    string SourceId,
    string DeviceId,
    string Alias,
    Eg4DeviceType Type,
    PowerMeasurementRole Role,
    Eg4DashboardDeviceState State,
    DateTime? ObservedAtUtc = null,
    double? VoltageV = null,
    double? CurrentA = null,
    double? PowerW = null,
    double? StateOfChargePercent = null,
    PowerObservationProvenance Provenance = PowerObservationProvenance.Unknown,
    string? Confidence = null,
    string? Model = null,
    string? Firmware = null,
    string? LastError = null,
    bool IsStale = false,
    PowerMpptDetailPayload? MpptDetail = null,
    PowerInverterDetailPayload? InverterDetail = null);

public sealed record Eg4OutboxDashboard(
    int PendingCount,
    int FailedCount,
    DateTime? LastSentAtUtc,
    int LastBatchCount,
    string ForwardingStatus,
    string? EndpointHost,
    string? LastError,
    int BatchSize,
    int SweepIntervalSeconds,
    bool IsOverride,
    bool IsRuntimeAvailable,
    int PermanentFailedCount = 0,
    int RetryExhaustedCount = 0);

public sealed record Eg4GatewayDashboardSnapshot(
    IReadOnlyList<Eg4DashboardDevice> Devices,
    Eg4OutboxDashboard Outbox,
    DateTime? RefreshedAtUtc,
    IReadOnlyList<Eg4DashboardPowerPoint>? History = null,
    int HistoryRevision = 0)
{
    public int ConfiguredCount => Devices.Count;
    public int OnlineCount => Devices.Count(device => device.State == Eg4DashboardDeviceState.Online);
    public int DegradedCount => Devices.Count(device => device.State is Eg4DashboardDeviceState.Waiting or Eg4DashboardDeviceState.Degraded);
    public int OfflineCount => Devices.Count(device => device.State == Eg4DashboardDeviceState.Offline);
    public int DisabledCount => Devices.Count(device => device.State == Eg4DashboardDeviceState.Disabled);
    public int UnavailableCount => Devices.Count(device => device.State == Eg4DashboardDeviceState.Unavailable);
    public IReadOnlyList<Eg4DashboardPowerPoint> PowerHistory => History ?? [];
    public int ExpectedPvTrackerCount => Devices
        .Where(device => device.State is not Eg4DashboardDeviceState.Disabled and not Eg4DashboardDeviceState.Unavailable)
        .Sum(device => device.Type == Eg4DeviceType.Inverter6500Ex ? 2 : 1);
    public IReadOnlyList<PowerMpptTrackerDetail> PvTrackers => Devices
        .Where(device => device.State == Eg4DashboardDeviceState.Online && !device.IsStale)
        .SelectMany(device => device.MpptDetail?.Trackers ?? [])
        .ToArray();
    public double? PvPowerW => PvTrackers.Count == ExpectedPvTrackerCount && PvTrackers.All(tracker => tracker.PowerW.HasValue)
        ? PvTrackers.Sum(tracker => tracker.PowerW!.Value)
        : null;
    public int ActiveCount => Devices.Count(device => device.State is not Eg4DashboardDeviceState.Disabled and not Eg4DashboardDeviceState.Unavailable);
    public Eg4DashboardHealthState HealthState => ActiveCount switch
    {
        0 => Eg4DashboardHealthState.Misconfigured,
        _ when OfflineCount == 0 && DegradedCount == 0 => Eg4DashboardHealthState.Healthy,
        _ when OnlineCount > 0 || DegradedCount > 0 => Eg4DashboardHealthState.Degraded,
        _ => Eg4DashboardHealthState.Offline,
    };
}

public interface IEg4GatewayDashboardState
{
    event Action? Changed;
    Eg4GatewayDashboardSnapshot GetSnapshot();
    Eg4OutboxSettingsResponse UpdateOutboxSettings(Eg4OutboxSettingsUpdate update);
}

public interface IEg4GatewayDashboardPublisher
{
    void Publish(
        Eg4DeviceOptions device,
        PowerBatteryObservation observation,
        string? model = null,
        string? firmware = null,
        PowerMpptDetailPayload? mpptDetail = null,
        PowerInverterDetailPayload? inverterDetail = null);
    void PublishFailure(Eg4DeviceOptions device, Exception exception);
}

public sealed class Eg4GatewayDashboardState : IEg4GatewayDashboardState, IEg4GatewayDashboardPublisher
{
    private readonly IOptions<Eg4Options> _options;
    private readonly TimeProvider _timeProvider;
    private readonly IEg4OutboxDashboardProvider _outboxProvider;
    private readonly object _stateLock = new();
    private readonly Dictionary<string, DeviceRuntime> _runtime = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<Eg4DashboardPowerPoint> _history = new();
    private int _historyRevision;
    private DateTime? _refreshedAtUtc;

    public Eg4GatewayDashboardState(
        IOptions<Eg4Options> options,
        TimeProvider timeProvider,
        IEg4OutboxDashboardProvider outboxProvider)
    {
        _options = options;
        _timeProvider = timeProvider;
        _outboxProvider = outboxProvider;
    }

    public event Action? Changed;

    public Eg4GatewayDashboardSnapshot GetSnapshot()
    {
        lock (_stateLock)
        {
            var now = _timeProvider.GetUtcNow().UtcDateTime;
            var devices = (_options.Value.Devices ?? [])
                .OrderBy(device => device.SourceId, StringComparer.OrdinalIgnoreCase)
                .Select(device => CreateDeviceSnapshot(device, now))
                .ToArray();
            return new Eg4GatewayDashboardSnapshot(
                devices,
                _outboxProvider.GetSnapshot(),
                _refreshedAtUtc,
                _history.ToArray(),
                _historyRevision);
        }
    }

    public void Publish(
        Eg4DeviceOptions device,
        PowerBatteryObservation observation,
        string? model = null,
        string? firmware = null,
        PowerMpptDetailPayload? mpptDetail = null,
        PowerInverterDetailPayload? inverterDetail = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(observation);
        var expectedRole = device.Type == Eg4DeviceType.Inverter6500Ex
            ? PowerMeasurementRole.InverterBranch
            : PowerMeasurementRole.ChargeControllerBranch;
        if (!string.Equals(device.SourceId, observation.SourceId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(device.DeviceId, observation.DeviceId, StringComparison.OrdinalIgnoreCase) ||
            observation.Role != expectedRole)
            throw new ArgumentException("Published EG4 observation identity or measurement role does not match the configured device.", nameof(observation));
        ValidateDetailIdentity(device, mpptDetail, nameof(mpptDetail));
        ValidateDetailIdentity(device, inverterDetail, nameof(inverterDetail));
        lock (_stateLock)
        {
            _runtime[device.SourceId] = new DeviceRuntime(observation, mpptDetail, inverterDetail, null, model, firmware);
            AppendHistory(observation, mpptDetail);
            _refreshedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
        }
        Changed?.Invoke();
    }

    public void PublishFailure(Eg4DeviceOptions device, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(exception);
        lock (_stateLock)
        {
            _runtime.TryGetValue(device.SourceId, out var previous);
            _runtime[device.SourceId] = new DeviceRuntime(
                previous?.Observation,
                previous?.MpptDetail,
                previous?.InverterDetail,
                SafeError(exception),
                previous?.Model,
                previous?.Firmware);
            _refreshedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
        }
        Changed?.Invoke();
    }

    public Eg4OutboxSettingsResponse UpdateOutboxSettings(Eg4OutboxSettingsUpdate update) => _outboxProvider.Update(update);

    private Eg4DashboardDevice CreateDeviceSnapshot(Eg4DeviceOptions device, DateTime now)
    {
        _runtime.TryGetValue(device.SourceId, out var runtime);
        var observation = runtime?.Observation;
        var staleAfter = TimeSpan.FromSeconds(2 * (device.PollIntervalSeconds ?? _options.Value.DefaultPollIntervalSeconds));
        var isStale = observation is not null && now - observation.ObservedAtUtc > staleAfter;
        var hasTelemetry = observation is not null &&
            (observation.VoltageV.HasValue || observation.CurrentA.HasValue || observation.PowerW.HasValue || observation.StateOfChargePercent.HasValue);
        var state = !device.Enabled
            ? device.Type == Eg4DeviceType.ChargeControllerMppt10048Hv
                ? Eg4DashboardDeviceState.Unavailable
                : Eg4DashboardDeviceState.Disabled
            : observation is null
                ? runtime?.Error is null ? Eg4DashboardDeviceState.Waiting : Eg4DashboardDeviceState.Offline
                : runtime?.Error is not null || isStale || !hasTelemetry
                    ? Eg4DashboardDeviceState.Degraded
                    : Eg4DashboardDeviceState.Online;
        return new Eg4DashboardDevice(
            device.SourceId,
            device.DeviceId,
            device.Alias,
            device.Type,
            device.Type == Eg4DeviceType.Inverter6500Ex ? PowerMeasurementRole.InverterBranch : PowerMeasurementRole.ChargeControllerBranch,
            state,
            observation?.ObservedAtUtc,
            observation?.VoltageV,
            observation?.CurrentA,
            observation?.PowerW,
            observation?.StateOfChargePercent,
            observation?.Provenance ?? PowerObservationProvenance.Unknown,
            observation?.Confidence,
            runtime?.Model,
            runtime?.Firmware,
            LastError: runtime?.Error,
            IsStale: isStale,
            MpptDetail: runtime?.MpptDetail,
            InverterDetail: runtime?.InverterDetail);
    }

    private void AppendHistory(PowerBatteryObservation observation, PowerMpptDetailPayload? mpptDetail)
    {
        var appended = false;
        if (observation.PowerW.HasValue)
        {
            _history.Enqueue(new Eg4DashboardPowerPoint(
                observation.ObservedAtUtc,
                $"{observation.SourceId}/battery",
                observation.Role == PowerMeasurementRole.InverterBranch ? "Inverter battery" : "Controller battery output",
                Eg4DashboardSeriesKind.Battery,
                observation.PowerW.Value));
            appended = true;
        }

        foreach (var tracker in mpptDetail?.Trackers ?? [])
        {
            if (tracker.PowerW.HasValue)
            {
                _history.Enqueue(new Eg4DashboardPowerPoint(
                    mpptDetail!.RecordedAtUtc,
                    $"{mpptDetail.SourceId}/{tracker.TrackerId}",
                    $"{tracker.Name} ({mpptDetail.SourceId})",
                    Eg4DashboardSeriesKind.Pv,
                    tracker.PowerW.Value));
                appended = true;
            }
        }

        while (_history.Count > 2_000)
            _history.Dequeue();
        if (appended)
            _historyRevision++;
    }

    private static void ValidateDetailIdentity(Eg4DeviceOptions device, PowerMpptDetailPayload? detail, string parameterName)
    {
        if (detail is not null && (!string.Equals(device.SourceId, detail.SourceId, StringComparison.OrdinalIgnoreCase) ||
                                   !string.Equals(device.DeviceId, detail.DeviceId, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Published EG4 MPPT detail identity does not match the configured device.", parameterName);
    }

    private static void ValidateDetailIdentity(Eg4DeviceOptions device, PowerInverterDetailPayload? detail, string parameterName)
    {
        if (detail is not null && (!string.Equals(device.SourceId, detail.SourceId, StringComparison.OrdinalIgnoreCase) ||
                                   !string.Equals(device.DeviceId, detail.DeviceId, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Published EG4 inverter detail identity does not match the configured device.", parameterName);
    }

    private static string SafeError(Exception exception) => exception switch
    {
        Eg4TransportException transport => $"Transport {transport.Kind}",
        TimeoutException => "Read timed out",
        _ => "Read failed",
    };

    private sealed record DeviceRuntime(
        PowerBatteryObservation? Observation,
        PowerMpptDetailPayload? MpptDetail,
        PowerInverterDetailPayload? InverterDetail,
        string? Error,
        string? Model,
        string? Firmware);
}
