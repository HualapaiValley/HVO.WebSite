# HVO.WebSite — v9 Architecture & Development Plan

## Overview

HVO.WebSite v9 is a clean-slate rebuild of the Hualapai Valley Observatory website on .NET 10. The legacy `dbo` schema (SQL Server, Azure) remains read-accessible but is **not** the target for new development. All v9 features use a new `v9` schema owned exclusively by EF Core migrations via `HvoV9DbContext`.

---

## Key Architectural Decisions

### Database Strategy

- **No bridge views** — the legacy `dbo` schema is preserved as a reference (see `HVO.Database` SQL project) but v9 code does not build on top of it
- **Clean start** — all new entities live in the `v9` schema; `HvoV9DbContext` owns migrations
- **Dual contexts**: `HvoDbContext` (legacy read-only access to `dbo`) + `HvoV9DbContext` (v9 read/write to `v9` schema)
- **Connection string**: `ConnectionStrings__HualapaiValleyObservatory` environment variable — no hardcoded credentials

### Weather Data Retention

| Granularity | Retention |
|-------------|-----------|
| Raw (every reading) | 2 months |
| Per-minute aggregate | 6 months |
| Hourly aggregate | Forever |

### Authentication

- **Provider**: Microsoft Entra ID (Azure AD) — `Microsoft.Identity.Web` (no third-party auth libraries)
- **Roles**: `User` (read dashboard) and `Admin` (manage site, view logs)
- **Claims**: Role-based with extension points for future custom claims
- No local accounts; all identity managed via Entra

### API Design

- **Ingest endpoints**: Authenticated via API key middleware (header-based); used by weather stations and camera systems
- **Read endpoints**: Authenticated via Entra bearer token (or anonymous for public dashboard)
- **All data access** goes through the API layer — no direct DB calls from Razor pages/components
- RESTful versioned endpoints under `/api/v1/`

---

## v9 Schema Entities (EF Core Migrations)

| Entity | Purpose |
|--------|---------|
| `WeatherRaw` | Raw station readings, 2-month retention |
| `WeatherMinute` | Per-minute aggregates, 6-month retention |
| `WeatherHourly` | Hourly aggregates, retained forever |
| `ImageMetadata` | All-sky camera image records |
| `AlertLog` | System alerts and notifications |
| `SiteConfiguration` | Key/value site settings |

---

## Phase Plan

### Phase 1 — Foundation (Complete)

- [x] Upgrade to .NET 10, zero warnings/errors
- [x] Remove hardcoded credentials; use environment variables
- [x] Remove Playground project
- [x] Audit and snapshot legacy `dbo` schema (`HVO.Database` SQL project)
- [x] Add test projects: unit (MSTest + Moq), API (WebApplicationFactory), E2E (Playwright)
- [x] Create `HvoV9DbContext` with initial EF Core migration
- [x] Define v9 schema entities listed above
- [x] Configure Entra ID integration in `Program.cs` via `Microsoft.Identity.Web`
- [x] Implement API key middleware for ingest endpoints
- [x] Implement base ingest endpoints (`POST /api/v1/weather/raw`)
- [x] Implement base read endpoints (`GET /api/v1/weather/v9/raw/recent`, `GET /api/v1/weather/v9/hourly/recent`)
- [x] Implement Admin and User Entra app roles with `[Authorize(Policy = "AdminOnly")]` protected page
- [x] Integration tests for `/admin` page authorization (unauthenticated → OIDC challenge, wrong role → AccessDenied, Admin role → 200)

---

### Phase 1.1 — Weather Station Data Acquisition

**Goal:** Live weather data flows from the Davis Vantage Pro 2 console → SQLite outbox → web API → database.

#### New project: `src/HVO.Hardware.DavisVantagePro2`

A .NET Worker Service deployed as a Docker container (multi-arch: `linux/amd64` + `linux/arm64` for Raspberry Pi).

**Connection:** WeatherLink IP adapter at `192.168.2.121:22222` — TCP socket tunneling the Davis serial binary protocol. `TcpClient` replaces `SerialPort`; the binary framing (LOOP2, DMPAFT, CRC-CCITT-16) is identical to serial.

