using System.Text.Json;
using HVO.Edge.Contracts.PowerSystem;
using HVO.WebSite.v9.Models;

namespace HVO.WebSite.v9.Services;

/// <summary>A component-owned, cancellable lifecycle with one serialized load path.</summary>
public sealed class PowerDashboardSession : IAsyncDisposable
{
    private readonly IPowerDashboardQuery query;
    private readonly TimeProvider clock;
    private readonly PowerDashboardSettings settings;
    private readonly ILogger<PowerDashboardSession> logger;
    private readonly CancellationTokenSource _stopping = new();
    private readonly CancellationToken _stopToken;
    private readonly SemaphoreSlim _loadGate = new(1, 1);
    private IReadOnlyDictionary<string, string> _errors = new Dictionary<string, string>(StringComparer.Ordinal);
    private Task? _loop;
    private Task? _periodicLoad;
    private long? _currentLoadedAt;
    private long? _historyLoadedAt;
    private string? _historyFingerprint;
    private int _disposed;

    public PowerDashboardSession(IPowerDashboardQuery query, TimeProvider clock, PowerDashboardSettings settings, ILogger<PowerDashboardSession> logger)
    {
        this.query = query;
        this.clock = clock;
        this.settings = settings;
        this.logger = logger;
        _stopToken = _stopping.Token;
    }

    public Func<Task>? Changed { get; set; }
    public bool IsLoading { get; private set; } = true;
    public bool HasLoaded { get; private set; }
    public PowerSystemSnapshot? Snapshot { get; private set; }
    public PowerInverterDetailSnapshotResponse Inverter { get; private set; } = new();
    public PowerMpptDetailSnapshotResponse Controller { get; private set; } = new();
    public PowerTelemetryHistoryResponse History { get; private set; } = PowerTelemetryHistoryResponse.Empty;
    public int HistoryRevision { get; private set; }
    public IReadOnlyDictionary<string, string> Errors => _errors;
    public PowerStatusViewModel ViewModel => PowerStatusViewModel.FromSnapshot(Snapshot, settings.Composition, clock.GetUtcNow().UtcDateTime);
    public PowerEg4EquipmentViewModel Equipment => PowerEg4EquipmentViewModel.FromSnapshots(Inverter, Controller, clock.GetUtcNow().UtcDateTime, settings.Composition);

    public async Task StartAsync()
    {
        await RefreshAsync(force: true);
        if (Volatile.Read(ref _disposed) != 0) return;
        _stopToken.ThrowIfCancellationRequested();
        _loop = RunAsync();
    }

    public Task RetryAsync(CancellationToken cancellationToken = default) => RefreshAsync(force: true, cancellationToken);

    private async Task RunAsync()
    {
        try
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), clock, _stopToken);
                if (_periodicLoad is null || _periodicLoad.IsCompleted)
                {
                    if (_periodicLoad is not null) await _periodicLoad;
                    // Own at most one periodic load; retries use the same serialization gate.
                    _periodicLoad = RefreshAsync(force: false);
                }
                // Age already displayed source observations even while a provider read is pending.
                await NotifyChangedAsync();
            }
        }
        catch (OperationCanceledException) when (_stopToken.IsCancellationRequested) { }
    }

    private async Task RefreshAsync(bool force, CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_stopToken, cancellationToken);
        var token = linked.Token;
        ThrowIfCancelled(token);
        await _loadGate.WaitAsync(token);
        try
        {
            ThrowIfCancelled(token);
            var now = clock.GetTimestamp();
            var currentDue = force || _currentLoadedAt is null || clock.GetElapsedTime(_currentLoadedAt.Value, now) >= settings.CurrentInterval;
            var historyDue = force || _historyLoadedAt is null || clock.GetElapsedTime(_historyLoadedAt.Value, now) >= settings.HistoryInterval;
            IsLoading = currentDue || historyDue;
            if (currentDue)
            {
                try
                {
                    var result = await query.GetCurrentAsync(token);
                    ThrowIfCancelled(token);
                    Apply("current", result.Snapshot, value => Snapshot = value);
                    Apply("inverter", result.Inverter, value => Inverter = value ?? new());
                    Apply("controller", result.Controller, value => Controller = value ?? new());
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested || _stopToken.IsCancellationRequested) { throw; }
                catch (Exception exception)
                {
                    logger.LogWarning(exception, "Dashboard current operation failed");
                    SetError("current", DashboardSection<PowerSystemSnapshot>.Failed("Current power").Error);
                }
                _currentLoadedAt = clock.GetTimestamp();
            }
            if (historyDue)
            {
                ThrowIfCancelled(token);
                try
                {
                    var result = await query.GetHistoryAsync(clock.GetUtcNow().UtcDateTime.AddHours(-settings.HistoryHours), token);
                    ThrowIfCancelled(token);
                    Apply("history", result.History, value =>
                    {
                        var history = value ?? PowerTelemetryHistoryResponse.Empty;
                        // Compare content and the query window, never row count. Work only on the slower history cadence.
                        var fingerprint = JsonSerializer.Serialize(new { result.WindowStartUtc, History = history });
                        if (fingerprint != _historyFingerprint)
                        {
                            History = history;
                            _historyFingerprint = fingerprint;
                            HistoryRevision++;
                        }
                    });
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested || _stopToken.IsCancellationRequested) { throw; }
                catch (Exception exception)
                {
                    logger.LogWarning(exception, "Dashboard history operation failed");
                    SetError("history", DashboardSection<PowerTelemetryHistoryResponse>.Failed("Power history").Error);
                }
                _historyLoadedAt = clock.GetTimestamp();
            }
            HasLoaded = true;
            IsLoading = false;
            if (currentDue || historyDue) await NotifyChangedAsync();
        }
        finally { _loadGate.Release(); }
    }

    private void ThrowIfCancelled(CancellationToken token)
    {
        // CancelAsync marks the lifetime immediately; linked-token callbacks may complete later.
        _stopToken.ThrowIfCancellationRequested();
        token.ThrowIfCancellationRequested();
    }

    private void Apply<T>(string key, DashboardSection<T> section, Action<T?> accept)
    {
        if (section.Succeeded) { accept(section.Value); SetError(key, null); }
        else SetError(key, section.Error);
    }

    private void SetError(string key, string? error)
    {
        // Readers can render from the ticker while a load completes; never mutate a published map.
        var updated = new Dictionary<string, string>(_errors, StringComparer.Ordinal);
        if (error is null) updated.Remove(key);
        else updated[key] = error;
        _errors = updated;
    }

    private Task NotifyChangedAsync() => !_stopToken.IsCancellationRequested && Changed is { } changed ? changed() : Task.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        await _stopping.CancelAsync();
        if (_loop is not null) await _loop;
        if (_periodicLoad is not null)
        {
            try { await _periodicLoad; }
            catch (OperationCanceledException) when (_stopToken.IsCancellationRequested) { }
        }
        await _loadGate.WaitAsync();
        _loadGate.Release();
        Changed = null;
        _stopping.Dispose();
        _loadGate.Dispose();
    }
}
