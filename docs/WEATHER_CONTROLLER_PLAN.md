# Weather Controller Plan

## Purpose

This document captures the post-UI work needed for the weather controller and supporting services once the Davis weather dashboard UI is stable.

The current priority is finishing the UI and validating the raw/live data flow. After that, the weather controller layer will need to support derived values, summaries, astronomy data, and data-quality handling so the UI can show more than just direct console fields.

---

## Goals

1. Keep raw weather ingest and display working with minimal transformation.
2. Add derived values in a controlled way after the UI is finalized.
3. Separate raw station data from computed display values.
4. Make the controller/service layer explicit about what is live, persisted, derived, stale, or unavailable.
5. Avoid putting heavy calculation logic directly into Blazor page code-behind.

---

## Current State

The Davis application already provides:

- Live station connectivity and LOOP-based current conditions
- Local persisted weather data in the SQLite outbox
- Dashboard rendering with placeholder behavior for missing fields
- Archive-based summary loading for some 24-hour values
- A local current-conditions endpoint

The next phase is not more UI-first work in the controller. The next phase is enabling the controller/service layer to provide richer data once the rest of the UI has been reviewed.

---

## Responsibility Boundaries

### Raw Data

Raw data comes from:

- Current live station readings
- Persisted local outbox records
- Archive records from the console

Raw data should remain as close as possible to the source values.

### Derived Data

Derived data includes:

- Daily highs and lows
- Peak solar today
- Trend summaries
- Rolling summaries
- Daylight-related summaries
- Moonrise, moonset, phase, and illumination

Derived data should be computed in a dedicated controller/service layer, not scattered across UI components.

### Presentation State

The UI should be able to distinguish:

- Live value
- Latest persisted value
- Derived value
- Stale value
- Missing value

---

## Required Feature Areas

## 1. Weather-Derived Daily Metrics

These are the first calculations to support because they are directly useful to the dashboard and can be computed from persisted weather history.

Planned items:

- Outside temperature daily high
- Outside temperature daily low
- Outside pressure daily high
- Outside pressure daily low
- Peak solar today
- Wind gust high for the day
- Rain rollups beyond raw console fields where needed
- Data freshness and age indicators

Primary source:

- Local persisted records
- Archive history where appropriate

Notes:

- These should be observatory-local-day calculations, not naive UTC day calculations.
- The controller should own the day-boundary logic.

---

## 2. Rolling Summary Windows

Once daily metrics are stable, the next layer should support rolling summaries.

Planned items:

- Last 1 hour summaries
- Last 6 hour summaries
- Last 12 hour summaries
- Last 24 hour summaries
- Today vs yesterday comparisons
- Pressure trend summary when console trend is unavailable or insufficient
- Daylight window summaries from solar history

Potential outputs:

- Min/max/average values
- Peak timestamps
- First non-zero solar reading
- Last non-zero solar reading

---

## 3. Astronomy Data

Astronomy values should be treated as a separate concern from weather packet parsing.

Planned items:

- Moonrise
- Moonset
- Moon phase name
- Illumination percent
- Moon age
- Optional next full moon / next new moon
- Optional solar altitude / lunar altitude if the UI later requires them

Recommended approach:

- Use a dedicated astronomy service
- Feed it observatory latitude, longitude, and timezone
- Cache results by date because the values change slowly relative to weather data

Notes:

- Do not hand-roll moon calculations unless there is no suitable .NET library available.
- Keep astronomy logic testable and isolated.

---

## 4. Data Quality and Fallback Rules

The controller/service layer must define how to handle incomplete or stale data.

Planned items:

- Detect partial live packets
- Detect null/missing sensor fields
- Distinguish current live status from displayable weather completeness
- Mark stale values with age metadata
- Preserve `--` behavior when no acceptable value exists
- Optionally expose whether a displayed value is live, persisted, or derived

Examples:

- Live connection active, but outside temperature missing
- Most recent persisted record exists, but still has null solar/UV
- Daytime-derived value available for summary cards, but not for current-condition cards

The controller layer should make these rules explicit rather than embedding them ad hoc in UI logic.

---

## 5. Controller/Service Architecture

The weather controller should not become a giant all-in-one class.

Recommended service split:

- `CurrentConditionsService`
- `WeatherSummaryService`
- `AstronomyService`
- `WeatherDisplayCompositionService`

Suggested responsibilities:

### CurrentConditionsService

- Expose latest live reading
- Expose latest persisted reading
- Expose data freshness metadata
- Handle startup fallback behavior

### WeatherSummaryService

- Compute daily highs/lows
- Compute rolling summaries
- Compute peak solar and similar derived metrics
- Query persisted history efficiently

### AstronomyService

- Compute moonrise/moonset
- Compute moon phase and illumination
- Cache daily astronomy outputs

### WeatherDisplayCompositionService

- Build a dashboard-ready view model
- Label values as live, derived, stale, or missing
- Keep page code-behind thin

---

## 6. Persistence and Performance

Not every derived value should be stored immediately.

Initial strategy:

- Compute most derived values on demand
- Cache expensive calculations where practical
- Only materialize persistent summary tables if query cost becomes a problem

Likely candidates for cached or persisted summaries later:

- Daily weather summary rows
- Astronomy values by date
- Precomputed high/low/peak statistics

Notes:

- Start simple
- Measure before adding persistence complexity

---

## 7. Testing Requirements

All derived-data work should come with targeted tests.

Planned test categories:

- Daily high/low calculation tests
- Rolling window summary tests
- Midnight/day-boundary tests in observatory-local time
- Partial/null input data tests
- Stale/fallback behavior tests
- Astronomy regression tests for known dates and coordinates
- End-to-end controller/view-model tests for representative dashboard states

Representative test scenarios:

- Live data present and complete
- Live data present but partial
- No live data, persisted record available
- Persisted record available but incomplete
- No current data, no persisted data
- Daylight calculations crossing sunrise/sunset boundaries

---

## Implementation Order

After the remaining UI work is complete, implement in this order:

1. Daily highs/lows and peak solar
2. Data freshness metadata and fallback labeling
3. Rolling summaries and trend calculations
4. Astronomy service for moonrise, moonset, phase, and illumination
5. Optional richer comparison metrics and precomputed summaries

This order prioritizes high-value, low-risk weather calculations before astronomy and before any persistence-heavy optimization.

---

## Open Questions

These decisions can wait until the UI review is done:

1. Should derived values be exposed from the existing local weather endpoint or from a new controller endpoint?
2. Should astronomy values be calculated locally or via an external source?
3. How explicit should the API be about value provenance: live vs persisted vs derived?
4. Which summary values should remain query-time calculations and which should eventually be materialized?
5. Should the same weather-summary logic later be shared with the main website project?

---

## Recommended Next Step After UI Review

Once the remaining pages are reviewed, convert this plan into an implementation backlog with issue-sized tasks:

- Controller/service refactor tasks
- Derived weather metric tasks
- Astronomy tasks
- Data-quality/fallback tasks
- Testing tasks

That backlog should be ordered so the dashboard can gain richer data incrementally without destabilizing the existing live data path.