**Components:**

| Component | Purpose |
|---|---|
| `DavisConsoleClient` | TCP connection management — wake, send commands, read binary frames, CRC validation |
| `DavisLoop2Packet` | Maps the 99-byte LOOP2 binary structure (little-endian, `struct`-style) |
| `DavisArchiveRecord` | Maps 52-byte archive records from DMPAFT (used for catchup on startup) |
| `DavisProtocolConstants` | CRC table, magic bytes, packet type codes |
| `WeatherStationWorker` | `BackgroundService` — polls LOOP2 every N seconds, runs DMPAFT catchup on start |
| `OutboxForwarder` | `BackgroundService` — sweeps pending outbox records, POSTs to web API, marks sent |
| `OutboxDbContext` | EF Core + SQLite — owns the `OutboxRecord` table |
| `StationStatusController` | Minimal local API: `GET /status` returns latest record + outbox stats |
| Blazor/Razor status page | Single-page local UI: latest reading, station health, pending/sent counts |

**Offline-first outbox (transactional, FIFO, guaranteed delivery):**

```
READ LOOP2 from TCP
  │
  ├─ BEGIN TRANSACTION
  │    INSERT OutboxRecord (payload, status='Pending', sequence_id=AUTOINCREMENT)
  │  COMMIT  ← durable; crash-safe from this point
  │
OutboxForwarder (background sweep):
  │
  ├─ SELECT TOP 1 WHERE status='Pending' ORDER BY sequence_id ASC
  ├─ POST /api/v1/weather/v9/raw  (X-Api-Key header)
  │    on HTTP 2xx  → UPDATE status='Sent', sent_at=now
  │    on failure   → UPDATE attempt_count++, next_retry_at=now+exponential_backoff
  │    (row never deleted — audit trail preserved)
```

No row is deleted or marked sent until HTTP 2xx is confirmed. Process crash → restart picks up from last `Pending` row. Exponential backoff with a configurable max (e.g., 5 minutes) prevents thundering herd on reconnect.

**LOOP2 fields captured → `WeatherRaw` (see schema changes below):**

From the Davis serial protocol spec (verified against WeeWX vantage driver):

| LOOP2 field | Meaning | `WeatherRaw` column |
|---|---|---|
| `outTemp` | Outside temperature (°F × 10, signed) | `TemperatureF` |
| `outHumidity` | Outside relative humidity (%) | `HumidityPercent` |
| `dewpoint` | Dew point (°F, console-computed) | `DewPointF` |
| `heatindex` | Heat index (°F, console-computed) | `HeatIndexF` *(new)* |
| `windchill` | Wind chill (°F, console-computed) | `WindChillF` *(new)* |
| `barometer` | Station-corrected barometric pressure (inHg × 1000) | `BarometricPressureInHg` |
| `windSpeed` | Instantaneous wind speed (mph) | `WindSpeedMph` |
| `windGust10` | 10-min wind gust (mph) | `WindGustMph` |
| `windDir` | Instantaneous wind direction (degrees) | `WindDirectionDegrees` |
| `rainRate` | Instantaneous rain rate (in/hr, decoded from bucket tips) | `RainRateInchesPerHour` *(renamed)* |
| `dayRain` | Cumulative rain since midnight (in, decoded from bucket tips) | `DailyRainInches` *(renamed)* |
| `UV` | UV index (× 10) | `UvIndex` |
| `radiation` | Solar radiation (W/m²) | `SolarRadiationWm2` |
| `inTemp` | Inside console temperature (°F × 10) | `InsideTemperatureF` *(new)* |
| `inHumidity` | Inside console humidity (%) | `InsideHumidityPercent` *(new)* |

> **Note on `RainfallInches` rename:** The legacy field was ambiguous (rate? daily? storm?). Per the Davis serial protocol, `rainRate` is instantaneous rate and `dayRain` is daily cumulative. These become two separate columns. The existing `RainfallInches` column is removed in the migration.

**`WeatherRaw` schema changes required (new EF Core migration):**

