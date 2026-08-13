using System.Collections.Concurrent;
using System.Globalization;
using HVO.Edge.Contracts.PowerSystem;
using HVO.Hardware.Eg4.Configuration;
using HVO.Hardware.Eg4.Protocol;

namespace HVO.Hardware.Eg4.Telemetry;

public sealed class Eg46500ExTelemetrySource(
    IEg46500ExInquiryTransportFactory transportFactory,
    TimeProvider timeProvider) : IEg4DeviceTelemetrySource, IAsyncDisposable
{
    private static readonly TimeSpan EnergyRefreshInterval = TimeSpan.FromMinutes(5);
    private static readonly HashSet<(string Main, string Secondary)> SupportedFirmware =
    [
        ("VERFW:00079.02", "VERFW:00061.00"),
        ("VERFW:00079.71", "VERFW:00061.13"),
        ("VERFW:00079.72", "VERFW:00061.13"),
    ];

    private readonly ConcurrentDictionary<string, PortState> _ports = new(StringComparer.Ordinal);
    private readonly object _lifecycleLock = new();
    private TaskCompletionSource _idle = CompletedSource();
    private Task? _disposeTask;
    private int _activeOperations;
    private bool _disposing;

    public bool Supports(Eg4DeviceType deviceType) => deviceType == Eg4DeviceType.Inverter6500Ex;

    public async ValueTask<Eg4TelemetrySample> ReadSampleAsync(
        Eg4DeviceOptions device,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(device);
        if (!device.Enabled) throw new InvalidOperationException($"Device '{device.SourceId}' is disabled.");
        if (!Supports(device.Type)) throw new InvalidOperationException($"Device type '{device.Type}' is unsupported by the 6500EX adapter.");
        ArgumentException.ThrowIfNullOrWhiteSpace(device.Port);
        cancellationToken.ThrowIfCancellationRequested();

        BeginOperation();
        try
        {
            var state = _ports.GetOrAdd(device.Port, static port => new PortState(port));
            await state.Gate.WaitAsync(cancellationToken);
            try
            {
                try
                {
                    return await ReadWithReconnectAsync(state, device, cancellationToken);
                }
                catch (Eg4TransportException exception) when (exception.Kind == Eg4TransportFailureKind.Timeout)
                {
                    return Eg4TelemetrySample.Unavailable("timeout");
                }
            }
            finally
            {
                state.Gate.Release();
            }
        }
        finally
        {
            EndOperation();
        }
    }

    async ValueTask<Eg4TelemetrySample> IEg4TelemetrySource.ReadAsync(
        Eg4DeviceOptions device,
        CancellationToken cancellationToken) => await ReadSampleAsync(device, cancellationToken);

    public async ValueTask<PowerBatteryObservation> ReadAsync(
        Eg4DeviceOptions device,
        CancellationToken cancellationToken)
    {
        var sample = await ReadSampleAsync(device, cancellationToken);
        return sample.BatteryObservation ?? throw new Eg4TransportException(
            Eg4TransportFailureKind.Timeout,
            $"6500EX telemetry is unavailable ({sample.UnavailableReason ?? "no observation"}).");
    }

    public ValueTask DisposeAsync()
    {
        lock (_lifecycleLock)
        {
            _disposeTask ??= DisposeCoreAsync();
            return new ValueTask(_disposeTask);
        }
    }

    private async ValueTask<Eg4TelemetrySample> ReadWithReconnectAsync(
        PortState state,
        Eg4DeviceOptions device,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                state.Transport ??= transportFactory.Create(state.Port);
                state.Identity ??= await ValidateIdentityAsync(state.Transport, cancellationToken);

                var status = Eg46500ExPi30Protocol.DecodeGeneralStatus(
                    await state.Transport.ExchangeAsync(Eg46500ExInquiry.GeneralStatus, cancellationToken));
                var pv2 = state.Identity.SupportsDirectPv2
                    ? Eg46500ExPi30Protocol.DecodePv2Status(
                        await state.Transport.ExchangeAsync(Eg46500ExInquiry.Pv2Status, cancellationToken))
                    : null;
                var parallel = Eg46500ExPi30Protocol.DecodeParallelStatus(
                    await state.Transport.ExchangeAsync(Eg46500ExInquiry.ParallelStatus, cancellationToken));
                var extended = Eg46500ExPi30Protocol.DecodeExtendedStatus(
                    await state.Transport.ExchangeAsync(Eg46500ExInquiry.ExtendedStatus, cancellationToken));
                var observedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
                var energy = await ReadEnergyAsync(state, device, observedAtUtc, cancellationToken);
                var currentA = status.BatteryDischargingCurrentA - status.BatteryChargingCurrentA;
                var observation = new PowerBatteryObservation(
                    device.SourceId,
                    device.DeviceId,
                    PowerMetricSource.Eg46500Ex,
                    PowerMeasurementRole.InverterBranch,
                    "inverter-battery-branch",
                    observedAtUtc,
                    status.BatteryVoltageV,
                    currentA,
                    status.BatteryVoltageV * currentA,
                    status.ReportedStateOfChargePercent,
                    PowerObservationProvenance.Derived,
                    "PI30 voltage/SOC direct; current=discharge-charge; power=voltage*current",
                    [new PowerObservationInput(device.SourceId, observedAtUtc, device.DeviceId)]);
                return Eg4TelemetrySample.Available(
                    observation,
                    CreateMpptDetail(device, observedAtUtc, status, parallel, pv2),
                    CreateInverterDetail(device, observedAtUtc, status, parallel, pv2, extended, state.Identity, currentA),
                    energy);
            }
            catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
            {
                var cleanupFailure = await TryResetAsync(state);
                if (cleanupFailure is not null) exception.Data["6500EX cleanup failure"] = cleanupFailure;
                throw;
            }
            catch (Exception exception) when (attempt == 0 && IsTransient(exception))
            {
                var cleanupFailure = await TryResetAsync(state);
                if (cleanupFailure is not null) throw new AggregateException(exception, cleanupFailure);
                await Task.Delay(TimeSpan.FromMilliseconds(500), timeProvider, cancellationToken);
            }
            catch (Exception exception)
            {
                var cleanupFailure = await TryResetAsync(state);
                if (cleanupFailure is not null) throw new AggregateException(exception, cleanupFailure);
                throw;
            }
        }
        throw new InvalidOperationException("The 6500EX retry loop completed without a result.");
    }

    private static PowerMpptDetailPayload CreateMpptDetail(
        Eg4DeviceOptions device,
        DateTime observedAtUtc,
        Eg46500ExGeneralStatus status,
        Eg46500ExParallelStatus parallel,
        Eg46500ExPv2Status? pv2) => new()
    {
        SourceId = device.SourceId,
        SourceSystem = "eg4-6500ex",
        DeviceId = device.DeviceId,
        RecordedAtUtc = observedAtUtc,
        Trackers =
        [
            new PowerMpptTrackerDetail
            {
                TrackerId = "mppt-1",
                Name = "MPPT 1",
                VoltageV = status.Pv1VoltageV,
                CurrentA = status.Pv1CurrentA,
                PowerW = status.Pv1PowerW,
                Provenance = PowerObservationProvenance.Direct,
                Confidence = "direct PI30 QPIGS",
            },
            new PowerMpptTrackerDetail
            {
                TrackerId = "mppt-2",
                Name = "MPPT 2",
                VoltageV = pv2?.VoltageV ?? parallel.Pv2VoltageV,
                CurrentA = pv2?.CurrentA ?? parallel.Pv2CurrentA,
                PowerW = pv2?.PowerW ?? parallel.Pv2VoltageV * parallel.Pv2CurrentA,
                Provenance = pv2 is null ? PowerObservationProvenance.Derived : PowerObservationProvenance.Direct,
                Confidence = pv2 is null
                    ? "low-resolution/coarse integer current from PI30 QPGS0"
                    : "direct PI30 QPIGS2 on firmware 79.72",
            },
        ],
    };

    private static PowerInverterDetailPayload CreateInverterDetail(
        Eg4DeviceOptions device,
        DateTime observedAtUtc,
        Eg46500ExGeneralStatus status,
        Eg46500ExParallelStatus parallel,
        Eg46500ExPv2Status? pv2,
        Eg46500ExExtendedStatus extended,
        Eg46500ExIdentity identity,
        double currentA) => new()
    {
        SourceId = device.SourceId,
        SourceSystem = "eg4-6500ex",
        DeviceId = device.DeviceId,
        RecordedAtUtc = observedAtUtc,
        Ac = new PowerInverterAcDetail
        {
            InputVoltageV = status.AcInputVoltageV,
            InputFrequencyHz = status.AcInputFrequencyHz,
            OutputVoltageV = status.AcOutputVoltageV,
            OutputFrequencyHz = status.AcOutputFrequencyHz,
        },
        Load = new PowerInverterLoadDetail
        {
            LoadPowerW = status.LoadActivePowerW,
            LoadApparentPowerVa = status.LoadApparentPowerVa,
        },
        Battery = new PowerInverterBatteryDetail
        {
            VoltageV = status.BatteryVoltageV,
            CurrentA = currentA,
            PowerW = status.BatteryVoltageV * currentA,
        },
        Operating = new PowerInverterOperatingDetail
        {
            Mode = parallel.OperatingMode,
            FaultCode = parallel.FaultCode,
            LoadPercentage = status.LoadPercentage,
            StatusFlags = $"{status.StatusFlags}/{parallel.StatusFlags}/{status.SecondaryStatusFlags}",
        },
        TemperatureC = status.InverterTemperatureC,
        Temperatures =
        [
            Temperature("scc-pwm", "SCC PWM", extended.SccPwmTemperatureC),
            Temperature("inverter", "Inverter", extended.InverterTemperatureC),
            Temperature("battery-channel", "Battery channel", extended.BatteryChannelTemperatureC),
            Temperature("transformer", "Transformer", extended.TransformerTemperatureC),
        ],
        PvStrings =
        [
            new PowerPvStringDetail { StringId = "mppt-1", VoltageV = status.Pv1VoltageV, CurrentA = status.Pv1CurrentA, PowerW = status.Pv1PowerW },
            new PowerPvStringDetail
            {
                StringId = "mppt-2",
                VoltageV = pv2?.VoltageV ?? parallel.Pv2VoltageV,
                CurrentA = pv2?.CurrentA ?? parallel.Pv2CurrentA,
                PowerW = pv2?.PowerW ?? parallel.Pv2VoltageV * parallel.Pv2CurrentA,
            },
        ],
        Statuses =
        [
            Status("main-firmware", identity.MainFirmware, "QVFW"),
            Status("secondary-firmware", identity.SecondaryFirmware, "QVFW3"),
            Status("charge-stage", extended.ChargeStage, "Q1"),
            Status("fan-locked", extended.FanLocked ? "true" : "false", "Q1"),
            Status("fan-pwm-percent", extended.FanPwmPercent.ToString(CultureInfo.InvariantCulture), "Q1"),
            Status("parallel-role", extended.ParallelRole.ToString(CultureInfo.InvariantCulture), "Q1"),
            Status("parallel-warning-flags", extended.ParallelWarningFlags, "Q1"),
            Status("mppt-1-charge-power-w", extended.Pv1ChargePowerW.ToString(CultureInfo.InvariantCulture), "Q1"),
            Status("output-mode", parallel.OutputMode, "QPGS0"),
            Status("charger-source-priority", parallel.ChargerSourcePriority, "QPGS0"),
            Status("parallel-total-load-active-w", parallel.TotalLoadActivePowerW.ToString(CultureInfo.InvariantCulture), "QPGS0"),
            Status("parallel-total-load-apparent-va", parallel.TotalLoadApparentPowerVa.ToString(CultureInfo.InvariantCulture), "QPGS0"),
            Status("parallel-total-load-percent", parallel.TotalLoadPercentage.ToString(CultureInfo.InvariantCulture), "QPGS0"),
        ],
    };

    private static PowerInverterTemperatureDetail Temperature(string id, string name, double value) => new()
    {
        TemperatureId = id,
        Name = name,
        TemperatureC = value,
    };

    private static PowerInverterStatusDetail Status(string key, string value, string source) => new()
    {
        Key = key,
        Value = value,
        SourceTopic = source,
    };

    private static async ValueTask<Eg46500ExIdentity> ValidateIdentityAsync(
        IEg46500ExInquiryTransport transport,
        CancellationToken cancellationToken)
    {
        var protocol = await ReadPayloadAsync(transport, Eg46500ExInquiry.ProtocolId, cancellationToken);
        var model = await ReadPayloadAsync(transport, Eg46500ExInquiry.ModelName, cancellationToken);
        var generalModel = await ReadPayloadAsync(transport, Eg46500ExInquiry.GeneralModelName, cancellationToken);
        var mainFirmware = await ReadPayloadAsync(transport, Eg46500ExInquiry.MainFirmware, cancellationToken);
        var secondaryFirmware = await ReadPayloadAsync(transport, Eg46500ExInquiry.SecondaryFirmware, cancellationToken);

        if (protocol != "PI30" || model != "MKS2-6500" || generalModel != "045")
            throw new Eg4TransportException(Eg4TransportFailureKind.Protocol, "The connected device is not a validated EG4 6500EX PI30 endpoint.");
        if (!SupportedFirmware.Contains((mainFirmware, secondaryFirmware)))
            throw new Eg4TransportException(Eg4TransportFailureKind.Protocol, $"Unsupported 6500EX firmware layout '{mainFirmware}'/'{secondaryFirmware}'.");
        return new Eg46500ExIdentity(mainFirmware, secondaryFirmware);
    }

    private static async ValueTask<PowerEnergyPayload?> ReadEnergyAsync(
        PortState state,
        Eg4DeviceOptions device,
        DateTime observedAtUtc,
        CancellationToken cancellationToken)
    {
        if (state.LastEnergyReadAtUtc != default
            && observedAtUtc - state.LastEnergyReadAtUtc < EnergyRefreshInterval)
        {
            return state.LastEnergy;
        }

        state.LastEnergyReadAtUtc = observedAtUtc;
        try
        {
            var pvWh = Eg46500ExPi30Protocol.DecodeEnergyWh(
                await state.Transport!.ExchangeAsync(Eg46500ExInquiry.TotalPvEnergy, cancellationToken));
            var loadWh = Eg46500ExPi30Protocol.DecodeEnergyWh(
                await state.Transport.ExchangeAsync(Eg46500ExInquiry.TotalLoadEnergy, cancellationToken));
            state.LastEnergy = new PowerEnergyPayload
            {
                SourceId = device.SourceId,
                SourceSystem = "eg4-6500ex",
                DeviceId = device.DeviceId,
                RecordedAtUtc = observedAtUtc,
                Counters =
                [
                    new PowerEnergyCounter
                    {
                        Key = "pv_energy",
                        Name = "PV energy",
                        ValueKwh = pvWh / 1000d,
                        DeviceId = device.DeviceId,
                        SourceTopic = "QET",
                    },
                    new PowerEnergyCounter
                    {
                        Key = "load_energy",
                        Name = "AC load energy",
                        ValueKwh = loadWh / 1000d,
                        DeviceId = device.DeviceId,
                        SourceTopic = "QLT",
                    },
                ],
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Eg4TransportException)
        {
            // Energy counters are supplementary; normal inverter telemetry remains authoritative and available.
        }

        return state.LastEnergy;
    }

    private static async ValueTask<string> ReadPayloadAsync(
        IEg46500ExInquiryTransport transport,
        Eg46500ExInquiry inquiry,
        CancellationToken cancellationToken) =>
        Eg46500ExPi30Protocol.DecodePayload(await transport.ExchangeAsync(inquiry, cancellationToken));

    private static bool IsTransient(Exception exception) =>
        exception is Eg4TransportException { Kind: Eg4TransportFailureKind.Crc or
            Eg4TransportFailureKind.MalformedFrame or Eg4TransportFailureKind.Disconnected or Eg4TransportFailureKind.Timeout };

    private static async ValueTask ResetAsync(PortState state)
    {
        state.Identity = null;
        if (state.Transport is null) return;
        var transport = state.Transport;
        state.Transport = null;
        await transport.DisposeAsync();
    }

    private static async ValueTask<Exception?> TryResetAsync(PortState state)
    {
        try
        {
            await ResetAsync(state);
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private async Task DisposeCoreAsync()
    {
        lock (_lifecycleLock)
        {
            _disposing = true;
        }
        await _idle.Task;
        List<Exception>? failures = null;
        foreach (var state in _ports.Values)
        {
            try { await ResetAsync(state); }
            catch (Exception exception) { (failures ??= []).Add(exception); }
            finally { state.Gate.Dispose(); }
        }
        if (failures is not null) throw new AggregateException(failures);
    }

    private void BeginOperation()
    {
        lock (_lifecycleLock)
        {
            ObjectDisposedException.ThrowIf(_disposing, this);
            if (_activeOperations++ == 0)
                _idle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    private void EndOperation()
    {
        lock (_lifecycleLock)
        {
            if (--_activeOperations == 0) _idle.TrySetResult();
        }
    }

    private static TaskCompletionSource CompletedSource()
    {
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        source.SetResult();
        return source;
    }

    private sealed class PortState(string port)
    {
        public string Port { get; } = port;
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public IEg46500ExInquiryTransport? Transport { get; set; }
        public Eg46500ExIdentity? Identity { get; set; }
        public DateTime LastEnergyReadAtUtc { get; set; }
        public PowerEnergyPayload? LastEnergy { get; set; }
    }

    private sealed record Eg46500ExIdentity(string MainFirmware, string SecondaryFirmware)
    {
        public bool SupportsDirectPv2 => MainFirmware == "VERFW:00079.72";
    }
}
