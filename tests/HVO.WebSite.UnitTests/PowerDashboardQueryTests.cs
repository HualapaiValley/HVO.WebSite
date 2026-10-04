using System.Collections.Concurrent;
using FluentAssertions;
using HVO.Edge.Contracts.PowerSystem;
using HVO.WebSite.v9.Models;
using HVO.WebSite.v9.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace HVO.WebSite.UnitTests;

[TestClass]
public sealed class PowerDashboardQueryTests
{
    [TestMethod]
    [DataRow("snapshot")]
    [DataRow("inverter")]
    [DataRow("controller")]
    [DataRow("history")]
    public async Task ProviderFailureIsSectionSpecific_AndNextOperationRecovers(string failure)
    {
        var state = new ProbeState { Failure = failure };
        await using var services = BuildServices(state);
        var query = Query(services);
        var current = await query.GetCurrentAsync(CancellationToken.None);
        var history = await query.GetHistoryAsync(DateTime.UtcNow.AddHours(-6), CancellationToken.None);
        current.Snapshot.Succeeded.Should().Be(failure != "snapshot");
        current.Inverter.Succeeded.Should().Be(failure != "inverter");
        current.Controller.Succeeded.Should().Be(failure != "controller");
        history.History.Succeeded.Should().Be(failure != "history");
        state.Calls.Select(call => call.Section).Should().Equal("snapshot", "inverter", "controller", "history");
        state.Failure = null;
        var recovered = await query.GetCurrentAsync(CancellationToken.None);
        var recoveredHistory = await query.GetHistoryAsync(DateTime.UtcNow, CancellationToken.None);
        recovered.Snapshot.Succeeded.Should().BeTrue();
        recovered.Inverter.Succeeded.Should().BeTrue();
        recovered.Controller.Succeeded.Should().BeTrue();
        recoveredHistory.History.Succeeded.Should().BeTrue();
    }

    [TestMethod]
    public async Task ConcurrentOperationsOwnDistinctScopes_AndDisposeThemBeforeReturning()
    {
        var state = new ProbeState();
        await using var services = BuildServices(state);
        var query = Query(services);
        await Task.WhenAll(query.GetCurrentAsync(CancellationToken.None), query.GetCurrentAsync(CancellationToken.None),
            query.GetHistoryAsync(DateTime.UtcNow, CancellationToken.None));
        var groups = state.Calls.GroupBy(call => call.Scope).ToArray();
        groups.Should().HaveCount(3);
        groups.Select(group => group.Count()).Should().BeEquivalentTo(new[] { 3, 3, 1 });
        state.Disposed.Should().BeEquivalentTo(groups.Select(group => group.Key));
        state.OverlappedScope.Should().BeFalse("reads sharing one operation's context must remain sequential");
    }

    [TestMethod]
    public async Task RequestedCancellationPropagates_AndDisposesOperationScope()
    {
        var state = new ProbeState { Block = true };
        await using var services = BuildServices(state);
        var query = Query(services);
        using var stopping = new CancellationTokenSource();
        var load = query.GetCurrentAsync(stopping.Token);
        await state.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await stopping.CancelAsync();
        await Assert.ThrowsExactlyAsync<TaskCanceledException>(() => load);
        state.Calls.Should().ContainSingle();
        state.Disposed.Should().ContainSingle().Which.Should().Be(state.Calls.Single().Scope);
    }

    private static PowerDashboardQuery Query(ServiceProvider services) => new(services.GetRequiredService<IServiceScopeFactory>(), new(), NullLogger<PowerDashboardQuery>.Instance);
    private static ServiceProvider BuildServices(ProbeState state) => new ServiceCollection()
        .AddSingleton(state).AddScoped<OperationScope>()
        .AddScoped<IPowerSystemSnapshotProvider, SnapshotProvider>()
        .AddScoped<IPowerInventoryConfigurationProvider, InventoryProvider>()
        .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

    private sealed class ProbeState
    {
        public string? Failure { get; set; }
        public bool Block { get; set; }
        public bool OverlappedScope { get; set; }
        public ConcurrentQueue<(Guid Scope, string Section)> Calls { get; } = new();
        public ConcurrentQueue<Guid> Disposed { get; } = new();
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    private sealed class OperationScope(ProbeState state) : IAsyncDisposable
    {
        private readonly Guid _id = Guid.NewGuid();
        private int _active;
        public async Task ReadAsync(string section, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _active) != 1) state.OverlappedScope = true;
            state.Calls.Enqueue((_id, section));
            try
            {
                if (state.Block) { state.Started.TrySetResult(); await Task.Delay(Timeout.Infinite, cancellationToken); }
                await Task.Yield();
                if (state.Failure == section) throw new InvalidOperationException("SQL provider retries exhausted");
            }
            finally { Interlocked.Decrement(ref _active); }
        }
        public ValueTask DisposeAsync() { state.Disposed.Enqueue(_id); return ValueTask.CompletedTask; }
    }
    private sealed class SnapshotProvider(OperationScope scope) : IPowerSystemSnapshotProvider
    {
        public async Task<PowerSystemSnapshot?> GetLatestAsync(int lookbackMinutes = 60, CancellationToken ct = default)
        { await scope.ReadAsync("snapshot", ct); return new(DateTime.UtcNow); }
    }
    private sealed class InventoryProvider(OperationScope scope) : IPowerInventoryConfigurationProvider
    {
        public Task<(PowerDeviceInventorySnapshotResponse Inventory, PowerConfigurationSnapshotResponse Configuration)> GetLatestAsync(string sourceId = "solarassistant-total", int staleAfterMinutes = 1440, CancellationToken ct = default)
            => throw new NotSupportedException();
        public async Task<PowerInverterDetailSnapshotResponse> GetLatestInverterDetailAsync(string sourceId, int staleAfterMinutes = 5, CancellationToken ct = default)
        { await scope.ReadAsync("inverter", ct); return new() { IsPresent = true, SourceId = sourceId }; }
        public async Task<PowerMpptDetailSnapshotResponse> GetLatestMpptDetailAsync(string sourceId, int staleAfterMinutes = 5, CancellationToken ct = default)
        { await scope.ReadAsync("controller", ct); return new() { IsPresent = true, SourceId = sourceId }; }
        public async Task<PowerTelemetryHistoryResponse> GetRecentTelemetryAsync(IReadOnlyCollection<string> mpptSourceIds, IReadOnlyCollection<string> batterySourceIds, DateTime sinceUtc, CancellationToken ct = default)
        { await scope.ReadAsync("history", ct); return PowerTelemetryHistoryResponse.Empty; }
    }
}
