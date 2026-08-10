using System.Collections.Concurrent;
using HVO.Edge.Contracts.PowerSystem;
using HVO.Hardware.Eg4.Configuration;
using HVO.Hardware.Eg4.Protocol;
using HVO.Hardware.Eg4.Telemetry;

namespace HVO.Hardware.Eg4.Simulation;

public sealed record Eg4SimulatedTelemetry(
    double? VoltageV = null,
    double? CurrentA = null,
    double? PowerW = null,
    double? StateOfChargePercent = null,
    string? Confidence = "simulated",
    bool IncludeRichDetail = false);

public sealed record Eg4SimulationStep(
    Eg4SimulatedTelemetry? Telemetry = null,
    TimeSpan Delay = default,
    Eg4TransportFailureKind? Failure = null,
    bool Timeout = false);

public sealed record Eg4FleetSimulationResult(
    Eg4DeviceOptions Device,
    PowerBatteryObservation? Observation,
    Exception? Error);

public sealed class Eg4FleetSimulator(TimeProvider timeProvider) : IEg4TelemetrySource
{
    private readonly ConcurrentDictionary<string, ConcurrentQueue<Eg4SimulationStep>> _scripts =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Eg4SimulatedTelemetry> _current =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _sourceGates =
        new(StringComparer.OrdinalIgnoreCase);

    public bool Supports(Eg4DeviceType deviceType) =>
        deviceType is Eg4DeviceType.Inverter6500Ex or Eg4DeviceType.ChargeControllerMppt10048Hv;

