using System.Collections.Concurrent;

namespace HVO.Hardware.Eg4.Protocol;

public interface IEg4PortCoordinator
{
    ValueTask<Eg4ReadRegistersResponse> ReadRegistersAsync(
        string port,
        Eg4ReadRegistersRequest request,
        CancellationToken cancellationToken);
}

public sealed class Eg4PortCoordinator(IEg4RegisterTransportFactory factory) : IEg4PortCoordinator, IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, Lazy<PortState>> _ports = new(StringComparer.Ordinal);
    private readonly object _lifecycleLock = new();
    private TaskCompletionSource _idle = CompletedSource();
    private Task? _disposeTask;
    private int _activeOperations;
    private bool _disposing;

    public async ValueTask<Eg4ReadRegistersResponse> ReadRegistersAsync(
        string port,
        Eg4ReadRegistersRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(port);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        BeginOperation();
        try
        {
            var state = _ports.GetOrAdd(
                port,
                key => new Lazy<PortState>(() => new PortState(factory.Create(key)), LazyThreadSafetyMode.ExecutionAndPublication)).Value;
            await state.Gate.WaitAsync(cancellationToken);
            try { return await state.Transport.ReadRegistersAsync(request, cancellationToken); }
            finally { state.Gate.Release(); }
        }
        finally { EndOperation(); }
    }

    public ValueTask DisposeAsync()
    {
        lock (_lifecycleLock)
        {
            _disposeTask ??= DisposeCoreAsync();
            return new ValueTask(_disposeTask);
        }
    }

    private async Task DisposeCoreAsync()
    {
        _disposing = true;
        var idleTask = _idle.Task;
        await idleTask;
        List<Exception>? failures = null;
        foreach (var lazyState in _ports.Values)
        {
            if (!lazyState.IsValueCreated) continue;
            var state = lazyState.Value;
            try { await state.Transport.DisposeAsync(); }
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
            if (--_activeOperations == 0) _idle.TrySetResult();
    }

    private static TaskCompletionSource CompletedSource()
    {
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        source.SetResult();
        return source;
    }

    private sealed record PortState(IEg4RegisterTransport Transport)
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);
    }
}
