using FluentAssertions;
using HVO.Edge.Contracts.Weather;
using HVO.Hardware.DavisVantagePro2.Configuration;
using HVO.Hardware.DavisVantagePro2.HomeAssistant;
using HVO.Hardware.DavisVantagePro2.Outbox;
using HVO.Hardware.DavisVantagePro2.Protocol.Packets;
using HVO.Hardware.DavisVantagePro2.Station.Models;
using HVO.Hardware.DavisVantagePro2.Tests.Fakes;
using HVO.Hardware.DavisVantagePro2.Workers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HVO.Hardware.DavisVantagePro2.Tests.Workers;

[TestClass]
public sealed class WeatherStationWorkerTests
{
    [TestMethod]
    public async Task FirstTopOff_RequestsFullArchiveAndAdvancesOnlyAfterDurableEnqueue()
    {
        var events = new List<string>();
        var station = StationWithArchive(ArchiveAt(5, 0));
        var cursor = new RecordingCursorStore(events);
        var writer = new RecordingWriter(events, archiveInserted: true);
        await using var fixture = CreateFixture(station, cursor, writer);

        var count = await fixture.Worker.RunArchiveTopOffAsync();

        count.Should().Be(1);
        station.ArchiveRequests.Should().Equal(DateTime.MinValue);
        station.ArchiveRequestLimits.Should().Equal(25);
        events.Should().Equal("enqueue:archive", "cursor:advance");
        cursor.Current!.ConsoleRecordedAtLocal.Should().Be(new DateTime(2026, 8, 11, 5, 0, 0));
    }

    [TestMethod]
    public async Task DuplicateArchiveEnqueue_StillAdvancesCursorAfterDuplicateConfirmation()
    {
        var events = new List<string>();
        var station = StationWithArchive(ArchiveAt(5, 0));
        var cursor = new RecordingCursorStore(events);
        var writer = new RecordingWriter(events, archiveInserted: false);
        await using var fixture = CreateFixture(station, cursor, writer);

        await fixture.Worker.RunArchiveTopOffAsync();

        events.Should().Equal("enqueue:duplicate", "cursor:advance");
        cursor.Current.Should().NotBeNull();
    }

    [TestMethod]
    public async Task FailedArchiveEnqueue_DoesNotAdvanceCursor()
    {
        var events = new List<string>();
        var station = StationWithArchive(ArchiveAt(5, 0));
        var cursor = new RecordingCursorStore(events);
        await using var fixture = CreateFixture(station, cursor, new RecordingWriter(events, true, failArchive: true));

        var action = () => fixture.Worker.RunArchiveTopOffAsync();

        await action.Should().ThrowAsync<IOException>();
        events.Should().Equal("enqueue:failed");
        cursor.Current.Should().BeNull();
    }

