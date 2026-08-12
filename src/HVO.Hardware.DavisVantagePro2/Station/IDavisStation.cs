using HVO.Hardware.DavisVantagePro2.Protocol.Packets;
using HVO.Hardware.DavisVantagePro2.Station.Models;

namespace HVO.Hardware.DavisVantagePro2.Station;

public interface IDavisStation
{
    int ArchiveIntervalSeconds { get; }
    TimeSpan ConsoleUtcOffset { get; }
    Task ConnectAsync(CancellationToken cancellationToken = default);
    Task DisconnectAsync();
    void ApplyStationSettings(StationSettings settings);
    Task<StationSettings> GetStationSettingsAsync(CancellationToken cancellationToken = default);
    Task<StationInfo> GetStationInfoAsync(CancellationToken cancellationToken = default);
    Task<Loop2Packet> GetLoop1Async(CancellationToken cancellationToken = default);
    IAsyncEnumerable<Loop2Packet> StreamLoop2Async(int count, CancellationToken cancellationToken = default);
    IAsyncEnumerable<ArchiveRecord> GetArchiveSinceAsync(
        DateTime since,
        int maxRecords = int.MaxValue,
        bool fallbackOnEmpty = false,
        CancellationToken cancellationToken = default);
}
