using HVO.Edge.Contracts.PowerSystem;
using HVO.Hardware.Eg4.Configuration;

namespace HVO.Hardware.Eg4.Telemetry;

public interface IEg4TelemetrySource
{
    bool Supports(Eg4DeviceType deviceType);
    ValueTask<PowerBatteryObservation> ReadAsync(Eg4DeviceOptions device, CancellationToken cancellationToken);
}
