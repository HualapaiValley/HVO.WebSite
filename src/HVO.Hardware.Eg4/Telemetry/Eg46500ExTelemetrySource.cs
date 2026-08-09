using System.Collections.Concurrent;
using HVO.Edge.Contracts.PowerSystem;
using HVO.Hardware.Eg4.Configuration;
using HVO.Hardware.Eg4.Protocol;

namespace HVO.Hardware.Eg4.Telemetry;

public sealed class Eg46500ExTelemetrySource(
    IEg46500ExInquiryTransportFactory transportFactory,
    TimeProvider timeProvider) : IEg4TelemetrySource, IAsyncDisposable
{
    private static readonly HashSet<(string Main, string Secondary)> SupportedFirmware =
    [
        ("VERFW:00079.02", "VERFW:00061.00"),
        ("VERFW:00079.71", "VERFW:00061.13"),
    ];

    private readonly ConcurrentDictionary<string, PortState> _ports = new(StringComparer.Ordinal);
    private readonly object _lifecycleLock = new();
    private TaskCompletionSource _idle = CompletedSource();
    private Task? _disposeTask;
    private int _activeOperations;
    private bool _disposing;

    public bool Supports(Eg4DeviceType deviceType) => deviceType == Eg4DeviceType.Inverter6500Ex;

    public async ValueTask<PowerBatteryObservation> ReadAsync(
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
                return await ReadWithReconnectAsync(state, device, cancellationToken);
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

    public ValueTask DisposeAsync()
    {
        lock (_lifecycleLock)
        {
            _disposeTask ??= DisposeCoreAsync();
            return new ValueTask(_disposeTask);
        }
    }

    private async ValueTask<PowerBatteryObservation> ReadWithReconnectAsync(
        PortState state,
        Eg4DeviceOptions device,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                state.Transport ??= transportFactory.Create(state.Port);
                if (!state.IdentityValidated)
                {
                    await ValidateIdentityAsync(state.Transport, cancellationToken);
                    state.IdentityValidated = true;
                }

                var frame = await state.Transport.ExchangeAsync(Eg46500ExInquiry.GeneralStatus, cancellationToken);
                var status = Eg46500ExPi30Protocol.DecodeGeneralStatus(frame);
                var observedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
                var currentA = status.DischargingCurrentA - status.ChargingCurrentA;
                return new PowerBatteryObservation(
                    device.SourceId,
                    device.DeviceId,
                    PowerMetricSource.Eg46500Ex,
                    PowerMeasurementRole.InverterBranch,
                    "inverter-battery-branch",
                    observedAtUtc,
                    status.VoltageV,
                    currentA,
                    status.VoltageV * currentA,
                    status.ReportedStateOfChargePercent,
                    PowerObservationProvenance.Derived,
                    "PI30 voltage/SOC direct; current=discharge-charge; power=voltage*current",
                    [new PowerObservationInput(device.SourceId, observedAtUtc, device.DeviceId)]);
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

    private static async ValueTask ValidateIdentityAsync(
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
        state.IdentityValidated = false;
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
        public bool IdentityValidated { get; set; }
    }
}
