using System.Collections.Concurrent;
using HVO.Hardware.Eg4.Protocol;

namespace HVO.Hardware.Eg4.Simulation;

public sealed record ScriptedRegisterStep(
    Eg4ReadRegistersRequest ExpectedRequest,
    IReadOnlyList<ushort>? Registers = null,
    TimeSpan Delay = default,
    Eg4TransportFailureKind? Failure = null,
    bool Timeout = false);

public sealed record CapturedEg4RegisterRequest(
    long Sequence,
    string Port,
    Eg4ReadRegistersRequest Request,
    DateTimeOffset CapturedAtUtc);

public sealed class ScriptedEg4RegisterTransportFactory(TimeProvider timeProvider) : IEg4RegisterTransportFactory
{
    private readonly ConcurrentDictionary<(string Port, byte UnitId), ConcurrentQueue<ScriptedRegisterStep>> _scripts = new();
    private readonly ConcurrentQueue<CapturedEg4RegisterRequest> _captured = new();
    private long _sequence;

    public IReadOnlyList<CapturedEg4RegisterRequest> CapturedRequests => [.. _captured.OrderBy(item => item.Sequence)];

    public void Enqueue(string port, byte unitId, params ScriptedRegisterStep[] steps)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(port);
        ArgumentNullException.ThrowIfNull(steps);
        if (steps.Any(step => step is null))
            throw new ArgumentException("A register script cannot contain null steps.", nameof(steps));
        var queue = _scripts.GetOrAdd((port, unitId), _ => new ConcurrentQueue<ScriptedRegisterStep>());
        foreach (var step in steps) queue.Enqueue(step);
    }

    public IEg4RegisterTransport Create(string port) => new Transport(this, port);

    private async ValueTask<Eg4ReadRegistersResponse> ReadAsync(
        string port,
        Eg4ReadRegistersRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        _captured.Enqueue(new CapturedEg4RegisterRequest(
            Interlocked.Increment(ref _sequence), port, request, timeProvider.GetUtcNow()));
        if (!_scripts.TryGetValue((port, request.UnitId), out var queue) || !queue.TryDequeue(out var step))
            throw new InvalidOperationException($"No scripted EG4 response remains for {port} unit {request.UnitId}.");
        if (step.ExpectedRequest != request)
            throw new InvalidOperationException($"Expected {step.ExpectedRequest}, received {request}.");
        if (step.Delay > TimeSpan.Zero)
            await Task.Delay(step.Delay, timeProvider, cancellationToken);
        if (step.Timeout)
            throw new TimeoutException($"Scripted timeout for {port} unit {request.UnitId}.");
        if (step.Failure is { } failure)
            throw new Eg4TransportException(failure, $"Scripted {failure} failure for {port} unit {request.UnitId}.");

        var registers = step.Registers?.ToArray() ?? [];
        if (registers.Length != request.RegisterCount)
            throw new Eg4TransportException(Eg4TransportFailureKind.MalformedFrame, "Scripted register count does not match the request.");
        return new Eg4ReadRegistersResponse(registers);
    }

    private sealed class Transport(ScriptedEg4RegisterTransportFactory owner, string port) : IEg4RegisterTransport
    {
        public ValueTask<Eg4ReadRegistersResponse> ReadRegistersAsync(
            Eg4ReadRegistersRequest request,
            CancellationToken cancellationToken) => owner.ReadAsync(port, request, cancellationToken);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
