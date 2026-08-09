using System.Collections.Concurrent;
using FluentAssertions;
using HVO.Hardware.Eg4.Protocol;

namespace HVO.Hardware.Eg4.Tests.Protocol;

[TestClass]
public sealed class Eg4PortCoordinatorTests
{
    private static readonly Eg4ReadRegistersRequest Request = new(1, Eg4RegisterTable.Holding, 1, 1);

    [TestMethod]
    public async Task SamePortSerializesWhileDifferentPortsOverlap()
    {
        var factory = new GateTransportFactory();
        await using var coordinator = new Eg4PortCoordinator(factory);
        var a1 = coordinator.ReadRegistersAsync("a", Request, CancellationToken.None).AsTask();
        await factory.WaitForEntryAsync();
        var a2 = coordinator.ReadRegistersAsync("a", Request, CancellationToken.None).AsTask();
        var b = coordinator.ReadRegistersAsync("b", Request, CancellationToken.None).AsTask();
        await factory.WaitForEntryAsync();
        factory.Transports["a"].EntryCount.Should().Be(1);
        factory.Release.SetResult();

        await Task.WhenAll(a1, a2, b);
        factory.Transports["a"].MaximumConcurrency.Should().Be(1);
        factory.GlobalMaximumConcurrency.Should().BeGreaterThan(1);
    }

    [TestMethod]
    public async Task CancellationFailureAndDisposalAreSafeAndTerminal()
    {
        var factory = new GateTransportFactory();
        var coordinator = new Eg4PortCoordinator(factory);
        var active = coordinator.ReadRegistersAsync("a", Request, CancellationToken.None).AsTask();
        await factory.WaitForEntryAsync();
        using var cts = new CancellationTokenSource();
        var waiting = coordinator.ReadRegistersAsync("a", Request, cts.Token).AsTask();
        await cts.CancelAsync();
        await FluentActions.Awaiting(() => waiting).Should().ThrowAsync<OperationCanceledException>();
        var dispose = coordinator.DisposeAsync().AsTask();
        var concurrentDispose = coordinator.DisposeAsync().AsTask();
        dispose.IsCompleted.Should().BeFalse();
        concurrentDispose.IsCompleted.Should().BeFalse();
        factory.Release.SetResult();
        await active;
        await Task.WhenAll(dispose, concurrentDispose);
        factory.Transports["a"].Disposed.Should().BeTrue();
        await FluentActions.Awaiting(async () => await coordinator.ReadRegistersAsync("new", Request, CancellationToken.None))
            .Should().ThrowAsync<ObjectDisposedException>();
    }

    [TestMethod]
    public async Task FactoryFailureDoesNotBlockDisposalAndPreCancellationDoesNotCreateTransport()
    {
        var factory = new ThrowingFactory();
        var coordinator = new Eg4PortCoordinator(factory);
        await FluentActions.Awaiting(async () => await coordinator.ReadRegistersAsync("bad", Request, CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>();
        await coordinator.DisposeAsync();

        factory = new ThrowingFactory();
        coordinator = new Eg4PortCoordinator(factory);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        await FluentActions.Awaiting(async () => await coordinator.ReadRegistersAsync("bad", Request, cts.Token))
            .Should().ThrowAsync<OperationCanceledException>();
        factory.CreateCount.Should().Be(0);
        await coordinator.DisposeAsync();
    }

    private sealed class GateTransportFactory : IEg4RegisterTransportFactory
    {
        private int _globalConcurrency;
        private int _globalMaximumConcurrency;
        private readonly SemaphoreSlim _entries = new(0);
        public ConcurrentDictionary<string, GateTransport> Transports { get; } = new();
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int GlobalMaximumConcurrency => _globalMaximumConcurrency;
        public IEg4RegisterTransport Create(string port) => Transports.GetOrAdd(port, key => new GateTransport(this));
        public async Task WaitForEntryAsync() =>
            (await _entries.WaitAsync(TimeSpan.FromSeconds(5))).Should().BeTrue();
        public void Enter() { _entries.Release(); var value = Interlocked.Increment(ref _globalConcurrency); InterlockedExtensions.Max(ref _globalMaximumConcurrency, value); }
        public void Exit() => Interlocked.Decrement(ref _globalConcurrency);
    }

    private sealed class GateTransport(GateTransportFactory owner) : IEg4RegisterTransport
    {
        private int _concurrency;
        private int _entryCount;
        private int _maximumConcurrency;
        public int EntryCount => _entryCount;
        public int MaximumConcurrency => _maximumConcurrency;
        public bool Disposed { get; private set; }
        public async ValueTask<Eg4ReadRegistersResponse> ReadRegistersAsync(Eg4ReadRegistersRequest request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _entryCount);
            owner.Enter();
            var value = Interlocked.Increment(ref _concurrency);
            InterlockedExtensions.Max(ref _maximumConcurrency, value);
            try { await owner.Release.Task.WaitAsync(cancellationToken); return new Eg4ReadRegistersResponse([1]); }
            finally { Interlocked.Decrement(ref _concurrency); owner.Exit(); }
        }
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }
    private sealed class ThrowingFactory : IEg4RegisterTransportFactory
    {
        public int CreateCount { get; private set; }
        public IEg4RegisterTransport Create(string port) { CreateCount++; throw new InvalidOperationException("open failed"); }
    }

    private static class InterlockedExtensions
    {
        public static void Max(ref int target, int value)
        {
            var current = Volatile.Read(ref target);
            while (current < value)
            {
                var observed = Interlocked.CompareExchange(ref target, value, current);
                if (observed == current) return;
                current = observed;
            }
        }
    }
}
