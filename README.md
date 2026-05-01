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
docker compose up --build

# Or run the website only
cd src/HVO.WebSite.v9
dotnet run
```

---

## Dev Container

This repository includes a [dev container](.devcontainer/) configuration for a consistent development environment. Open in VS Code or GitHub Codespaces to get started automatically.

---

## Documentation

| Guide | Description |
|-------|-------------|
| [Contributing](CONTRIBUTING.md) | PR workflow, branch naming, coding standards |
| [Changelog](CHANGELOG.md) | Release history and notable changes |
| [Plan](docs/PLAN.md) | Implementation plan and milestone tracking |

---

## License

See [LICENSE](LICENSE). Internal use only. Not for external distribution.
