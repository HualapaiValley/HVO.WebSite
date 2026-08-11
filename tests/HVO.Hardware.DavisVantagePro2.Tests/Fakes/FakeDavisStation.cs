using HVO.Hardware.DavisVantagePro2.Protocol.Packets;
using HVO.Hardware.DavisVantagePro2.Protocol;
using HVO.Hardware.DavisVantagePro2.Station;
using HVO.Hardware.DavisVantagePro2.Station.Models;

namespace HVO.Hardware.DavisVantagePro2.Tests.Fakes;

internal sealed class FakeDavisStation : IDavisStation
{
    private readonly Queue<IReadOnlyList<ArchiveRecord>> archiveResponses = [];

    public int ArchiveIntervalSeconds { get; set; } = 300;
    public TimeSpan ConsoleUtcOffset { get; set; } = TimeSpan.FromHours(-7);
    public List<DateTime> ArchiveRequests { get; } = [];
    public List<string> Calls { get; } = [];
    public int ConnectCount { get; private set; }
    public int DisconnectCount { get; private set; }
    public Exception? Loop1Exception { get; set; }
    public Exception? ArchiveException { get; set; }
    public bool BlockSubsequentConnects { get; set; }
    public bool BlockConnects { get; set; }
    public Loop2Packet Loop1 { get; set; } = new() { RecordedAtUtc = DateTime.UtcNow };
    public IReadOnlyList<Loop2Packet> Loop2Packets { get; set; } = [];
    public StationSettings Settings { get; set; } = new() { ArchiveIntervalSeconds = 300, GmtOffsetHours = -7 };
    public StationInfo Info { get; set; } = new();

    public void QueueArchiveResponse(params ArchiveRecord[] records) => archiveResponses.Enqueue(records);

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        ConnectCount++;
        Calls.Add("connect");
        if (BlockConnects)
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        if (BlockSubsequentConnects && ConnectCount > 1)
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    }

    public Task DisconnectAsync()
    {
        DisconnectCount++;
        Calls.Add("disconnect");
        return Task.CompletedTask;
    }

    public void ApplyStationSettings(StationSettings settings)
    {
        Settings = settings;
        ArchiveIntervalSeconds = settings.ArchiveIntervalSeconds;
        ConsoleUtcOffset = settings.UseTimezoneCode
            ? DavisTimeZoneTable.GetOffset(settings.TimezoneCode) ?? TimeSpan.Zero
            : TimeSpan.FromHours(settings.GmtOffsetHours);
    }

    public Task<StationSettings> GetStationSettingsAsync(CancellationToken cancellationToken = default) => Task.FromResult(Settings);
    public Task<StationInfo> GetStationInfoAsync(CancellationToken cancellationToken = default) => Task.FromResult(Info);

    public Task<Loop2Packet> GetLoop1Async(CancellationToken cancellationToken = default)
    {
        Calls.Add("loop1");
        return Loop1Exception is null ? Task.FromResult(Loop1) : Task.FromException<Loop2Packet>(Loop1Exception);
    }

    public async IAsyncEnumerable<Loop2Packet> StreamLoop2Async(
        int count,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Calls.Add("loop2");
        foreach (var packet in Loop2Packets.Take(count))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return packet;
        }
        await Task.CompletedTask;
    }

    public async IAsyncEnumerable<ArchiveRecord> GetArchiveSinceAsync(
        DateTime since,
        int maxRecords = int.MaxValue,
        bool fallbackOnEmpty = false,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Calls.Add("archive");
        ArchiveRequests.Add(since);
        if (ArchiveException is not null)
        {
            var exception = ArchiveException;
            ArchiveException = null;
            throw exception;
        }
        var records = archiveResponses.Count == 0 ? [] : archiveResponses.Dequeue();
        foreach (var record in records.Take(maxRecords))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return record;
        }
        await Task.CompletedTask;
    }
}
