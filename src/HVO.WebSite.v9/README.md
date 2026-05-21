# HVO.WebSite.v9

Main observatory dashboard for Hualapai Valley Observatory. Built with ASP.NET Core 10 and Blazor Server (SSR). Serves the web UI, hosts the ingest REST API for hardware collectors, and persists all data to Azure SQL via Entity Framework Core.

## Purpose

- Serve the HVO web UI using Blazor Server components with the HVO dark theme
- Host versioned REST APIs for weather and BMS ingest and read access
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
| `GET`  | `/api/v1/weather/raw/recent` | API key `read:weather` | Recent raw readings (paginated, default 100) |
| `GET`  | `/api/v1/weather/hourly/recent` | API key `read:weather` | Recent hourly aggregates (paginated, default 24) |
| `GET`  | `/api/v1/weather/latest` | open | Latest weather record |
| `GET`  | `/api/v1/weather/current` | open | Current conditions with today's highs/lows |
| `GET`  | `/api/v1/weather/highs-lows` | open | Highs/lows for a date range |

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

**API key scopes** (`ApiScopes.cs`): `ingest:weather`, `ingest:bms`, `ingest:images`, `ingest:power`, `read:weather`, `read:api`

## Configuration

| Key | Description |
|-----|-------------|
| `ASPNETCORE_URLS` | Deployment-specific listener binding. Use `http://+:8080` for ACA and `https://+:443;http://+:8080` for local container HTTPS |
| `EnableHttpsRedirect` | Keep `false` for local sidecar traffic and for ACA when ingress owns HTTP to HTTPS behavior |
| `AzureAd:*` | Microsoft Entra ID OIDC settings |
| `ConnectionStrings:HualapaiValleyObservatory` | Azure SQL connection string |
| `ASPNETCORE_Kestrel__Certificates__Default__*` | TLS certificate path and password for local container HTTPS |

Configuration should be split by purpose:

- secrets belong in Key Vault
- deployment and hosting values belong in appsettings plus environment overrides
- runtime-editable non-secret values should move into the `v9.SiteConfiguration` table instead of accumulating in environment variables

The website now includes `ISiteConfigurationService`, which reads and caches `v9.SiteConfiguration` values for runtime use.

See [docs/WEBSITE_CONTAINER_APP.md](docs/WEBSITE_CONTAINER_APP.md) for the website deployment and configuration strategy.

See `Program.cs` for service registration and middleware pipeline.