- Remove: `RainfallInches`
- Add: `RainRateInchesPerHour`, `DailyRainInches`, `HeatIndexF`, `WindChillF`, `InsideTemperatureF`, `InsideHumidityPercent`
- Add unique index on `(StationId, RecordedAt)` — enables idempotent ingest (DMPAFT catchup retries)
- Ingest endpoint handles unique constraint violation as HTTP 200/idempotent (not 500)

**Local web UI (no auth — LAN-only):**

Single Blazor/Razor page at the container's HTTP port showing:
- Latest reading (all fields, timestamp, age)
- Station connectivity status (connected / last seen)
- Outbox: pending count, last-sent timestamp, last error

**Docker:**

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
# multi-arch: --platform linux/amd64 or linux/arm64
```

`docker-compose.yml` for local dev with environment overrides for host IP, API endpoint, API key.

**Configuration (environment variables / appsettings):**

| Setting | Purpose |
|---|---|
| `Davis:Host` | WeatherLink IP address (`192.168.2.121`) |
| `Davis:Port` | TCP port (`22222`) |
| `Davis:PollingIntervalSeconds` | How often to request LOOP2 (default: `10`) |
| `Davis:ArchiveCatchupOnStartup` | Run DMPAFT on startup (`true`) |
| `Api:Endpoint` | Ingest API URL |
| `Api:Key` | API key (with `ingest:weather` scope) |
| `Outbox:MaxRetryAttempts` | Before giving up on a record |
| `Outbox:MaxBackoffSeconds` | Cap for exponential backoff |

**Tests:**

| Test | Type |
|---|---|
| LOOP2 binary packet parsing (known byte sequence → expected field values) | Unit |
| CRC-CCITT-16 validation (valid and corrupt packet) | Unit |
| Rain bucket tip decoding (all 3 bucket types) | Unit |
| Outbox: record inserted on read, not deleted on forward failure | Unit |
| Outbox: record marked sent only on HTTP 2xx | Unit |
| Outbox: FIFO ordering (sequence_id respected) | Unit |
| Outbox: exponential backoff increments correctly | Unit |
| End-to-end: LOOP2 parsed → outbox written → forwarded → `WeatherRaw` in DB | Integration |

#### Web app changes for Phase 1.1

- `WeatherAggregationService` (`BackgroundService` in `HVO.WebSite.v9`):
  - Every 60 seconds: aggregate `WeatherRaw` rows in the last completed minute → upsert `WeatherMinute`
  - Every 60 minutes: aggregate `WeatherMinute` rows in the last completed hour → upsert `WeatherHourly`
  - Idempotent: upsert by `(StationId, PeriodStart)` — safe to re-run

**Aggregation strategy per field** (derived from legacy stored procedures in `dbo`):

| Field | Method | Rationale |
|-------|--------|-----------|
| `TemperatureF` | AVG + MIN + MAX | Range matters for daily extremes |
| `InsideTemperatureF` | AVG | Stable, no extremes needed |
| `HumidityPercent` | AVG | |
| `InsideHumidityPercent` | AVG | |
| `DewPointF` | AVG | Console-computed derived value |
| `HeatIndexF` | AVG | Console-computed derived value |
| `WindChillF` | AVG | Console-computed derived value |
| `BarometricPressureInHg` | AVG | |
| `WindSpeedMph` | AVG + MIN + MAX | Range captures calm and peak |
| `WindGustMph` | MAX | Highest gust in the period is the meaningful value |
| `WindDirectionDegrees` | Vector average (sin/cos) | Scalar average is meaningless across 0°/360° boundary |
| `RainRateInchesPerHour` | AVG | Instantaneous rate |
| `DailyRainInches` | MAX | Running cumulative total from console — last (highest) value in period |
| `SolarRadiationWm2` | AVG | |
| `UvIndex` | AVG | |

> **Note:** `WeatherMinute` and `WeatherHourly` entities will need additional columns to hold min/max alongside avg (e.g., `TemperatureFMin`, `TemperatureFMax`, `WindSpeedMphMin`, `WindSpeedMphMax`). If there are questions about how any specific field should roll up, ask before implementing.

**Phase 1.1 checklist:**

- [ ] `WeatherRaw` schema migration — add/rename rainfall and inside sensor fields, unique index
- [ ] Update ingest endpoint to handle unique constraint as idempotent (200 if duplicate)
- [x] Create `src/HVO.Hardware.DavisVantagePro2` Worker Service project
- [x] Implement `DavisConsoleClient` (TCP, wake, LOOP2, DMPAFT, CRC-CCITT-16)
- [x] Implement `DavisLoop2Packet` binary parser
- [x] Implement `DavisArchiveRecord` binary parser
- [x] Implement SQLite transactional outbox (`OutboxDbContext`, `OutboxRecord`)
- [x] Implement `WeatherStationWorker` (poll LOOP2, write to outbox)
- [x] Implement `OutboxForwarder` (sweep outbox, POST to API, exponential backoff)
- [x] Local status page (latest reading + outbox health)
- [x] Dockerfile (multi-arch: amd64 + arm64)
- [ ] Implement `WeatherAggregationService` in web app (minute + hourly rollups)
- [x] Unit tests: packet parsing, CRC, rain decoding (`Loop2PacketTests`, `CrcCalculatorTests`, `ArchiveRecordTests`)
- [ ] Unit tests: outbox behavior (insert-on-read, no-delete-on-failure, sent-on-2xx, FIFO, backoff)
- [ ] Integration tests: ingest duplicate → idempotent; aggregation correctness
- [x] Zero warnings, zero errors

---

### Phase 1.5 — Data Retention

- [ ] Background job: prune `WeatherRaw` rows older than 2 months
- [ ] Background job: prune `WeatherMinute` rows older than 6 months
- [ ] Tests: retention jobs delete correct rows, leave in-window rows intact

---

### Phase 2 — Weather Dashboard

- [ ] Weather summary page (current conditions, 24h trend)
- [ ] Historical weather charts (hourly/daily/monthly)
- [ ] Image ingest endpoint (`POST /api/v1/images`)
- [ ] Weather station health/status indicator

### Phase 3 — Power Dashboard

- [ ] Solar/battery status display
- [ ] Power history charts
- [ ] Power ingest endpoint

### Phase 4 — All-Sky Viewer

- [ ] Latest all-sky image display
- [ ] Image archive browser (date/time navigation)
- [ ] Image metadata ingest endpoint
- [ ] Timelapse generation or linking

### Phase 5 — Observatory & Security

- [ ] Admin panel (site config, user role management)
- [ ] Alert log viewer
- [ ] Observatory status page (roof state, telescope state)
- [ ] Audit logging for admin actions

---

## Test Strategy

| Layer | Framework | Purpose |
|-------|-----------|---------|
| Unit | MSTest + Moq + FluentAssertions | Controller/service logic with mocked dependencies |
| API | MSTest + WebApplicationFactory | Integration tests against full ASP.NET Core pipeline |
| E2E | Playwright + MSTest | Browser-level tests against running server |

All tests must pass (`dotnet test`) before any PR is created. E2E tests are skipped by default (`[Ignore]`) unless the server is running.

---

## Constraints

- .NET SDK `10.0.203` (pinned in `global.json`)
- Zero build warnings, zero build errors — mandatory
- No `npm`, `npx`, Node.js, or Python
- `az` CLI is available for Azure/Entra resource management
- No heredoc syntax in shell scripts (use file-creation tools instead)
- Central package management via `Directory.Packages.props`
- Branch naming: `feature/<issue#>-<short-desc>`, `fix/<issue#>-<short-desc>`
- Commit style: Conventional commits (`feat:`, `fix:`, `chore:`, `refactor:`, `test:`, `docs:`)
- Merge strategy: Squash merge into `main`

---

## Legacy Schema Reference

The full legacy `dbo` schema (15 tables, 6 views, 40 stored procedures, 3 functions) is captured in `src/HVO.Database/` as a `Microsoft.Build.Sql` SQL project. This is a **read-only reference** — no new development targets the `dbo` schema.
