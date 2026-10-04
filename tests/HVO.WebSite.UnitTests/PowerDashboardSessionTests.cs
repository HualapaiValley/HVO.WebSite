using System.Threading.Channels;
using FluentAssertions;
using HVO.Edge.Contracts.PowerSystem;
using HVO.WebSite.v9.Configuration;
using HVO.WebSite.v9.Models;
using HVO.WebSite.v9.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace HVO.WebSite.UnitTests;

[TestClass]
public sealed class PowerDashboardSessionTests
{
    [TestMethod]
    public async Task CurrentClockAgesUnchangedTelemetry_WithoutFetchingHistoryOnEachTick()
    {
        await using var fixture = new Fixture();
        fixture.Query.Snapshot = Snapshot(fixture.Clock.GetUtcNow().UtcDateTime);
        await fixture.StartAsync();
        fixture.Session.ViewModel.SnapshotState.Should().Be("Live");
        await fixture.AdvanceAsync(TimeSpan.FromSeconds(8));
        fixture.Session.ViewModel.SnapshotState.Should().Be("Warning");
        await fixture.AdvanceAsync(TimeSpan.FromSeconds(3));
        fixture.Session.ViewModel.SnapshotState.Should().Be("Stale");
        fixture.Session.ViewModel.BatteryObservations.Single().FreshnessStatus.Should().Be("stale");
        fixture.Query.CurrentCalls.Should().Be(1, "freshness ticks do not require a database read");
        fixture.Query.HistoryCalls.Should().Be(1);
        await fixture.AdvanceAsync(TimeSpan.FromSeconds(9));
        fixture.Query.CurrentCalls.Should().Be(2);
        fixture.Query.HistoryCalls.Should().Be(1);
        await fixture.AdvanceAsync(TimeSpan.FromSeconds(40));
        fixture.Query.HistoryCalls.Should().Be(2);
    }

    [TestMethod]
    public async Task WaitingDashboardDiscoversDataAndNewAlarms()
    {
        await using var fixture = new Fixture();
        await fixture.StartAsync();
        fixture.Session.ViewModel.SnapshotState.Should().Be("Waiting");
        fixture.Query.Snapshot = Snapshot(fixture.Clock.GetUtcNow().UtcDateTime);
        await fixture.AdvanceAsync(TimeSpan.FromSeconds(20));
        fixture.Session.ViewModel.BatteryPower.Should().Be("-250 W");
        fixture.Query.Snapshot = Snapshot(fixture.Clock.GetUtcNow().UtcDateTime, alarm: true);
        await fixture.AdvanceAsync(TimeSpan.FromSeconds(20));
        fixture.Session.ViewModel.BatteryAlarmState.Should().Be("Active alarm");
    }

    [TestMethod]
    public async Task FreshnessKeepsTickingWhileAProviderReadIsInFlight()
    {
        await using var fixture = new Fixture(currentIntervalSeconds: 5);
        fixture.Query.Snapshot = Snapshot(fixture.Clock.GetUtcNow().UtcDateTime);
        await fixture.StartAsync();
        fixture.Query.Block = true;
        await fixture.AdvanceAsync(TimeSpan.FromSeconds(5));
        await fixture.Query.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await fixture.AdvanceAsync(TimeSpan.FromSeconds(6));
        fixture.Session.ViewModel.SnapshotState.Should().Be("Stale");
        fixture.Query.CurrentCalls.Should().Be(2);
        fixture.Query.HistoryCalls.Should().Be(1);
        fixture.Query.MaxConcurrent.Should().Be(1);
    }

    [TestMethod]
    public async Task HistoryRevisionTracksContentAndWindow_DespiteSameRowCount()
    {
        await using var fixture = new Fixture();
        var observed = fixture.Clock.GetUtcNow().UtcDateTime;
        fixture.Query.History = History(observed, -100);
        await fixture.StartAsync();
        var original = fixture.Session.HistoryRevision;
        await fixture.Session.RetryAsync();
        fixture.Session.HistoryRevision.Should().Be(original, "identical content and window need no chart update");
        fixture.Query.History = History(observed, -200);
        await fixture.Session.RetryAsync();
        fixture.Session.HistoryRevision.Should().Be(original + 1);
        fixture.Session.History.BatteryReadings.Should().ContainSingle().Which.PowerW.Should().Be(-200);
        await fixture.AdvanceAsync(TimeSpan.FromSeconds(60));
        fixture.Session.HistoryRevision.Should().Be(original + 2, "the moving query window is part of the revision");
    }

