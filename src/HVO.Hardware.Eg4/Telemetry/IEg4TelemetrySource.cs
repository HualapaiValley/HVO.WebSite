using HVO.Edge.Contracts.PowerSystem;
using HVO.Hardware.Eg4.Configuration;

namespace HVO.Hardware.Eg4.Telemetry;

public interface IEg4TelemetrySource
{
    bool Supports(Eg4DeviceType deviceType);
    ValueTask<Eg4TelemetrySample> ReadAsync(Eg4DeviceOptions device, CancellationToken cancellationToken);
}

public sealed record Eg4TelemetrySample(
    bool IsAvailable,
    PowerBatteryObservation? BatteryObservation = null,
    PowerMpptDetailPayload? MpptDetail = null,
    PowerInverterDetailPayload? InverterDetail = null,
    PowerEnergyPayload? Energy = null,
    string? UnavailableReason = null)
{
    public static Eg4TelemetrySample Available(
        PowerBatteryObservation observation,
        PowerMpptDetailPayload? mpptDetail = null,
        PowerInverterDetailPayload? inverterDetail = null,
        PowerEnergyPayload? energy = null) =>
        new(true, observation, mpptDetail, inverterDetail, energy);

    public static Eg4TelemetrySample Unavailable(string reason) => new(false, UnavailableReason: reason);
}
