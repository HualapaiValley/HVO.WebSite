using HVO.Edge.Hosting.Telemetry;
using HVO.Hardware.VictronSmartShunt.Configuration;
using HVO.Hardware.VictronSmartShunt.HomeAssistant;
using HVO.Hardware.VictronSmartShunt.Outbox;
using HVO.Hardware.VictronSmartShunt.SmartShunt;
using Microsoft.Extensions.Options;

namespace HVO.Hardware.VictronSmartShunt.Workers;

public sealed class SmartShuntWorker(
    IServiceScopeFactory scopeFactory,
    ISmartShuntSessionState session,
    ISmartShuntHomeAssistantProjection homeAssistant,
    IOptions<SmartShuntOptions> options,
    GatewayTelemetry telemetry,
    TimeProvider timeProvider,
    ILogger<SmartShuntWorker> logger) : BackgroundService
{
    private readonly SmartShuntOptions options = options.Value;
    private long lastSnapshotAtTicks;
    private long lastOutboxWriteAtTicks;
    private volatile SmartShuntLiveSample? lastSample;
    private volatile string? lastError;

    public SmartShuntLiveSample? LastSample => lastSample;
    public string? LastError => lastError ?? session.LastError;
    public bool IsConnected => session.IsConnected;
    public DateTime? LastSnapshotAtUtc => ReadUtc(lastSnapshotAtTicks);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunIterationAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                lastError = exception.GetType().Name;
                logger.LogWarning(exception, "SmartShunt observation processing failed");
            }
            await Task.Delay(TimeSpan.FromSeconds(options.SampleIntervalSeconds), timeProvider, stoppingToken);
        }
    }

    internal async Task RunIterationAsync(CancellationToken cancellationToken)
    {
        if (!await PollOnceAsync(cancellationToken))
            homeAssistant.PublishUnavailable(timeProvider.GetUtcNow().UtcDateTime);
    }

    internal async Task<bool> PollOnceAsync(CancellationToken cancellationToken)
    {
        var sample = session.CurrentSample;
        if (!session.IsConnected || sample is null)
            return false;
        var recordedAt = sample.RecordedAtUtc == default
            ? timeProvider.GetUtcNow().UtcDateTime
            : sample.RecordedAtUtc.ToUniversalTime();
        if (timeProvider.GetUtcNow().UtcDateTime - recordedAt > TimeSpan.FromSeconds(options.SampleStaleAfterSeconds))
        {
            return false;
        }
        var normalized = new SmartShuntLiveSample
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
            PublicSessionActive = true,
        };
        lastSample = normalized;
        Volatile.Write(ref lastSnapshotAtTicks, recordedAt.Ticks);
        lastError = null;
        homeAssistant.Publish(normalized);

        var lastWrite = ReadUtc(lastOutboxWriteAtTicks);
        if (!lastWrite.HasValue || recordedAt - lastWrite.Value >= TimeSpan.FromSeconds(options.SnapshotIntervalSeconds))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var bundle = new HVO.Edge.Contracts.PowerSystem.SmartShuntObservationPayload(
                    SmartShuntPowerMapper.MapSummary(normalized, options),
                    SmartShuntPowerMapper.MapDetail(normalized, options));
                await scope.ServiceProvider.GetRequiredService<ISmartShuntOutboxWriter>()
                    .EnqueueAsync(bundle, cancellationToken);
                // Both a new insert and an idempotent duplicate mean this observation is durable.
                Volatile.Write(ref lastOutboxWriteAtTicks, recordedAt.Ticks);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                lastError = "outbox";
                logger.LogWarning(exception, "SmartShunt outbox enqueue failed; the same observation remains eligible for retry");
            }
        }
        telemetry.RecordPoll(true, 0, options.SourceId, options.DeviceId, "smartshunt");
        return true;
    }

    private static DateTime? ReadUtc(long ticks)
    {
        var value = Volatile.Read(ref ticks);
        return value == 0 ? null : new DateTime(value, DateTimeKind.Utc);
    }
}