    [TestMethod]
    public async Task FailedSectionsRetainSuccessfulData_AndRetryRecoversIndependently()
    {
        await using var fixture = new Fixture();
        fixture.Query.Snapshot = Snapshot(fixture.Clock.GetUtcNow().UtcDateTime);
        fixture.Query.History = History(fixture.Clock.GetUtcNow().UtcDateTime, -100);
        await fixture.StartAsync();
        fixture.Query.FailSnapshot = true;
        fixture.Query.FailHistory = true;
        fixture.Query.Inverter = new() { IsPresent = true, PvStrings = [new() { StringId = "one", PowerW = 1234 }] };
        await fixture.Session.RetryAsync();
        fixture.Session.ViewModel.BatteryPower.Should().Be("-250 W");
        fixture.Session.Equipment.InverterPvSubtotal.Should().Be("1234 W");
        fixture.Session.History.BatteryReadings.Should().ContainSingle();
        fixture.Session.Errors.Keys.Should().BeEquivalentTo("current", "history");
        fixture.Query.FailSnapshot = false;
        fixture.Query.FailHistory = false;
        fixture.Query.Snapshot = Snapshot(fixture.Clock.GetUtcNow().UtcDateTime, alarm: true);
        await fixture.Session.RetryAsync();
        fixture.Session.Errors.Should().BeEmpty();
        fixture.Session.ViewModel.BatteryAlarmState.Should().Be("Active alarm");
    }

    [TestMethod]
    public async Task TimerAndRetryNeverOverlap_DisposalCancelsActiveAndQueuedLoads()
    {
        var fixture = new Fixture();
        await fixture.StartAsync();
        fixture.Query.Block = true;
        fixture.Clock.Advance(TimeSpan.FromSeconds(20));
        await fixture.Query.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var retry = fixture.Session.RetryAsync();
        fixture.Query.MaxConcurrent.Should().Be(1);
        await fixture.DisposeAsync();
        await Assert.ThrowsAsync<OperationCanceledException>(() => retry);
        fixture.Query.Cancellations.Should().Be(1);
        var calls = fixture.Query.CurrentCalls;
        fixture.Clock.Advance(TimeSpan.FromHours(1));
        fixture.Query.CurrentCalls.Should().Be(calls);
        fixture.Query.MaxConcurrent.Should().Be(1);
    }

    [TestMethod]
    public async Task DisposalDuringInitialLoadCancelsItWithoutPublishingAnError()
    {
        var fixture = new Fixture();
        fixture.Query.Block = true;
        var start = fixture.Session.StartAsync();
        await fixture.Query.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await fixture.DisposeAsync();
        await Assert.ThrowsAsync<OperationCanceledException>(() => start);
        fixture.Session.Errors.Should().BeEmpty();
        fixture.Query.Cancellations.Should().Be(1);
    }

    [TestMethod]
    public async Task ProviderCompletingAfterDisposalCannotStartHistoryOrPublishItsResult()
    {
        var fixture = new Fixture();
        fixture.Query.Block = true;
        fixture.Query.IgnoreCancellation = true;
        var start = fixture.Session.StartAsync();
        await fixture.Query.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var dispose = fixture.DisposeAsync().AsTask();
        fixture.Query.Release.TrySetResult();
        await dispose;
        await Assert.ThrowsAsync<OperationCanceledException>(() => start);
        fixture.Query.HistoryCalls.Should().Be(0);
        fixture.Session.HasLoaded.Should().BeFalse();
        fixture.Session.Errors.Should().BeEmpty();
    }

    [TestMethod]
    public async Task UnexpectedQueryFailureDoesNotKillRefresh_AndStillLoadsHistory()
    {
        await using var fixture = new Fixture();
        fixture.Query.ThrowCurrent = true;
        fixture.Query.History = History(fixture.Clock.GetUtcNow().UtcDateTime, -100);
        await fixture.StartAsync();
        fixture.Session.Errors.Should().ContainKey("current");
        fixture.Session.History.BatteryReadings.Should().ContainSingle();
        fixture.Query.ThrowCurrent = false;
        fixture.Query.Snapshot = Snapshot(fixture.Clock.GetUtcNow().UtcDateTime);
        await fixture.AdvanceAsync(TimeSpan.FromSeconds(20));
        fixture.Session.Errors.Should().BeEmpty();
        fixture.Session.ViewModel.BatteryPower.Should().Be("-250 W");
    }

