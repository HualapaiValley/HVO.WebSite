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
| **HVO.Hardware.DavisVantagePro2** | Davis Vantage Pro 2 weather station collector — polls console, stores local station state separately from shared SQLite outbox, forwards to website API |
| **HVO.Hardware.JkBms** | JK BMS battery monitor collector — polls devices over Bluetooth LE, stores to shared SQLite outbox, forwards to website API |
| **HVO.Gateway.SolarAssistant** | SolarAssistant gateway — REST/MQTT power telemetry, inventory/configuration/detail/status snapshots, shared SQLite outbox |
| **HVO.Hardware.VictronSmartShunt** | Victron SmartShunt gateway — BLE battery monitor telemetry with shared SQLite outbox |
| **HVO.Gateway.TplinkKasa** | TP-Link/Kasa gateway — local device status UI plus energy/inventory forwarding through shared SQLite outbox |
| **HVO.Edge.Outbox** | Shared edge durable outbox model, store, retry/dead-letter/requeue logic, compaction, and health evaluator |
| **HVO.Edge.Contracts** | Shared gateway status, health, and payload contracts |
| **HVO.DataModels** | Entity Framework Core models and DbContexts for observatory data |
| **HVO.WebSite.Themes** | Shared CSS themes, fonts, and static assets (Razor Class Library) |

## Architecture

```
Davis Vantage Pro 2 console (TCP)
        │
 HVO.Hardware.DavisVantagePro2
    ├─ Headless health/diagnostics + MQTT current state
   ├─ SQLite outbox (durable, idempotent, with retry)
   └─ POST /api/v1/weather/raw  ──────────────────────────────┐
                                                               │
JK BMS devices (Bluetooth LE)                                  ▼
        │                                              HVO.WebSite.v9
 HVO.Hardware.JkBms                                  ├─ Headless health/diagnostics API
    ├─ MQTT Discovery/current state                    ├─ REST API (API-key auth)
    ├─ SQLite outbox (durable, idempotent, with retry)  ├─ Azure SQL (EF Core)
    └─ POST /api/v1/bms/readings  ──────────────────────┤ Role-based auth (Entra ID)
                                                         └─ Health probes + OpenAPI
```

Additional Pi gateways follow the same edge pattern: SolarAssistant, SmartShunt, and TPLink Kasa poll local sources, enqueue to their local shared outbox, and forward typed payloads to the website API.

## Edge Deployment Direction

- Azure remains the central website/API/persistence boundary.
- Pi-class ARM64 edge hosts are the primary deployment targets for hardware gateway services.
- `devPi5` is the current validated gateway target for BLE workloads.
- `hvo-docker` remains the preferred home for shared observability/infrastructure services such as Grafana and telemetry collectors, not direct BLE gateway polling.

Current validated BLE edge baseline:

- Host: `devPi5`
- Architecture: `linux-arm64`
- Runtime: `.NET 10`
- Container runtime: Docker
- Bluetooth path: BlueZ over mounted system D-Bus socket
- Proven workload: 7 concurrent JK BMS BLE connections, polled successfully both bare-host and inside Docker

## Features

| Feature | Description |
|---------|-------------|
| **Weather collection** | Davis Vantage Pro 2 console polled at ~2 sec (LOOP2) and archived on schedule; UI for calibration, settings, clock, transmitters |
| **Battery monitoring** | JK BMS devices polled over Bluetooth LE with alarm change detection and device-info snapshots |
| **Durable outbox** | Each gateway writes to a local SQLite outbox before forwarding; retries with exponential backoff survive restarts/API downtime, permanent failures are dead-lettered, and retry-exhausted rows can requeue after recovery |
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
| `GET`  | `/api/v1/weather/raw/recent` | API key `read:weather` or `read:api` | Recent raw readings (paginated) |
| `GET`  | `/api/v1/weather/hourly/recent` | API key `read:weather` or `read:api` | Recent hourly aggregates (paginated) |
| `GET`  | `/api/v1/weather/latest` | API key `read:weather` or `read:api` | Latest weather record |
| `GET`  | `/api/v1/weather/current` | API key `read:weather` or `read:api` | Current conditions with today's extremes |
| `GET`  | `/api/v1/weather/highs-lows` | API key `read:weather` or `read:api` | Highs/lows for a date range |

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

The deployable images are published independently to the self-hosted registry on `hvo-docker`, and each image keeps its own version in `.env`.

Use the repo script to build, tag, push, and verify one image at a time:

```bash
./scripts/sync-secrets-from-keyvault.sh --apply
./scripts/sync-env-gist.sh
./scripts/publish-image.sh website
./scripts/publish-image.sh davis
./scripts/publish-image.sh jkbms
./scripts/publish-image.sh solarassistant
./scripts/publish-image.sh smartshunt
./scripts/publish-image.sh tplinkkasa
```

See [docs/CONTAINER_PUBLISHING.md](docs/CONTAINER_PUBLISHING.md) for the
self-hosted registry inventory, the Key Vault synchronization and versioning
workflow, and the commands used to inspect published tags.

---

## Dev Container

This repository includes a [dev container](.devcontainer/) configuration for a consistent development environment. Open in VS Code or GitHub Codespaces to get started automatically.

Recommended workflow:

- Use the repo devcontainer on `hvo-dev` as the primary development environment.
- Deploy hardware gateway containers to Pi targets for BLE/runtime validation.
- Keep direct Pi development available for host-level diagnostics, but treat Pi systems primarily as edge deployment targets.

Docker contexts:

```bash
docker context ls
docker --context devpi5 ps
docker --context devpi5 compose up -d --build
```

---

## Documentation

| Guide | Description |
|-------|-------------|
| [Docs Index](docs/README.md) | Entry point for current docs, discovery notes, and future-work roadmap |
| [Contributing](CONTRIBUTING.md) | PR workflow, branch naming, coding standards |
| [Changelog](CHANGELOG.md) | Release history and notable changes |
| [Project History](docs/PROJECT_HISTORY.md) | Session-by-session working history, key decisions, and next-context notes |
| [Architecture](docs/ARCHITECTURE.md) | Current system baseline, data flow, collector pattern, and future integration direction |
| [Container Publishing](docs/CONTAINER_PUBLISHING.md) | Self-hosted registry inventory, versioning workflow, publish script usage |
| [Website Data Protection](docs/WEBSITE_DATA_PROTECTION.md) | Durable encrypted key-ring deployment, backup, restore, and rollback |
| [Website Container App](docs/WEBSITE_CONTAINER_APP.md) | Azure Container App deployment decisions and runtime requirements for `HVO.WebSite` |

---

## License

See [LICENSE](LICENSE). Internal use only. Not for external distribution.
