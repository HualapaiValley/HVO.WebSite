namespace HVO.Hardware.JkBms.Bms;

/// <summary>
/// Handles alarm conditions reported by a BMS device.
/// Implement this interface to add notification integrations (email, MQTT, etc.).
/// </summary>
public interface IBmsAlarmHandler
{
    /// <summary>
    /// Called by the poller worker whenever a reading has one or more active alarms.
    /// </summary>
    Task HandleAsync(BmsDeviceReading reading, CancellationToken ct);
}

/// <summary>
/// No-op alarm handler. Registered by default — replace with a real implementation
/// when alarm notifications are needed.
/// </summary>
public sealed class NullAlarmHandler : IBmsAlarmHandler
{
    public Task HandleAsync(BmsDeviceReading reading, CancellationToken ct) => Task.CompletedTask;
}
