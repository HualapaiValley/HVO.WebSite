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

### Phase 1 — Foundation (Current)

- [x] Upgrade to .NET 10, zero warnings/errors
- [x] Remove hardcoded credentials; use environment variables
- [x] Remove Playground project
- [x] Audit and snapshot legacy `dbo` schema (`HVO.Database` SQL project)
- [x] Add test projects: unit (MSTest + Moq), API (WebApplicationFactory), E2E (Playwright)
- [ ] Create `HvoV9DbContext` with initial EF Core migration
- [ ] Define v9 schema entities listed above
- [x] Configure Entra ID integration in `Program.cs` via `Microsoft.Identity.Web`
- [ ] Implement API key middleware for ingest endpoints
- [ ] Implement base ingest endpoints (`POST /api/v1/weather/raw`, `POST /api/v1/images`)
- [ ] Implement base read endpoints (`GET /api/v1/weather/latest`, `GET /api/v1/weather/highs-lows`)

### Phase 2 — Weather Dashboard

- [ ] Weather summary page (current conditions, 24h trend)
- [ ] Historical weather charts (hourly/daily/monthly)
- [ ] Data retention background jobs (prune raw after 2 months, prune minute after 6 months)
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
