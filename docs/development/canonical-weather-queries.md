# Canonical weather queries for new website features

New website features should inject `IV9WeatherQueryService`. It reads only
`HvoV9DbContext.WeatherRaw` and `WeatherArchive`, where the Davis outbox currently
sends its canonical observations. `IWeatherService` retains the existing API response
boundary; current weather and public highs/lows now read v9 raw observations while
the legacy latest endpoint is unchanged. [Public weather history](public-weather-history.md)
documents the anonymous HTTP endpoints' separate 24-hour limit and hourly aggregation.
Registering the internal query service does not itself change public routes or
authorization; the explicit public-history controller changes are documented separately.

## Service scope and concurrency

The service uses one scoped `HvoV9DbContext`, which is not thread-safe. Resolve it
inside an operation-owned async scope and complete calls sequentially before
disposing that scope. HTTP callers can use their existing request scope for
sequential calls. Interactive components should inject `IServiceScopeFactory`,
create and dispose a scope per refresh/read, and retain the returned DTOs rather
than the service for the circuit. Do not overlap calls on the same service instance.

## Repository inventory and compatibility decision

| Boundary | Source and current repository consumer | Decision |
|---|---|---|
| `GET /api/v1/weather/latest` | `WeatherController` → `IWeatherService` → legacy `DavisVantageProConsoleRecordsNew` through `HvoDbContext` | Preserve response shape, legacy selection and WeatherRead policy |
| `GET /api/v1/weather/current` | v9 `WeatherRaw` through `IWeatherService` | Anonymous current/extremes contract, latest station and server-local daily extremes |
| `GET /api/v1/weather/highs-lows` | v9 `WeatherRaw` through `IWeatherService` | Anonymous flat response, null-aware extremes, optional dates and positive 24-hour limit |
| `POST /api/v1/weather/raw`, `.../raw/batch` | v9 `WeatherRaw`; active Davis live outbox uses batch, HA exporter can also use raw when enabled | Preserve writes; station is required for the new reads, so data from other stations is not merged |
| `POST /api/v1/weather/archive/batch` | v9 `WeatherArchive`; Davis archive outbox | Preserve separate archive observations and console-local metadata |
| `GET /api/v1/weather/raw/recent` | v9 `WeatherRaw`, public paged API | Preserve array shape; optional UTC dates within 24 hours and timestamp/ID continuation |
| `GET /api/v1/weather/hourly/recent` | v9 `WeatherRaw`, hourly SQL aggregation | Public 24-hour window; no dependency on an aggregate-table writer |
| New website DI boundary | `IV9WeatherQueryService`, scoped with `HvoV9DbContext` and existing `TimeProvider` | Explicit latest/current/raw history/archive history typed queries |

`WeatherController` is the repository consumer of `IWeatherService`; current website
components do not consume it. This inventory describes source code, not deployed
processes. **External production writers to legacy tables, users of the legacy API,
and external writers to v9 aggregates remain unknown.** Inventory those operations
before proposing any separately reviewed versioned transition. The existing legacy
current-weather method retains its host-local day behavior; new UTC semantics do not retroactively
change old contracts or reinterpret their historical timestamps.

Davis station identity comes from `Station:StationId` (validated against the edge
source ID); callers must supply that configured station explicitly. The service
does not guess a production station, choose among stations, normalize identifiers,
or infer acquisition ownership from measurements. SQL identifier matching follows
the existing database collation and station/time unique identity.

## Query semantics

- Station identifiers must be trimmed, nonempty and at most 64 characters.
- `GetLatestAsync` returns the newest raw observation at or before the injected
  UTC clock, ordered by observation time and ID. No matching data returns null;
  future-dated rows and other stations cannot displace the latest observation.
- `GetCurrentAsync` reports NoData, Current or Stale alongside the latest raw
  observation and its age. The default stale threshold is 5 minutes; callers may
  choose a positive threshold up to 31 days. Age equal to the threshold is stale.
  NoData has a null observation and age. Query/database/cancellation errors propagate;
  they are not represented as missing data. A refresh request reevaluates freshness.
- Raw and archive histories remain separate; there is no automatic archive fallback,
  resampling or cross-table deduplication. Inputs are `DateTimeOffset` instants
  normalized to UTC. Ranges are half-open `[start,end)`, positive and at most 31 days.
  Rows after the clock are excluded even when end is in the future.
- Page sizes 1–1000 are validated rather than silently clamped. Rows ascend by time
  then ID; `HasMore` and `NextAfterUtc` expose continuation explicitly. Continue with
  the same station/range and the returned exclusive timestamp cursor. Existing unique
  station/time indexes make that cursor unambiguous. Empty ranges return an empty
  page and no continuation. A page is a query result, not a transactionally frozen
  snapshot: later-arriving historical rows before a cursor require starting a fresh
  history query. New website callers must follow continuation or present paging;
  the first page must not be presented as complete history when HasMore is true.
- Database reads project bounded DTO fields and use no tracking. Measurements retain
  stored units and nulls; no zero filling, interpolated weather, synthetic extremes
  or inferred rainfall totals are added. Raw persistence currently stores only a
  subset of the Davis live contract; inside readings, calculated heat indices and
  daily/monthly/yearly live rain fields are not fabricated from absent raw columns.
  Archive rainfall is the stored interval measurement and rain rate is separate.

SQL `datetime2` does not preserve .NET Kind. DTO `RecordedAtUtc`, query boundaries
and checked/query timestamps explicitly have UTC Kind after materialization;
`ConsoleRecordedAtLocal` remains Unspecified metadata. Display requires an explicit
zone. For an interactive refresh, with an injected `IServiceScopeFactory` named
`scopeFactory`, for example:

```csharp
await using var scope = scopeFactory.CreateAsyncScope();
var weather = scope.ServiceProvider.GetRequiredService<IV9WeatherQueryService>();
var current = await weather.GetCurrentAsync(stationId, cancellationToken: ct);
if (current.Observation is { } observation)
{
    var displayed = TimeZoneInfo.ConvertTime(
        new DateTimeOffset(observation.RecordedAtUtc), selectedDisplayZone);
}
```

Keep the UTC instant as identity. Repeated display hours across daylight-saving
transitions are distinct observations with different UTC instants/offsets. Console
local timestamps are never used as UTC query keys. Day views should explicitly
derive start/end instants from their chosen zone; this service does not infer a
display zone from the host.

## Relational and rollout assumptions

The existing migration chain provides WeatherRaw and WeatherArchive plus their
unique station/time indexes. No schema/index migration, data backfill, package or
production setting changes are introduced. Actual SQL tests exercise clean bootstrap
and supported prior-schema upgrades, authenticated Davis raw/archive ingestion,
replay, station isolation, latest/freshness, UTC offset ranges, half-open boundaries,
pagination, null measurements and nontracking projection. Fast SQLite tests cover
validation, no-data/future cases, inclusive freshness, cancellation and repeated
display-hour semantics; they do not establish SQL translation by themselves.

#400 retains retry durability ownership and #401 retains source authorization/proxy
ownership. This read-only boundary edits neither write path. #346 circular-buffer
recovery, physical station scanning and #372 SDK retirement remain separate. This
change makes the service available for new pages, without introducing such pages,
changing legacy APIs, deploying or claiming operational cutover.
