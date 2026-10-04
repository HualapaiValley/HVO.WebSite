# Canonical weather queries for new website features

New website features should inject `IV9WeatherQueryService`. It reads only
`HvoV9DbContext.WeatherRaw` and `WeatherArchive`, where the Davis outbox currently
sends its canonical observations. `IWeatherService` remains the legacy API boundary.
Registering the new service does not redirect, retire or change any existing route.
There is no new public HTTP endpoint or change to read/ingest authorization.

## Repository inventory and compatibility decision

| Boundary | Source and current repository consumer | Decision |
|---|---|---|
| `GET /api/v1/weather/latest` | `WeatherController` → `IWeatherService` → legacy `DavisVantageProConsoleRecordsNew` through `HvoDbContext` | Preserve response shape, legacy selection and WeatherRead policy |
| `GET /api/v1/weather/current` | Same legacy table plus `sp__GetWeatherRecordHighLowSummary` | Preserve current/extremes contract; no silent v9 fallback |
| `GET /api/v1/weather/highs-lows` | Legacy stored procedure through `HvoDbContext` | Preserve historical access, units and existing date behavior |
| `POST /api/v1/weather/raw`, `.../raw/batch` | v9 `WeatherRaw`; active Davis live outbox uses batch, HA exporter can also use raw when enabled | Preserve writes; station is required for the new reads, so data from other stations is not merged |
| `POST /api/v1/weather/archive/batch` | v9 `WeatherArchive`; Davis archive outbox | Preserve separate archive observations and console-local metadata |
| `GET /api/v1/weather/raw/recent` | v9 `WeatherRaw`, existing paginated API | Preserve endpoint/query/DTO behavior |
| `GET /api/v1/weather/hourly/recent` | v9 `WeatherHourly`, existing API | Preserve endpoint; new service does not assume a canonical aggregate writer |
| New website DI boundary | `IV9WeatherQueryService`, scoped with `HvoV9DbContext` and existing `TimeProvider` | Explicit latest/current/raw history/archive history typed queries |

`WeatherController` is the repository consumer of `IWeatherService`; current website
components do not consume it. This inventory describes source code, not deployed
processes. **External production writers to legacy tables, users of the legacy API,
and external writers to v9 aggregates remain unknown.** Inventory those operations
before proposing any separately reviewed versioned transition. The existing legacy
service retains its host-local day behavior; new UTC semantics do not retroactively
change old contracts or reinterpret their historical timestamps.

Davis station identity comes from `Station:StationId` (validated against the edge
source ID); callers must supply that configured station explicitly. The service
does not guess a production station, choose among stations, normalize identifiers,
or infer acquisition ownership from measurements. SQL identifier matching follows
the existing database collation and station/time unique identity.

## Query semantics

- Station identifiers must be trimmed, nonempty and at most64 characters.
- `GetLatestAsync` returns the newest raw observation at or before the injected
  UTC clock, ordered by observation time and ID. No matching data returns null;
  future-dated rows and other stations cannot displace the latest observation.
- `GetCurrentAsync` reports NoData, Current or Stale alongside the latest raw
  observation and its age. The default stale threshold is5 minutes; callers may
  choose a positive threshold up to31 days. Age equal to the threshold is stale.
  NoData has a null observation and age. Query/database/cancellation errors propagate;
  they are not represented as missing data. A refresh request reevaluates freshness.
- Raw and archive histories remain separate; there is no automatic archive fallback,
  resampling or cross-table deduplication. Inputs are `DateTimeOffset` instants
  normalized to UTC. Ranges are half-open `[start,end)`, positive and at most31 days.
  Rows after the clock are excluded even when end is in the future.
- Page sizes1–1000 are validated rather than silently clamped. Rows ascend by time
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
zone, for example:

```csharp
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
