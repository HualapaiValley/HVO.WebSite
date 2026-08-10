using HVO.Hardware.Eg4.Configuration;

namespace HVO.Hardware.Eg4.Telemetry;

public sealed class Eg4TelemetrySourceRouter(IEnumerable<IEg4DeviceTelemetrySource> sources) : IEg4TelemetrySource
{
    private readonly IReadOnlyList<IEg4DeviceTelemetrySource> _sources = sources.ToArray();

    public bool Supports(Eg4DeviceType deviceType) => _sources.Any(source => source.Supports(deviceType));

    public ValueTask<Eg4TelemetrySample> ReadAsync(Eg4DeviceOptions device, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(device);
        var source = _sources.SingleOrDefault(candidate => candidate.Supports(device.Type))
            ?? throw new InvalidOperationException($"No EG4 telemetry source supports device type '{device.Type}'.");
        return source.ReadAsync(device, cancellationToken);
    }
}

public interface IEg4DeviceTelemetrySource : IEg4TelemetrySource;