    public async ValueTask SetScriptAsync(
        string sourceId,
        IEnumerable<Eg4SimulationStep> steps,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        ArgumentNullException.ThrowIfNull(steps);
        var script = steps.ToArray();
        if (script.Any(step => step is null))
            throw new ArgumentException("A simulation script cannot contain null steps.", nameof(steps));
        var gate = _sourceGates.GetOrAdd(sourceId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try { _scripts[sourceId] = new ConcurrentQueue<Eg4SimulationStep>(script); }
        finally { gate.Release(); }
    }

    public async ValueTask SeedIfUnscriptedAsync(
        string sourceId,
        Eg4SimulatedTelemetry telemetry,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        ArgumentNullException.ThrowIfNull(telemetry);
        var gate = _sourceGates.GetOrAdd(sourceId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (!_scripts.ContainsKey(sourceId) && !_current.ContainsKey(sourceId))
                _scripts[sourceId] = new ConcurrentQueue<Eg4SimulationStep>([new Eg4SimulationStep(telemetry)]);
        }
        finally { gate.Release(); }
    }

    public async ValueTask<Eg4TelemetrySample> ReadAsync(
        Eg4DeviceOptions device,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(device);
        if (!device.Enabled) throw new InvalidOperationException($"Device '{device.SourceId}' is disabled.");
        if (!Supports(device.Type)) throw new InvalidOperationException($"Device type '{device.Type}' is unsupported.");
        var gate = _sourceGates.GetOrAdd(device.SourceId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var step = _scripts.TryGetValue(device.SourceId, out var queue) && queue.TryDequeue(out var scripted)
                ? scripted
                : new Eg4SimulationStep(_current.GetValueOrDefault(device.SourceId, new Eg4SimulatedTelemetry()));
            if (step.Delay > TimeSpan.Zero)
                await Task.Delay(step.Delay, timeProvider, cancellationToken);
            if (step.Timeout) throw new TimeoutException($"Scripted timeout for '{device.SourceId}'.");
            if (step.Failure is { } failure)
                throw new Eg4TransportException(failure, $"Scripted {failure} failure for '{device.SourceId}'.");
            var telemetry = step.Telemetry ?? _current.GetValueOrDefault(device.SourceId, new Eg4SimulatedTelemetry());
            _current[device.SourceId] = telemetry;
            var observedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
            return Eg4TelemetrySample.Available(
                CreateObservation(device, telemetry, observedAtUtc),
                telemetry.IncludeRichDetail ? CreateMpptDetail(device, telemetry, observedAtUtc) : null,
                telemetry.IncludeRichDetail && device.Type == Eg4DeviceType.Inverter6500Ex
                    ? CreateInverterDetail(device, telemetry, observedAtUtc)
                    : null);
        }
        finally { gate.Release(); }
    }

    public async Task<IReadOnlyList<Eg4FleetSimulationResult>> PollFleetAsync(
        IEnumerable<Eg4DeviceOptions> devices,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(devices);
        var fleet = devices.ToArray();
        if (fleet.Any(device => device is null))
            throw new ArgumentException("A fleet cannot contain null devices.", nameof(devices));
        var tasks = fleet.Where(device => device.Enabled).Select(async device =>
        {
            try { return new Eg4FleetSimulationResult(device, (await ReadAsync(device, cancellationToken)).BatteryObservation, null); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex) { return new Eg4FleetSimulationResult(device, null, ex); }
        });
        return await Task.WhenAll(tasks);
    }

    private static PowerBatteryObservation CreateObservation(
        Eg4DeviceOptions device,
        Eg4SimulatedTelemetry telemetry,
        DateTime observedAtUtc)
    {
        var inverter = device.Type == Eg4DeviceType.Inverter6500Ex;
        return new PowerBatteryObservation(
            device.SourceId,
            device.DeviceId,
            inverter ? PowerMetricSource.Eg46500Ex : PowerMetricSource.Eg4Mppt10048Hv,
            inverter ? PowerMeasurementRole.InverterBranch : PowerMeasurementRole.ChargeControllerBranch,
            device.Alias,
            observedAtUtc,
            telemetry.VoltageV,
            telemetry.CurrentA,
            telemetry.PowerW,
            telemetry.StateOfChargePercent,
            PowerObservationProvenance.Direct,
            telemetry.Confidence);
    }

    private static PowerMpptDetailPayload CreateMpptDetail(
        Eg4DeviceOptions device,
        Eg4SimulatedTelemetry telemetry,
        DateTime observedAtUtc)
    {
        var inverter = device.Type == Eg4DeviceType.Inverter6500Ex;
        var trackers = inverter
            ? new[]
            {
                new PowerMpptTrackerDetail { TrackerId = "mppt-1", Name = "Inverter MPPT 1", PowerW = 1250, VoltageV = 320, CurrentA = 3.9, Provenance = PowerObservationProvenance.Direct },
                new PowerMpptTrackerDetail { TrackerId = "mppt-2", Name = "Inverter MPPT 2", PowerW = 1100, VoltageV = 305, CurrentA = 3.6, Provenance = PowerObservationProvenance.Direct },
            }
            : [new PowerMpptTrackerDetail { TrackerId = "mppt-1", Name = "External MPPT", PowerW = 900, VoltageV = 360, CurrentA = 2.5, Provenance = PowerObservationProvenance.Direct }];
        return new PowerMpptDetailPayload
        {
            SourceId = device.SourceId,
            SourceSystem = inverter ? "eg4-6500ex" : "eg4-mppt100-48hv",
            DeviceId = device.DeviceId,
            RecordedAtUtc = observedAtUtc,
            Trackers = trackers,
            BatteryOutput = new PowerMpptBatteryOutputDetail
            {
                VoltageV = telemetry.VoltageV,
                CurrentA = telemetry.CurrentA,
                PowerW = telemetry.PowerW,
            },
        };
    }

    private static PowerInverterDetailPayload CreateInverterDetail(
        Eg4DeviceOptions device,
        Eg4SimulatedTelemetry telemetry,
        DateTime observedAtUtc) => new()
    {
        SourceId = device.SourceId,
        SourceSystem = "eg4-6500ex",
        DeviceId = device.DeviceId,
        RecordedAtUtc = observedAtUtc,
        PvStrings =
        [
            new PowerPvStringDetail { StringId = "mppt-1", PowerW = 1250, VoltageV = 320, CurrentA = 3.9 },
            new PowerPvStringDetail { StringId = "mppt-2", PowerW = 1100, VoltageV = 305, CurrentA = 3.6 },
        ],
        Ac = new PowerInverterAcDetail { InputVoltageV = 0, InputFrequencyHz = 0, OutputVoltageV = 120.1, OutputFrequencyHz = 60 },
        Load = new PowerInverterLoadDetail { LoadPowerW = 850, LoadApparentPowerVa = 920 },
        Battery = new PowerInverterBatteryDetail { VoltageV = telemetry.VoltageV, CurrentA = telemetry.CurrentA, PowerW = telemetry.PowerW },
        Operating = new PowerInverterOperatingDetail { Mode = "Battery", LoadPercentage = 14, FaultCode = "00" },
        Temperatures = [new PowerInverterTemperatureDetail { TemperatureId = "inverter", Name = "Inverter", TemperatureC = 42 }],
        Statuses = [new PowerInverterStatusDetail { Key = "simulation", Value = "normal" }],
    };
}
