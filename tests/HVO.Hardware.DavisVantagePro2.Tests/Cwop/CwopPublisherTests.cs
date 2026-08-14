using System.Threading.Channels;
using FluentAssertions;
using HVO.Hardware.DavisVantagePro2.Configuration;
using HVO.Hardware.DavisVantagePro2.Cwop;
using HVO.Hardware.DavisVantagePro2.Outbox;
using HVO.Hardware.DavisVantagePro2.Protocol.Packets;
using HVO.Hardware.DavisVantagePro2.Station.Models;
using HVO.Hardware.DavisVantagePro2.Workers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace HVO.Hardware.DavisVantagePro2.Tests.Cwop;

[TestClass]
[DoNotParallelize]
public sealed class CwopPublisherTests
{
    private static readonly DateTimeOffset InitialTime = new(2026, 8, 14, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task RunIterationAsync_ReturnsConfiguredCadenceAndUsesLatestObservation()
    {
        var clock = new FakeTimeProvider(InitialTime);
        var state = CurrentState(clock, 70);
        var client = new FakeClient();
        await using var fixture = CreateFixture(clock, state, client);

        var firstDelay = await fixture.Publisher.RunIterationAsync(CancellationToken.None);
        var first = await client.Packets.Reader.ReadAsync();
        state.Observed(Observation(clock, 80), clock.GetUtcNow().UtcDateTime);
        clock.Advance(firstDelay);
        var secondDelay = await fixture.Publisher.RunIterationAsync(CancellationToken.None);
        var second = await client.Packets.Reader.ReadAsync();

        firstDelay.Should().Be(TimeSpan.FromMinutes(5));
        secondDelay.Should().Be(TimeSpan.FromMinutes(5));
        first.Should().Contain("t070");
        second.Should().Contain("t080");
        await fixture.Publisher.StopAsync(CancellationToken.None);
    }

    [TestMethod]
    public async Task RunIterationAsync_FailureUsesBoundedBackoffAndRecoversWithLatestOnly()
    {
        var clock = new FakeTimeProvider(InitialTime);
        var state = CurrentState(clock, 70);
        var client = new FakeClient([new(false, true, CwopOutcome.Transport), CwopSendResult.Success]);
        await using var fixture = CreateFixture(clock, state, client);

        var firstDelay = await fixture.Publisher.RunIterationAsync(CancellationToken.None);
        var first = await client.Packets.Reader.ReadAsync();
        fixture.State.Snapshot().LastError.Should().Be("transport");
        fixture.State.Snapshot().ConsecutiveFailures.Should().Be(1);
        state.Observed(Observation(clock, 80), clock.GetUtcNow().UtcDateTime);
        clock.Advance(firstDelay);
        var secondDelay = await fixture.Publisher.RunIterationAsync(CancellationToken.None);
        var second = await client.Packets.Reader.ReadAsync();

        firstDelay.Should().Be(TimeSpan.FromMinutes(5));
        secondDelay.Should().Be(TimeSpan.FromMinutes(5));
        first.Should().Contain("t070");
        second.Should().Contain("t080");
        fixture.State.Snapshot().ConsecutiveFailures.Should().Be(0);
        fixture.State.Snapshot().LastSuccessAtUtc.Should().Be(clock.GetUtcNow().UtcDateTime);
        client.Attempts.Should().Be(2);
        client.Packets.Reader.TryRead(out _).Should().BeFalse("failed observations must not be queued for replay");
        await fixture.Publisher.StopAsync(CancellationToken.None);
    }

    [TestMethod]
    public async Task PublishLatestAsync_RejectsStaleObservationWithoutNetworkAccess()
    {
        var clock = new FakeTimeProvider(InitialTime);
        var state = CurrentState(clock, 70);
        clock.Advance(TimeSpan.FromMinutes(11));
        var client = new FakeClient();
        await using var fixture = CreateFixture(clock, state, client);

        (await fixture.Publisher.PublishLatestAsync(CancellationToken.None)).Should().BeFalse();

        client.Attempts.Should().Be(0);
        fixture.State.Snapshot().LastError.Should().Be("stale-observation");
    }

    [TestMethod]
    public async Task PublishLatestAsync_RejectsMissingOrInvalidPositionWithoutNetworkAccess()
    {
        var clock = new FakeTimeProvider(InitialTime);
        var client = new FakeClient();
        await using var missing = CreateFixture(clock, CurrentState(clock, 70), client, missingSettings: true);

        (await missing.Publisher.PublishLatestAsync(CancellationToken.None)).Should().BeFalse();
        missing.State.Snapshot().LastError.Should().Be("missing-station-settings");

        await using var invalid = CreateFixture(clock, CurrentState(clock, 70), client, new StationSettings
        {
            LatitudeDegrees = 91,
            LongitudeDegrees = -114,
            AltitudeFeet = 4000,
        });
        (await invalid.Publisher.PublishLatestAsync(CancellationToken.None)).Should().BeFalse();
        invalid.State.Snapshot().LastError.Should().Be("invalid-position:invalid-latitude");
        client.Attempts.Should().Be(0);
    }

    [TestMethod]
    public async Task PublishLatestAsync_PropagatesApplicationCancellation()
    {
        var clock = new FakeTimeProvider(InitialTime);
        var client = new FakeClient { BlockUntilCancellation = true };
        await using var fixture = CreateFixture(clock, CurrentState(clock, 70), client);
        using var cancellation = new CancellationTokenSource();

        var publish = fixture.Publisher.PublishLatestAsync(cancellation.Token);
        await client.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();

        await FluentActions.Awaiting(() => publish).Should().ThrowAsync<OperationCanceledException>();
    }

    private static PublisherFixture CreateFixture(
        FakeTimeProvider clock,
        DavisRuntimeState state,
        FakeClient client,
        StationSettings? settings = null,
        bool missingSettings = false)
    {
        var options = Options.Create(new CwopOptions
        {
            Enabled = true,
            StationId = "DW4515",
            Host = "example.test",
            IntervalSeconds = 300,
            StaleAfterSeconds = 600,
        });
        var credential = new CwopCredential();
        credential.Initialize("12345");
        var publisherState = new CwopPublisherState();
        var store = new FakeSettingsStore(missingSettings ? null : settings ?? ValidSettings());
        var publisher = new CwopPublisher(
            state,
            store,
            client,
            credential,
            publisherState,
            options,
            clock,
            NullLogger<CwopPublisher>.Instance);
        return new(publisher, publisherState);
    }

    private static DavisRuntimeState CurrentState(FakeTimeProvider clock, double temperature)
    {
        var state = new DavisRuntimeState();
        state.Observed(Observation(clock, temperature), clock.GetUtcNow().UtcDateTime);
        return state;
    }

    private static Loop2Packet Observation(FakeTimeProvider clock, double temperature) => new()
    {
        RecordedAtUtc = clock.GetUtcNow().UtcDateTime,
        OutsideTemperatureF = temperature,
    };

    private static StationSettings ValidSettings() => new()
    {
        LatitudeDegrees = 35,
        LongitudeDegrees = -114,
        AltitudeFeet = 4000,
    };

    private sealed class FakeClient(IEnumerable<CwopSendResult>? results = null) : ICwopClient
    {
        private readonly Queue<CwopSendResult> results = new(results ?? []);
        public Channel<string> Packets { get; } = Channel.CreateUnbounded<string>();
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool BlockUntilCancellation { get; init; }
        public int Attempts { get; private set; }

        public async Task<CwopSendResult> SendAsync(string login, string packet, CancellationToken cancellationToken)
        {
            Attempts++;
            Packets.Writer.TryWrite(packet);
            Started.TrySetResult();
            if (BlockUntilCancellation)
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return results.Count > 0 ? results.Dequeue() : CwopSendResult.Success;
        }
    }

    private sealed class FakeSettingsStore(StationSettings? settings) : IStationSettingsSnapshotStore
    {
        public Task<StoredStationSettingsSnapshot?> GetAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(settings is null ? null : new StoredStationSettingsSnapshot(settings, InitialTime.UtcDateTime));

        public Task<StoredStationSettingsSnapshot> SaveAsync(StationSettings value, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class PublisherFixture(CwopPublisher publisher, CwopPublisherState state) : IAsyncDisposable
    {
        public CwopPublisher Publisher { get; } = publisher;
        public CwopPublisherState State { get; } = state;

        public async ValueTask DisposeAsync()
        {
            if (Publisher.ExecuteTask is { IsCompleted: false })
                await Publisher.StopAsync(CancellationToken.None);
            Publisher.Dispose();
        }
    }
}
