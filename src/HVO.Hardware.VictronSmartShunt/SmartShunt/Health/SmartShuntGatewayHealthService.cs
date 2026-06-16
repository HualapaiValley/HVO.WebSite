using HVO.Edge.Contracts;
using HVO.Edge.Outbox;
using HVO.Hardware.VictronSmartShunt.Configuration;
using HVO.Hardware.VictronSmartShunt.Outbox;
using HVO.Hardware.VictronSmartShunt.Workers;
using Microsoft.Extensions.Options;

namespace HVO.Hardware.VictronSmartShunt.SmartShunt.Health;

public sealed class SmartShuntGatewayHealthService(
    SmartShuntWorker worker,
    PowerApiForwarder forwarder,
    IOptions<SmartShuntOptions> optionsAccessor) : ISmartShuntGatewayHealthSnapshotProvider
{
    private readonly SmartShuntWorker _worker = worker;
    private readonly PowerApiForwarder _forwarder = forwarder;
    private readonly SmartShuntOptions _options = optionsAccessor.Value;

    public SmartShuntGatewayHealthSnapshot GetSnapshot(DateTime? nowUtc = null) => Evaluate(
        _options,
        _worker.LastSnapshotAt,
        _worker.LastError,
        _worker.LastSnapshot,
        _forwarder.PendingCount,
        _forwarder.FailedCount,
        _forwarder.LastError,
        nowUtc ?? DateTime.UtcNow);

    public static SmartShuntGatewayHealthSnapshot Evaluate(
        SmartShuntOptions options,
        DateTime? lastSnapshotAtUtc,
        string? lastError,
        SmartShuntDeviceSnapshot? snapshot,
        int pendingOutboxCount,
        int failedOutboxCount,
        string? outboxError,
        DateTime now)
    {
        var alerts = new List<SmartShuntGatewayHealthAlert>();

        if (string.IsNullOrWhiteSpace(options.Address))
        {
            alerts.Add(Alert("address-missing", SmartShuntGatewayHealthSeverity.Warning, "SmartShunt address is not configured."));
        }
        else if (!string.IsNullOrWhiteSpace(lastError))
        {
            alerts.Add(Alert("poll-error", SmartShuntGatewayHealthSeverity.Critical, $"SmartShunt worker error: {lastError}"));
        }
        else if (lastSnapshotAtUtc is null)
        {
            alerts.Add(Alert("waiting", SmartShuntGatewayHealthSeverity.Warning, "Waiting for first SmartShunt sample."));
        }
        else if (now - lastSnapshotAtUtc.Value > TimeSpan.FromSeconds(options.SampleStaleAfterSeconds))
        {
            alerts.Add(Alert("stale", SmartShuntGatewayHealthSeverity.Critical, "Latest SmartShunt sample is stale."));
        }

        alerts.AddRange(BuildOutboxAlerts(options, pendingOutboxCount, failedOutboxCount, outboxError));

        var batterySocLooksInvalid = snapshot is not null
            && snapshot.StateOfChargePercent.HasValue
            && snapshot.StateOfChargePercent.Value == 0
            && snapshot.VoltageV.HasValue
            && snapshot.VoltageV.Value > 1
            && snapshot.CurrentA.HasValue;

        if (snapshot?.StateOfChargePercent is { } soc && !batterySocLooksInvalid)
        {
            if (soc <= options.CriticalBatteryPercent)
                alerts.Add(Alert("battery-critical", SmartShuntGatewayHealthSeverity.Critical, $"Battery state of charge is critical at {soc:0}%"));
            else if (soc <= options.LowBatteryWarningPercent)
                alerts.Add(Alert("battery-low", SmartShuntGatewayHealthSeverity.Warning, $"Battery state of charge is low at {soc:0}%"));
        }

        return new SmartShuntGatewayHealthSnapshot
        {
            EvaluatedAtUtc = now,
            State = alerts.Any(a => a.Severity == SmartShuntGatewayHealthSeverity.Critical)
                ? "critical"
                : alerts.Any(a => a.Severity == SmartShuntGatewayHealthSeverity.Warning) ? "warning" : "healthy",
            Alerts = alerts,
        };
    }

    private static SmartShuntGatewayHealthAlert Alert(string code, SmartShuntGatewayHealthSeverity severity, string message)
        => new()
        {
            Code = code,
            Severity = severity,
            Message = message,
        };

    private static IReadOnlyList<SmartShuntGatewayHealthAlert> BuildOutboxAlerts(
        SmartShuntOptions options,
        int pendingOutboxCount,
        int failedOutboxCount,
        string? outboxError)
    {
        var evaluation = EdgeOutboxHealthEvaluator.Evaluate(
            new EdgeOutboxObservation(
                PendingCount: pendingOutboxCount,
                FailedCount: failedOutboxCount,
                LastError: outboxError,
                PermanentFailedCount: failedOutboxCount),
            new EdgeOutboxHealthOptions(
                PendingWarningCount: options.OutboxPendingWarningCount,
                FailedCriticalCount: options.OutboxFailedCriticalCount));

        return evaluation.Alerts
            .Select(MapOutboxAlert)
            .ToArray();
    }

    private static SmartShuntGatewayHealthAlert MapOutboxAlert(GatewayHealthAlert alert)
    {
        return Alert(
            MapOutboxAlertCode(alert.Code),
            alert.Severity switch
            {
                GatewayAlertSeverity.Critical => SmartShuntGatewayHealthSeverity.Critical,
                GatewayAlertSeverity.Warning => SmartShuntGatewayHealthSeverity.Warning,
                _ => SmartShuntGatewayHealthSeverity.Info,
            },
            alert.Message);
    }

    private static string MapOutboxAlertCode(string code) => code switch
    {
        "outbox-current-sync-failing" => "outbox-error",
        "outbox-pending-backlog" => "outbox-backlog",
        "outbox-historical-failures" or "outbox-historical-failures-over-threshold"
            or "outbox-permanent-failures" or "outbox-retry-exhausted" => "outbox-failed",
        _ => code,
    };
}
