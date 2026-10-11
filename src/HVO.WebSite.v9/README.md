# HVO.WebSite.v9

Main observatory dashboard for Hualapai Valley Observatory. Built with ASP.NET Core 10 and Blazor Server (SSR). Serves the web UI and central ingest API, and persists canonical history to SQL Server via Entity Framework Core. Current hosting is self-hosted Docker on `hvo-docker`.

## Purpose

- Serve the HVO web UI using Blazor Server components with the HVO dark theme
- Host versioned REST APIs for weather, BMS and typed power ingest/read access
- Authenticate browser users via Microsoft Entra ID (OIDC + cookie)
- Authenticate hardware services via API keys with scope claims
- Provide health probes for readiness/liveness and detailed diagnostics
- Publish OpenAPI documentation and an interactive API explorer

## Technologies

- .NET 10 / ASP.NET Core
- Blazor Server (SSR) with HVO dark theme
- MVC Controllers for REST API endpoints
- Entity Framework Core (Azure SQL Server) via `HVO.DataModels`
- Microsoft Entra ID OIDC via `Microsoft.Identity.Web`
- API key authentication middleware with SHA-256 hashed keys and scope claims
- API Versioning (`Asp.Versioning.Mvc`) — URL segment versioning, default v1
- Health Checks with EF Core checks for both DB contexts
- OpenAPI (`Microsoft.AspNetCore.OpenApi`) + Scalar UI (`Scalar.AspNetCore`)

## API Endpoints

### Weather ingest & read

| Method | Route | Auth | Description |
|--------|-------|------|-------------|
| `POST` | `/api/v1/weather/raw` | API key `ingest:weather` | Ingest a single raw weather reading |
| `POST` | `/api/v1/weather/raw/batch` | API key `ingest:weather` | Ingest a batch of raw readings (idempotent) |
| `GET`  | `/api/v1/weather/raw/recent` | Anonymous | Raw history within 24 hours (default 100, maximum 500 per page) |
| `GET`  | `/api/v1/weather/hourly/recent` | Anonymous | Hourly aggregates within 24 hours (default 25, maximum 168) |
| `GET`  | `/api/v1/weather/latest` | API key `read:weather` or `read:api` | Latest weather record |
| `GET`  | `/api/v1/weather/current` | Anonymous | Live current conditions with today's highs/lows |
| `GET`  | `/api/v1/weather/highs-lows` | Anonymous | Highs/lows within a positive date range of at most 24 hours |

`/api/v1/weather/current` reads the newest live gateway observation from `v9.WeatherRaw`.
Clients can request `https://www.hualapaivalleyobservatory.org/api/v1/weather/current`
without an API key. Raw history, hourly history and highs/lows are also public;
`/weather/latest` and weather ingest retain their API-key policies.
If an optional `X-Api-Key` header is supplied, it must contain a valid active key.
Its response keeps the existing `current` and `todaysExtremes` fields. Daily extremes are
computed from observations for the same station during the server's local day (UTC in
the production container), with observation timestamps returned as UTC instants. Missing
measurements remain `null`; indoor readings, pressure trend, rain totals/rate, heat index,
wind chill, and sunrise/sunset are not stored in `WeatherRaw`. Raw `rainfallInches` is not
used as a substitute for daily rainfall. If the newest observation is from an earlier
day, its acquisition timestamp is preserved and `todaysExtremes` is `null`. An empty
live table returns 404.

`/weather/latest` still uses legacy weather database objects. The public history endpoints
read current `v9.WeatherRaw`; see [public weather history](../../docs/development/public-weather-history.md)
for dates, paging, hourly aggregation and missing-data semantics.

### BMS ingest

| Method | Route | Auth | Description |
|--------|-------|------|-------------|
| `POST` | `/api/v1/bms/readings` | API key `ingest:bms` | Ingest batch BMS readings with alarm/config detection |

### Infrastructure

| Route | Description |
|-------|-------------|
| `/health` | Full diagnostic health report (JSON) |
| `/health/ready` | Readiness probe — database checks |
| `/health/live` | Liveness probe — always 200 if process is running |
| `/openapi/v1.json` | OpenAPI specification |
| `/scalar/v1` | Interactive API explorer (Development only) |

## Roles & Scopes

**Entra ID roles** (`AppRoles.cs`): `Admin`, `User`

**API key scopes** (`ApiScopes.cs`): `ingest:weather`, `ingest:bms`, `ingest:images`, `ingest:power`, `read:weather`, `read:power`, `read:api`

## Configuration

| Key | Description |
|-----|-------------|
| `ASPNETCORE_URLS` | Current self-hosted Compose uses `http://+:8080`, with external TLS/proxy ownership. Optional local HTTPS needs runtime-mounted certificate configuration |
| `EnableHttpsRedirect` | Deliberate proxy/listener policy; self-hosted Compose defaults false |
| `AzureAd:*` | Microsoft Entra ID OIDC settings |
| `ConnectionStrings:HualapaiValleyObservatory` | SQL Server target; startup seeding applies EF migrations, so a development run needs an explicitly approved database |
| `Seeding:PowerApiKey` | Optional write-only `ingest:power` key seed for power gateways |
| `Seeding:WeatherReadApiKey` | Optional `read:weather` key seed for weather API clients |
| `Seeding:PowerReadApiKey` | Optional `read:power` key seed for operational power API verification |
| `ASPNETCORE_Kestrel__Certificates__Default__*` | TLS certificate path and password for local container HTTPS |

Configuration should be split by purpose:

- secrets belong in Key Vault
- deployment and hosting values belong in appsettings plus environment overrides
- runtime-editable non-secret values should move into the `v9.SiteConfiguration` table instead of accumulating in environment variables

The website now includes `ISiteConfigurationService`, which reads and caches `v9.SiteConfiguration` values for runtime use.

See [current self-hosted deployment](../../deploy/hvo-docker/README.md),
[Data Protection](../../docs/WEBSITE_DATA_PROTECTION.md) and
[hardware-free build/browser orientation](../../README.md#first-local-success).
The [former ACA record](../../docs/archive/website-container-app.md) preserves
historical identity/key migration evidence; it is not current deployment guidance.

See `Program.cs` for service registration and middleware pipeline.

Current interactive dashboards use scoped typed query operations and owned refresh/
disposal rather than circuit-held DbContexts. [UTC power history](../../docs/development/power-history-utc.md),
[power observation identity](../../docs/development/power-observation-identity.md),
[weather queries](../../docs/development/canonical-weather-queries.md),
[retry durability](../../docs/development/canonical-ingest-retries.md) and
[source/auth/proxy trust](../../docs/development/ingest-trust-boundaries.md) own
the detailed contracts beyond the orientation table above. Actual current routes
live in [controllers](Controllers/), including separate full weather archive and
atomic SmartShunt observation ingest. The [browser guide](../../tests/HVO.WebSite.PlaywrightTests/README.md)
describes the hardware-free real-host fixture rather than an operational website run.

[Grouped documentation and all project owners](../../docs/README.md#project-documentation-owners) provides the current navigation entry.
