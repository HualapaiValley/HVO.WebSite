using HVO.WebSite.v9.Models;

namespace HVO.WebSite.v9.Services;

/// <summary>Read-only canonical v9 boundary for new website features. Legacy IWeatherService remains separate.</summary>
public interface IV9WeatherQueryService
{
    Task<V9WeatherObservation?> GetLatestAsync(string stationId, CancellationToken cancellationToken = default);
    Task<V9WeatherCurrent> GetCurrentAsync(string stationId, TimeSpan? staleAfter = null,
        CancellationToken cancellationToken = default);
    Task<V9WeatherHistoryPage<V9WeatherObservation>> GetRawHistoryAsync(string stationId,
        DateTimeOffset start, DateTimeOffset end, int limit = 100, DateTimeOffset? after = null,
        CancellationToken cancellationToken = default);
    Task<V9WeatherHistoryPage<V9WeatherArchiveObservation>> GetArchiveHistoryAsync(string stationId,
        DateTimeOffset start, DateTimeOffset end, int limit = 100, DateTimeOffset? after = null,
        CancellationToken cancellationToken = default);
}
