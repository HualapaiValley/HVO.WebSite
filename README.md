# HVO.WebSite

[![CI](https://github.com/RoySalisbury/HVO.WebSite/actions/workflows/ci.yml/badge.svg)](https://github.com/RoySalisbury/HVO.WebSite/actions/workflows/ci.yml)
![.NET](https://img.shields.io/badge/.NET-10.0-blue)
![License](https://img.shields.io/badge/license-proprietary-lightgrey)

Observatory dashboard and monitoring system built with ASP.NET Core and Blazor Server (SSR). Collects and displays real-time weather data and battery monitor readings from hardware at Hualapai Valley Observatory, persists data to Azure SQL, and provides role-based web access via Microsoft Entra ID.

---

## Projects

| Project | Description |
|---------|-------------|
| **HVO.WebSite.v9** | Main observatory dashboard — Blazor SSR pages, REST API endpoints, Azure SQL persistence |
| **HVO.Hardware.DavisVantagePro2** | Davis Vantage Pro 2 weather station collector — polls console, stores to local SQLite outbox, forwards to website API |
| **HVO.Hardware.JkBms** | JK BMS battery monitor collector — polls devices over Bluetooth LE, stores to local SQLite outbox, forwards to website API |
| **HVO.DataModels** | Entity Framework Core models and DbContexts for observatory data |
| **HVO.WebSite.Themes** | Shared CSS themes, fonts, and static assets (Razor Class Library) |

## Architecture

```
Davis Vantage Pro 2 console (TCP)
        │
 HVO.Hardware.DavisVantagePro2
   ├─ Blazor SSR UI (status, archive, calibration, settings, …)
   ├─ SQLite outbox (durable, idempotent, with retry)
   └─ POST /api/v1/weather/raw  ──────────────────────────────┐
                                                               │
JK BMS devices (Bluetooth LE)                                  ▼
        │                                              HVO.WebSite.v9
 HVO.Hardware.JkBms                                  ├─ Blazor SSR dashboard
   ├─ Blazor SSR UI (status, devices, device detail)  ├─ REST API (API-key auth)
   ├─ SQLite outbox (durable, idempotent, with retry)  ├─ Azure SQL (EF Core)
   └─ POST /api/v1/bms/readings  ──────────────────────┤ Role-based auth (Entra ID)
                                                        └─ Health probes + OpenAPI
```

## Features

| Feature | Description |
|---------|-------------|
| **Weather collection** | Davis Vantage Pro 2 console polled at ~2 sec (LOOP2) and archived on schedule; UI for calibration, settings, clock, transmitters |
| **Battery monitoring** | JK BMS devices polled over Bluetooth LE with alarm change detection and device-info snapshots |
| **Durable outbox** | Each collector writes to a local SQLite outbox before forwarding; retries with exponential backoff survive API downtime |
| **REST API** | Versioned API (`/api/v1/…`) protected by API key + scope claims; supports single and batch ingest |
| **Observatory dashboard** | Blazor SSR web UI with HVO dark theme; role-gated admin area |
| **Authentication** | Microsoft Entra ID OIDC for browser users; API key + scope for hardware services |
| **Health probes** | `/health/live` (liveness), `/health/ready` (DB readiness), `/health` (full diagnostics) |
| **OpenAPI** | `/openapi/v1.json` spec; interactive Scalar UI at `/scalar/v1` (dev) |

## UI Baseline

The Davis collector UI now serves as the baseline shell/template for the hardware admin apps.

- Fixed top and bottom app bars with the page content scrolling inside the center canvas
- Light and dark theme support driven by shared shell tokens instead of page-local hardcoded colors
- MudBlazor shell chrome, with page-specific content kept in Blazor components and scoped CSS
- Inline SVG charts and astronomy graphics so the live status page has no separate charting dependency

See [src/HVO.Hardware.DavisVantagePro2/README.md](src/HVO.Hardware.DavisVantagePro2/README.md) for the Davis template structure, dependencies, and the Status page implementation notes.

## API Endpoints

### Weather
| Method | Route | Auth | Description |
|--------|-------|------|-------------|
| `POST` | `/api/v1/weather/raw` | API key `ingest:weather` | Ingest single raw weather reading |
| `POST` | `/api/v1/weather/raw/batch` | API key `ingest:weather` | Ingest batch of raw readings |
| `GET`  | `/api/v1/weather/raw/recent` | API key `read:weather` | Recent raw readings (paginated) |
| `GET`  | `/api/v1/weather/hourly/recent` | API key `read:weather` | Recent hourly aggregates (paginated) |
| `GET`  | `/api/v1/weather/latest` | open | Latest weather record |
| `GET`  | `/api/v1/weather/current` | open | Current conditions with today's extremes |
| `GET`  | `/api/v1/weather/highs-lows` | open | Highs/lows for a date range |

### BMS
| Method | Route | Auth | Description |
|--------|-------|------|-------------|
| `POST` | `/api/v1/bms/readings` | API key `ingest:bms` | Ingest batch BMS readings with alarm/config detection |

## Dependencies

- [HVO.Core](https://github.com/RoySalisbury/HVO.SDK) — core shared library (NuGet)
- [HVO.Core.SourceGenerators](https://github.com/RoySalisbury/HVO.SDK) — source generators (NuGet)

---

## Quick Start

```bash
# Run the full stack locally (requires .env with secrets)
# Local hardware services now send OTLP telemetry to the global collector.
# Set OTEL_COLLECTOR_ENDPOINT in .env, for example:
# OTEL_COLLECTOR_ENDPOINT=http://192.168.1.238:4318
docker compose up --build

# Or run the website only
cd src/HVO.WebSite.v9
dotnet run
```

## Container Publishing

The deployable images are published independently to Azure Container Registry, and each image keeps its own version in `.env`.

Use the repo script to build, tag, push, and verify one image at a time:

```bash
./scripts/sync-env-gist.sh
./scripts/publish-acr-image.sh website
./scripts/publish-acr-image.sh davis
./scripts/publish-acr-image.sh jkbms
./scripts/publish-acr-image.sh solarassistant
```

See [docs/CONTAINER_PUBLISHING.md](docs/CONTAINER_PUBLISHING.md) for the Azure subscription and ACR inventory, the version-variable workflow, the gist sync requirement, and the query commands used to inspect published tags.

---

## Dev Container

This repository includes a [dev container](.devcontainer/) configuration for a consistent development environment. Open in VS Code or GitHub Codespaces to get started automatically.

---

## Documentation

| Guide | Description |
|-------|-------------|
| [Contributing](CONTRIBUTING.md) | PR workflow, branch naming, coding standards |
| [Changelog](CHANGELOG.md) | Release history and notable changes |
| [Architecture](docs/ARCHITECTURE.md) | Current system baseline, data flow, collector pattern, and future integration direction |
| [Edge Outbox And Gateway Plan](docs/EDGE_OUTBOX_AND_GATEWAY_PLAN.md) | Shared edge outbox direction, gateway naming, and SolarAssistant discovery plan |
| [Container Publishing](docs/CONTAINER_PUBLISHING.md) | Azure ACR inventory, versioning workflow, publish script usage |
| [Website Container App](docs/WEBSITE_CONTAINER_APP.md) | Azure Container App deployment decisions and runtime requirements for `HVO.WebSite` |
| [Plan](docs/PLAN.md) | Implementation plan and milestone tracking |

---

## License

See [LICENSE](LICENSE). Internal use only. Not for external distribution.
