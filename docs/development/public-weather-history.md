# Public weather history

The website exposes anonymous GET requests for `/api/v1/weather/raw/recent`,
`/api/v1/weather/hourly/recent` and `/api/v1/weather/highs-lows`. They use current
v9 raw observations; hourly reads aggregate on request instead of depending on
the unpopulated WeatherHourly table. Highs/lows no longer invokes the legacy
stored procedure. Weather ingest and the legacy latest endpoint retain authentication.
Presenting an invalid optional API key still returns 401.

## Dates and stations

All three accept optional ISO 8601 `start` and `end` instants and `stationId`.
Use `Z` or an explicit offset; encode `+` in positive offsets as `%2B`.
Dates normalize to UTC and define a half-open range: start included, end excluded.
The end must follow the start by no more than **24 elapsed hours**. Invalid dates,
reversed/equal endpoints, unsupported date arithmetic and longer ranges return 400.

- Neither date: the preceding 24 hours ending at the injected UTC clock.
- Start only: end is 24 hours after start.
- End only: start is 24 hours before end.
- Both: use the specified window, including exactly 24 hours.

Highs/lows also accepts its previous `startDate` and `endDate` names; conflicting
aliases return 400. Raw/hourly queries without a station include all stations but
keep their observations/summaries separate. Highs/lows without a station uses the
most recently observed station within the requested window. Supply `stationId`
to select a particular station. Highs/lows returns 404 for a station/window with
no observations; existing observations with missing sensor fields yield null extremes.
Raw/hourly queries with no observations return an empty array.

Example for the October 10–11 Arizona overnight window (18:00–08:00):

```text
https://www.hualapaivalleyobservatory.org/api/v1/weather/raw/recent?stationId=hvo-davis-01&start=2026-10-11T01:00:00Z&end=2026-10-11T15:00:00Z&limit=500
https://www.hualapaivalleyobservatory.org/api/v1/weather/hourly/recent?stationId=hvo-davis-01&start=2026-10-11T01:00:00Z&end=2026-10-11T15:00:00Z
https://www.hualapaivalleyobservatory.org/api/v1/weather/highs-lows?stationId=hvo-davis-01&start=2026-10-11T01:00:00Z&end=2026-10-11T15:00:00Z
```

## Raw paging

Raw responses keep the existing array shape and newest-first order, with ID
descending to break equal timestamp ties. `limit` remains 1–500, default 100.
Acquisition timestamps explicitly identify UTC. Response headers `X-Weather-Start`
and `X-Weather-End` report the resolved window, including defaults.

When another page exists, `X-Weather-Next-Before` and `X-Weather-Next-Before-Id`
provide an exclusive timestamp/ID cursor. Continue with the **same resolved dates,
station and limit**, adding `before` and `beforeId` from those headers. Both cursor
fields are required together; the ID must be positive and the timestamp inside the
window. Encode the timestamp as a query value. Absence of the next-page headers
means this page completes the available range. This permits retrieving an overnight
window without an unbounded response or losing same-time records across stations.
Later-arriving records can require a fresh query; paging is not a frozen snapshot.

## Hourly and extreme values

Hourly aggregation groups by station and UTC clock hour, using only observations
inside the selected window. A 24-hour window beginning between hour boundaries can
touch 25 partial hours; the default limit is 25. Explicit limits remain 1–168 but
never expand the date window. Hours without observations are omitted. Average/min/max
calculations ignore missing sensor values; fields missing for an entire hour remain
null. Rain totals remain unknown if any included rainfall measurement is missing.
Dominant wind direction is the most frequent available direction, with the smallest
degree breaking ties. Hourly `id` identifies the first contributing raw record, and
`periodStart` is the UTC hour boundary, which may precede the requested start for a
partial first hour. The window headers retain the actual requested boundaries.

Highs/lows keeps the existing flat JSON property names and response envelope. Values
retain available precision; unknown metrics and their acquisition times are nullable
instead of synthetic zeros/default dates. Tied extremes retain the earliest acquisition
time, and all times identify UTC. Indoor/heat-index/wind-chill fields remain null because
they are absent from current raw storage.

The Davis gateway's daily/monthly/yearly rain counters and rain rate are not persisted
in raw storage. These APIs preserve that absence; raw `rainfallInches` is not a
substitute for the missing cumulative counters. The 24-hour public HTTP limit is
separate from the existing internal canonical query service's 31-day limit.
