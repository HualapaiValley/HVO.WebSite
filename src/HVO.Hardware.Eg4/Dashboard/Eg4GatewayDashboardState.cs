using HVO.Edge.Contracts.PowerSystem;
using HVO.Hardware.Eg4.Configuration;
using HVO.Hardware.Eg4.Protocol;
using Microsoft.Extensions.Options;

namespace HVO.Hardware.Eg4.Dashboard;

public enum Eg4DashboardDeviceState { Waiting, Online, Degraded, Offline, Disabled }
public enum Eg4DashboardHealthState { Healthy, Degraded, Offline, Misconfigured }

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
    bool IsStale = false);

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
    DateTime? RefreshedAtUtc)
{
    public int ConfiguredCount => Devices.Count;
    public int OnlineCount => Devices.Count(device => device.State == Eg4DashboardDeviceState.Online);
    public int DegradedCount => Devices.Count(device => device.State is Eg4DashboardDeviceState.Waiting or Eg4DashboardDeviceState.Degraded);
    public int OfflineCount => Devices.Count(device => device.State == Eg4DashboardDeviceState.Offline);
    public int DisabledCount => Devices.Count(device => device.State == Eg4DashboardDeviceState.Disabled);
    public Eg4DashboardHealthState HealthState => Devices.Count(device => device.State != Eg4DashboardDeviceState.Disabled) switch
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
    void Publish(Eg4DeviceOptions device, PowerBatteryObservation observation, string? model = null, string? firmware = null);
    void PublishFailure(Eg4DeviceOptions device, Exception exception);
}

public sealed class Eg4GatewayDashboardState : IEg4GatewayDashboardState, IEg4GatewayDashboardPublisher
{
    private readonly IOptions<Eg4Options> _options;
    private readonly TimeProvider _timeProvider;
    private readonly IEg4OutboxDashboardProvider _outboxProvider;
    private readonly object _stateLock = new();
    private readonly Dictionary<string, DeviceRuntime> _runtime = new(StringComparer.OrdinalIgnoreCase);
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
            return new Eg4GatewayDashboardSnapshot(devices, _outboxProvider.GetSnapshot(), _refreshedAtUtc);
        }
    }

    public void Publish(Eg4DeviceOptions device, PowerBatteryObservation observation, string? model = null, string? firmware = null)
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
        lock (_stateLock)
        {
            _runtime[device.SourceId] = new DeviceRuntime(observation, null, model, firmware);
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
            _runtime[device.SourceId] = new DeviceRuntime(previous?.Observation, SafeError(exception), previous?.Model, previous?.Firmware);
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
            ? Eg4DashboardDeviceState.Disabled
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
            IsStale: isStale);
    }

    private static string SafeError(Exception exception) => exception switch
    {
        Eg4TransportException transport => $"Transport {transport.Kind}",
        TimeoutException => "Read timed out",
        _ => "Read failed",
    };

    private sealed record DeviceRuntime(PowerBatteryObservation? Observation, string? Error, string? Model, string? Firmware);
}