    [TestMethod]
    public async Task ReconnectAndPeriodicTopOff_UseExactCursorBetweenFiniteLoopBatches()
    {
        var station = StationWithArchive(ArchiveAt(5, 5));
        station.ArchiveIntervalSeconds = 300;
        station.Settings = station.Settings with { ArchiveIntervalSeconds = 300 };
        station.Loop2Packets = [new Loop2Packet { RecordedAtUtc = new DateTime(2026, 8, 11, 12, 1, 0, DateTimeKind.Utc) }];
        var cursor = new RecordingCursorStore([])
        {
            Current = new("station-1", new DateTime(2026, 8, 11, 5, 0, 0), new DateTime(2026, 8, 11, 12, 0, 0, DateTimeKind.Utc), DateTime.UtcNow)
        };
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 8, 11, 12, 0, 0, TimeSpan.Zero));
        await using var fixture = CreateFixture(station, cursor, new RecordingWriter([], true), clock);

        await fixture.Worker.RunArchiveTopOffAsync();
        station.QueueArchiveResponse(ArchiveAt(5, 10));
        await fixture.Worker.RunArchiveTopOffAsync();
        station.QueueArchiveResponse(ArchiveAt(5, 15));
        await fixture.Worker.PollLoopBatchAsync(CancellationToken.None);
        clock.Advance(TimeSpan.FromMinutes(5).Add(TimeSpan.FromSeconds(-1)));
        (await fixture.Worker.RunPeriodicArchiveTopOffIfDueAsync(CancellationToken.None)).Should().BeFalse();
        clock.Advance(TimeSpan.FromSeconds(1));
        var periodicRan = await fixture.Worker.RunPeriodicArchiveTopOffIfDueAsync(CancellationToken.None);

        periodicRan.Should().BeTrue();
        station.ArchiveRequests.Should().HaveCount(3);
        station.ArchiveRequests[0].Should().Be(new DateTime(2026, 8, 11, 5, 0, 0));
        station.ArchiveRequests[1].Should().Be(new DateTime(2026, 8, 11, 5, 5, 0));
        station.ArchiveRequests[2].Should().Be(new DateTime(2026, 8, 11, 5, 10, 0));
        station.Calls.Should().ContainInOrder("loop1", "loop2", "archive");
    }

    [TestMethod]
    public async Task Disconnect_PublishesUnavailableAndWorkerKeepsRunningUntilCancellation()
    {
        var station = new FakeDavisStation
        {
            Loop1Exception = new IOException("station disconnected"),
            BlockSubsequentConnects = true,
        };
        var homeAssistant = new RecordingHomeAssistantProjection();
        await using var fixture = CreateFixture(
            station,
            new RecordingCursorStore([]),
            new RecordingWriter([], true),
            homeAssistant: homeAssistant);

        await fixture.Worker.StartAsync(CancellationToken.None);
        await Task.WhenAll(homeAssistant.Unavailable.Task, station.Disconnected.Task)
            .WaitAsync(TimeSpan.FromSeconds(5));

        fixture.Worker.ExecuteTask.Should().NotBeNull();
        fixture.Worker.ExecuteTask!.IsCompleted.Should().BeFalse();
        station.DisconnectCount.Should().Be(1);
        await fixture.Worker.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
    }

    [TestMethod]
    public async Task ArchiveFailure_DoesNotDisconnectOrPreventLiveCollection()
    {
        var station = new FakeDavisStation
        {
            ArchiveException = new IOException("DMPAFT unavailable"),
            Loop2Packets = [new Loop2Packet { RecordedAtUtc = DateTime.UtcNow }],
        };
        var writer = new RecordingWriter([], true);
        await using var fixture = CreateFixture(station, new RecordingCursorStore([]), writer);

        await fixture.Worker.StartAsync(CancellationToken.None);
        await writer.LiveEnqueued.Task.WaitAsync(TimeSpan.FromSeconds(5));

        station.ConnectCount.Should().Be(1);
        station.DisconnectCount.Should().Be(0);
        fixture.Worker.ExecuteTask.Should().NotBeNull();
        fixture.Worker.ExecuteTask!.IsCompleted.Should().BeFalse();
        await fixture.Worker.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
    }

    [TestMethod]
    public async Task ArchiveFailureThenReconnect_DefersArchiveRetryAndResumesLiveCollection()
    {
        var station = new FakeDavisStation
        {
            ArchiveException = new IOException("DMPAFT unavailable"),
            Loop1Exception = new IOException("station disconnected"),
            ClearLoop1ExceptionAfterThrow = true,
            Loop2Packets = [new Loop2Packet { RecordedAtUtc = DateTime.UtcNow }],
        };
        var writer = new RecordingWriter([], true);
        await using var fixture = CreateFixture(station, new RecordingCursorStore([]), writer);

        await fixture.Worker.StartAsync(CancellationToken.None);
        await writer.LiveEnqueued.Task.WaitAsync(TimeSpan.FromSeconds(5));

        station.ConnectCount.Should().Be(2);
        station.ArchiveRequests.Should().HaveCount(1);
        fixture.Worker.ExecuteTask.Should().NotBeNull();
        fixture.Worker.ExecuteTask!.IsCompleted.Should().BeFalse();
        await fixture.Worker.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
    }

    [TestMethod]
    public async Task Loop1RefreshFailure_ContinuesStreamingWithCachedLoop1Fields()
    {
        var firstAt = new DateTime(2026, 8, 11, 12, 0, 0, DateTimeKind.Utc);
        var station = new FakeDavisStation
        {
            Loop1 = new Loop2Packet { RecordedAtUtc = firstAt, ConsoleBatteryVoltage = 4.6 },
            Loop2Packets = [new Loop2Packet { RecordedAtUtc = firstAt.AddSeconds(2), OutsideTemperatureF = 80 }],
        };
        var writer = new RecordingWriter([], true);
        await using var fixture = CreateFixture(station, new RecordingCursorStore([]), writer);

        await fixture.Worker.PollLoopBatchAsync(CancellationToken.None);
        station.Loop1Exception = new IOException("LOOP1 unavailable");
        station.Loop2Packets = [new Loop2Packet { RecordedAtUtc = firstAt.AddSeconds(4), OutsideTemperatureF = 81 }];
        await fixture.Worker.PollLoopBatchAsync(CancellationToken.None);

        writer.LivePayloads.Should().HaveCount(2);
        writer.LivePayloads[1].TemperatureF.Should().Be(81);
        writer.LivePayloads[1].ConsoleBatteryVoltage.Should().Be(4.6);
        station.DisconnectCount.Should().Be(0);
    }

    private static FakeDavisStation StationWithArchive(params ArchiveRecord[] records)
    {
        var station = new FakeDavisStation();
        station.QueueArchiveResponse(records);
        return station;
    }

    private static ArchiveRecord ArchiveAt(int hour, int minute) => new()
    {
        DateTimeLocal = new DateTime(2026, 8, 11, hour, minute, 0, DateTimeKind.Unspecified),
        ArchiveIntervalMinutes = 5,
        DownloadRecordType = 1,
    };

    private static WorkerFixture CreateFixture(
        FakeDavisStation station,
        RecordingCursorStore cursor,
        RecordingWriter writer,
        TimeProvider? timeProvider = null,
        RecordingHomeAssistantProjection? homeAssistant = null)
    {
        var services = new ServiceCollection().AddSingleton<IDavisOutboxWriter>(writer).BuildServiceProvider();
        var state = new DavisRuntimeState();
        var worker = new WeatherStationWorker(
            station,
            services.GetRequiredService<IServiceScopeFactory>(),
            cursor,
            new NoOpSettingsStore(),
            new NoOpInfoStore(),
            homeAssistant ?? new RecordingHomeAssistantProjection(),
            state,
            Options.Create(new StationOptions { StationId = "station-1", ArchiveCatchupMode = ArchiveCatchupMode.Enabled, ArchiveOverlapIntervals = 2 }),
            timeProvider ?? TimeProvider.System,
            NullLogger<WeatherStationWorker>.Instance);
        return new(worker, services);
    }

    private sealed class WorkerFixture(WeatherStationWorker worker, ServiceProvider services) : IAsyncDisposable
    {
        public WeatherStationWorker Worker { get; } = worker;
        public async ValueTask DisposeAsync()
        {
            if (Worker.ExecuteTask is { IsCompleted: false })
                await Worker.StopAsync(CancellationToken.None);
            await services.DisposeAsync();
        }
    }

    private sealed class RecordingCursorStore(List<string> events) : IDavisArchiveCursorStore
    {
        public DavisArchiveCursor? Current { get; set; }
        public Task<DavisArchiveCursor?> GetAsync(string stationId, CancellationToken cancellationToken) => Task.FromResult(Current);
        public Task<bool> AdvanceAsync(string stationId, DateTime local, DateTime utc, CancellationToken cancellationToken)
        {
            events.Add("cursor:advance");
            if (Current is null || local > Current.ConsoleRecordedAtLocal)
                Current = new(stationId, local, utc, DateTime.UtcNow);
            return Task.FromResult(true);
        }
    }

    private sealed class RecordingWriter(List<string> events, bool archiveInserted, bool failArchive = false) : IDavisOutboxWriter
    {
        public TaskCompletionSource LiveEnqueued { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<DavisWeatherLivePayload> LivePayloads { get; } = [];
        public Task<bool> EnqueueRawAsync(DavisWeatherLivePayload payload, CancellationToken cancellationToken)
        {
            events.Add("enqueue:live");
            LivePayloads.Add(payload);
            LiveEnqueued.TrySetResult();
            return Task.FromResult(true);
        }
        public Task<bool> EnqueueArchiveAsync(DavisWeatherArchivePayload payload, CancellationToken cancellationToken)
        {
            if (failArchive)
            {
                events.Add("enqueue:failed");
                return Task.FromException<bool>(new IOException("outbox unavailable"));
            }
            events.Add(archiveInserted ? "enqueue:archive" : "enqueue:duplicate");
            return Task.FromResult(archiveInserted);
        }
    }

    private sealed class RecordingHomeAssistantProjection : IDavisHomeAssistantProjection
    {
        public TaskCompletionSource Unavailable { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Publish(Loop2Packet reading) => true;
        public bool PublishUnavailable(DateTimeOffset observedAtUtc) { Unavailable.TrySetResult(); return true; }
    }

    private sealed class NoOpSettingsStore : IStationSettingsSnapshotStore
    {
        public Task<StoredStationSettingsSnapshot?> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult<StoredStationSettingsSnapshot?>(null);
        public Task<StoredStationSettingsSnapshot> SaveAsync(StationSettings settings, CancellationToken cancellationToken = default) =>
            Task.FromResult(new StoredStationSettingsSnapshot(settings, DateTime.UtcNow));
    }

    private sealed class NoOpInfoStore : IStationInfoSnapshotStore
    {
        public Task<StoredStationInfoSnapshot?> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult<StoredStationInfoSnapshot?>(null);
        public Task<StoredStationInfoSnapshot> SaveAsync(StationInfo stationInfo, CancellationToken cancellationToken = default) =>
            Task.FromResult(new StoredStationInfoSnapshot(stationInfo, DateTime.UtcNow));
    }

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
        public void Advance(TimeSpan amount) => utcNow += amount;
    }
}