    private static PowerSystemSnapshot Snapshot(DateTime observed, bool alarm = false) => new(observed,
        Battery: new(PowerW: new(250, PowerMetricSource.Eg46500Ex, observed), HasAlarms: new(alarm, PowerMetricSource.Eg46500Ex, observed)),
        BatteryObservations: [new("inverter", "one", PowerMetricSource.Eg46500Ex, PowerMeasurementRole.InverterBranch, "battery", observed, PowerW: 250)]);
    private static PowerTelemetryHistoryResponse History(DateTime observed, double power) => new([], [new(observed, "inverter", "one", power)]);

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly Channel<bool> _changed = Channel.CreateUnbounded<bool>();
        private readonly Channel<bool> _armed = Channel.CreateUnbounded<bool>();
        public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 10, 4, 0, 0, 0, TimeSpan.Zero));
        public ControlledQuery Query { get; } = new();
        public PowerDashboardSession Session { get; }
        public Fixture(int currentIntervalSeconds = 20)
        {
            Session = new(Query, new SchedulingClock(Clock, _armed), new()
            {
                CurrentInterval = TimeSpan.FromSeconds(currentIntervalSeconds), HistoryInterval = TimeSpan.FromSeconds(60),
                Composition = new PowerCompositionOptions { Eg4BranchFreshnessSeconds = 10 },
            }, NullLogger<PowerDashboardSession>.Instance)
            { Changed = () => { _changed.Writer.TryWrite(true); return Task.CompletedTask; } };
        }
        public async Task StartAsync()
        {
            await Session.StartAsync();
            await _changed.Reader.ReadAsync();
            await _armed.Reader.ReadAsync();
        }
        public async Task AdvanceAsync(TimeSpan duration)
        {
            while (_changed.Reader.TryRead(out _)) { }
            Clock.Advance(duration);
            await _changed.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));
            // Advancing again waits for the next timer to be armed, without real-time sleeps.
            await _armed.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));
        }
        public ValueTask DisposeAsync() => Session.DisposeAsync();
    }

    private sealed class SchedulingClock(FakeTimeProvider clock, Channel<bool> armed) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => clock.GetUtcNow();
        public override long GetTimestamp() => clock.GetTimestamp();
        public override long TimestampFrequency => clock.TimestampFrequency;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = clock.CreateTimer(callback, state, dueTime, period);
            armed.Writer.TryWrite(true);
            return timer;
        }
    }

    private sealed class ControlledQuery : IPowerDashboardQuery
    {
        private int _concurrent;
        public int MaxConcurrent { get; private set; }
        public int CurrentCalls { get; private set; }
        public int HistoryCalls { get; private set; }
        public int Cancellations { get; private set; }
        public bool Block { get; set; }
        public bool IgnoreCancellation { get; set; }
        public bool FailSnapshot { get; set; }
        public bool FailHistory { get; set; }
        public bool ThrowCurrent { get; set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public PowerSystemSnapshot? Snapshot { get; set; }
        public PowerInverterDetailSnapshotResponse Inverter { get; set; } = new();
        public PowerTelemetryHistoryResponse History { get; set; } = PowerTelemetryHistoryResponse.Empty;

        public async Task<PowerDashboardCurrentResult> GetCurrentAsync(CancellationToken cancellationToken)
        {
            CurrentCalls++;
            MaxConcurrent = Math.Max(MaxConcurrent, Interlocked.Increment(ref _concurrent));
            try
            {
                if (Block)
                {
                    Started.TrySetResult();
                    try
                    {
                        if (IgnoreCancellation) await Release.Task;
                        else await Task.Delay(Timeout.Infinite, cancellationToken);
                    }
                    catch (OperationCanceledException) { Cancellations++; throw; }
                }
                if (ThrowCurrent) throw new InvalidOperationException("Provider retries exhausted");
                return new(FailSnapshot ? DashboardSection<PowerSystemSnapshot?>.Failed("Current power") : new(Snapshot), new(Inverter), new(new()));
            }
            finally { Interlocked.Decrement(ref _concurrent); }
        }
        public Task<PowerDashboardHistoryResult> GetHistoryAsync(DateTime windowStartUtc, CancellationToken cancellationToken)
        {
            HistoryCalls++;
            return Task.FromResult(new PowerDashboardHistoryResult(windowStartUtc,
                FailHistory ? DashboardSection<PowerTelemetryHistoryResponse>.Failed("Power history") : new(History)));
        }
    }
}
