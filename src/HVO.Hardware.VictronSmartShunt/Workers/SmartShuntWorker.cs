using System.Diagnostics;
using HVO.Hardware.VictronSmartShunt.Configuration;
using HVO.Hardware.VictronSmartShunt.Outbox;
using HVO.Hardware.VictronSmartShunt.SmartShunt;
using HVO.Hardware.VictronSmartShunt.Telemetry;
using Microsoft.Extensions.Options;

namespace HVO.Hardware.VictronSmartShunt.Workers;

public sealed class SmartShuntWorker : BackgroundService
{
    private static readonly TimeSpan MaxOverlayAge = TimeSpan.FromMinutes(30);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ISmartShuntSessionState _sessionState;
    private readonly ISmartShuntPrivateInfoSource _privateInfoSource;
    private readonly SmartShuntOptions _options;
    private readonly ILogger<SmartShuntWorker> _logger;
    private readonly SmartShuntTelemetry _telemetry;
    private readonly object _historyLock = new();
    private readonly Queue<SmartShuntHistoryPoint> _history = new();

    private volatile string? _lastError;
    private volatile SmartShuntDeviceSnapshot? _lastSnapshot;
    private volatile SmartShuntDeviceInfo? _privateInfo = new();
    private long _lastSnapshotAtTicks;
    private DateTime _lastOutboxWriteAtUtc;
    private DateTime _lastPrivateRefreshAtUtc;

    public string? LastError => _lastError;
    public SmartShuntDeviceSnapshot? LastSnapshot => _lastSnapshot;
    public SmartShuntDeviceInfo? PrivateInfo => _privateInfo;
    public IReadOnlyList<SmartShuntHistoryPoint> History
    {
        get
        {
            lock (_historyLock)
                return _history.ToArray();
        }
    }

    public DateTime? LastSnapshotAt
    {
        get
        {
            var ticks = Volatile.Read(ref _lastSnapshotAtTicks);
            return ticks == 0 ? null : new DateTime(ticks, DateTimeKind.Utc);
        }
    }

    public SmartShuntWorker(
        IServiceScopeFactory scopeFactory,
        ISmartShuntSessionState sessionState,
        ISmartShuntPrivateInfoSource privateInfoSource,
        IOptions<SmartShuntOptions> options,
        ILogger<SmartShuntWorker> logger,
        SmartShuntTelemetry telemetry)
    {
        _scopeFactory = scopeFactory;
        _sessionState = sessionState;
        _privateInfoSource = privateInfoSource;
        _options = options.Value;
        _logger = logger;
        _telemetry = telemetry;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(_options.Address))
        {
            _logger.LogWarning("SmartShunt address is not configured; live collection is disabled.");
            return;
        }

        _logger.LogInformation(
            "SmartShunt worker starting for {Address} on {Adapter}. PublicOnly={PublicOnly} PrivateEnrichment={PrivateEnrichment}",
            _options.Address,
            _options.Adapter,
            _options.PublicOnly,
            _options.EnablePrivateEnrichment);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                _telemetry.RecordPoll(false, 0, "device_error");
                _logger.LogWarning(ex, "SmartShunt poll failed");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_options.SampleIntervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    internal async Task<bool> PollOnceAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var sample = _sessionState.CurrentSample;
        if (sample is null)
        {
            _telemetry.RecordPoll(false, sw.Elapsed.TotalSeconds, "no_sample");
            return false;
        }

        var recordedAt = sample.RecordedAtUtc == default ? DateTime.UtcNow : sample.RecordedAtUtc.ToUniversalTime();
        var normalizedSample = new SmartShuntLiveSample
        {
            RecordedAtUtc = recordedAt,
            StateOfChargePercent = sample.StateOfChargePercent,
            VoltageV = sample.VoltageV,
            CurrentA = sample.CurrentA,
            PowerW = sample.PowerW,
            ConsumedAh = sample.ConsumedAh,
            StarterVoltageV = sample.StarterVoltageV,
            TemperatureC = sample.TemperatureC,
            RemainingMinutes = sample.RemainingMinutes,
            PublicSessionActive = sample.PublicSessionActive,
            PrivateEnrichmentActive = sample.PrivateEnrichmentActive,
            DataPath = sample.DataPath,
        };

        var snapshot = SmartShuntPowerMapper.ToSnapshot(normalizedSample);
        snapshot = SmartShuntPowerMapper.ApplyOverlay(snapshot, _privateInfo?.Overlay, MaxOverlayAge);
        _lastSnapshot = snapshot;
        Volatile.Write(ref _lastSnapshotAtTicks, recordedAt.Ticks);
        _lastError = null;
        _telemetry.RecordPoll(true, sw.Elapsed.TotalSeconds);
        AddHistory(normalizedSample);

        if (_options.EnablePrivateEnrichment && (_lastPrivateRefreshAtUtc == default || recordedAt - _lastPrivateRefreshAtUtc >= TimeSpan.FromSeconds(_options.PrivateRefreshIntervalSeconds)))
        {
            try
            {
                var privateInfo = await _privateInfoSource.TryReadAsync(ct);
                if (privateInfo is not null)
                {
                    _privateInfo = privateInfo;
                    _lastPrivateRefreshAtUtc = recordedAt;
                    if (_lastSnapshot is not null)
                        _lastSnapshot = SmartShuntPowerMapper.ApplyOverlay(_lastSnapshot, privateInfo.Overlay, MaxOverlayAge);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug(ex, "SmartShunt private enrichment refresh failed");
            }
        }

        if (_lastOutboxWriteAtUtc == default || recordedAt - _lastOutboxWriteAtUtc >= TimeSpan.FromSeconds(_options.SnapshotIntervalSeconds))
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var writer = scope.ServiceProvider.GetRequiredService<PowerOutboxWriter>();
            var payload = SmartShuntPowerMapper.MapLiveSample(normalizedSample, _options, _privateInfo?.Overlay, MaxOverlayAge);
            var inserted = await writer.EnqueueAsync(payload, ct);
            if (inserted)
            {
                _lastOutboxWriteAtUtc = recordedAt;
                _logger.LogDebug(
                    "Queued SmartShunt snapshot for {SourceId} at {RecordedAt:O}",
                    payload.SourceId,
                    recordedAt);
            }
        }

        return true;
    }

    private void AddHistory(SmartShuntLiveSample sample)
    {
        lock (_historyLock)
        {
            _history.Enqueue(new SmartShuntHistoryPoint
            {
                RecordedAtUtc = sample.RecordedAtUtc,
                StateOfChargePercent = sample.StateOfChargePercent,
                VoltageV = sample.VoltageV,
                CurrentA = sample.CurrentA,
                PowerW = sample.PowerW,
                ConsumedAh = sample.ConsumedAh,
            });

            while (_history.Count > _options.HistoryCapacity)
                _history.Dequeue();
        }
    }
}
