using HVO.Edge.Contracts.PowerSystem;
using HVO.Hardware.Eg4.Configuration;

namespace HVO.Hardware.Eg4.Diagnostics;

internal sealed record Eg4DeviceRuntimeSnapshot(
    Eg4DeviceOptions Device,
    DateTime? LastAttemptAtUtc,
    PowerBatteryObservation? LastObservation,
    string? FailureCategory);

internal sealed class Eg4RuntimeState
{
    private readonly object sync = new();
    private readonly Dictionary<string, Eg4DeviceRuntimeSnapshot> devices;

    public Eg4RuntimeState(Microsoft.Extensions.Options.IOptions<Eg4Options> options)
    {
        devices = (options.Value.Devices ?? [])
            .Where(static device => device.Enabled)
            .ToDictionary(
                static device => device.SourceId,
                static device => new Eg4DeviceRuntimeSnapshot(device, null, null, null),
                StringComparer.Ordinal);
    }

    public void RecordSuccess(Eg4DeviceOptions device, PowerBatteryObservation observation, DateTime attemptedAtUtc)
    {
        lock (sync)
            devices[device.SourceId] = new(device, attemptedAtUtc, observation, null);
    }

    public void RecordFailure(Eg4DeviceOptions device, DateTime attemptedAtUtc, string category)
    {
        lock (sync)
        {
            devices.TryGetValue(device.SourceId, out var previous);
            devices[device.SourceId] = new(device, attemptedAtUtc, previous?.LastObservation, category);
        }
    }

    public IReadOnlyList<Eg4DeviceRuntimeSnapshot> Snapshot()
    {
        lock (sync)
            return devices.Values.OrderBy(static device => device.Device.SourceId, StringComparer.Ordinal).ToArray();
    }
}
