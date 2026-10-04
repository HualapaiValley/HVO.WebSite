# HVO.WebSite

[![CI](https://github.com/RoySalisbury/HVO.WebSite/actions/workflows/ci.yml/badge.svg)](https://github.com/RoySalisbury/HVO.WebSite/actions/workflows/ci.yml)
![.NET](https://img.shields.io/badge/.NET-10.0-blue)
![License](https://img.shields.io/badge/license-proprietary-lightgrey)

Observatory dashboard and monitoring system built with ASP.NET Core and Blazor Server (SSR). Collects and displays weather, battery and power observations, persists canonical history to SQL Server, and provides role-based web access via Microsoft Entra ID. The current website/API deployment is self-hosted on `hvo-docker`.

---

## Projects

| Project | Description |
|---------|-------------|
| **HVO.WebSite.v9** | Main observatory dashboard — Blazor SSR pages, REST API endpoints, Azure SQL persistence |
| **HVO.Hardware.DavisVantagePro2** | Davis Vantage Pro 2 weather station collector — polls console, stores local station state separately from shared SQLite outbox, forwards to website API |
| **HVO.Hardware.JkBms** | JK BMS battery monitor collector — polls devices over Bluetooth LE, stores to shared SQLite outbox, forwards to website API |
| **HVO.Hardware.Eg4** | EG4 6500EX and MPPT100 collector — read-only direct device telemetry, Home Assistant MQTT projection, and shared SQLite outbox |
| **HVO.Hardware.VictronSmartShunt** | Victron SmartShunt collector — paired BLE battery monitor telemetry, Home Assistant MQTT projection, and shared SQLite outbox |
| **HVO.Edge.Exporter.HomeAssistant** | Implemented exporter for approved HA-owned sources; intentionally disabled in production with no mappings or source claims |
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

The active direct headless vNext collectors are Davis, JK BMS, EG4, and SmartShunt. Home Assistant owns Kasa and Govee acquisition and presentation. The HA exporter exists for a future approved canonical-history path, but it is intentionally disabled and has no production mappings or source claims.

The retired direct SolarAssistant and TP-Link/Kasa containers, images, and Docker volumes have been removed. Their applications are not deployable repository targets. A checksum-verified SolarAssistant volume archive is retained outside Docker storage for historical recovery only.

## Edge Deployment Direction

- The internally hosted website/API on `hvo-docker` is the canonical ingest
  boundary; the public Azure address is a proxy path rather than a Pi ingest dependency.
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
| **Weather collection** | Davis Vantage Pro 2 console polled at ~2 sec (LOOP2), archived on schedule, and forwarded through the durable outbox |
| **Battery monitoring** | JK BMS devices polled over Bluetooth LE with alarm change detection and device-info snapshots |
| **Durable outbox** | Each gateway writes to a local SQLite outbox before forwarding; retries with exponential backoff survive restarts/API downtime, permanent failures are dead-lettered, and retry-exhausted rows can requeue after recovery |
| **REST API** | Versioned API (`/api/v1/…`) protected by API key + scope claims; supports single and batch ingest |
| **Observatory dashboard** | Blazor SSR web UI with HVO dark theme; role-gated admin area |
| **Authentication** | Microsoft Entra ID OIDC for browser users; API key + scope for hardware services |
| **Health probes** | `/health/live` (liveness), `/health/ready` (DB readiness), `/health` (full diagnostics) |
| **OpenAPI** | `/openapi/v1.json` spec; interactive Scalar UI at `/scalar/v1` (dev) |

## UI Design System

`HVO.WebSite.Themes` provides the shared UI baseline for the website and ThemeSandbox. The active direct collectors are headless; Kasa and Govee presentation is owned by Home Assistant.

- Fixed top and bottom app bars with the page content scrolling inside the center canvas
- Light and dark theme support driven by shared shell tokens instead of page-local hardcoded colors
- MudBlazor shell chrome, with page-specific content kept in Blazor components and scoped CSS
- Shared chart and astronomy presentation components for current website surfaces

See [src/HVO.WebSite.Themes/README.md](src/HVO.WebSite.Themes/README.md) for the current shared layout, component, and asset contract.

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

Start from the repository root with the exact SDK in `global.json` (10.0.400),
PowerShell and the tools listed in [testing](docs/development/testing.md).
This path builds and exercises test-owned website/ThemeSandbox fixtures without
production secrets, a running website, SQL Server or physical gateways:

```bash
dotnet --version
dotnet restore HVO.WebSite.sln --locked-mode --nologo
dotnet build HVO.WebSite.sln --no-restore --nologo
pwsh tests/HVO.WebSite.PlaywrightTests/bin/Debug/net10.0/playwright.ps1 install --with-deps chromium
dotnet test HVO.WebSite.sln --no-build --no-restore --filter "TestCategory!=Integration&TestCategory!=Live" --logger trx --results-directory TestResults/fast
```

Browser tests start real application hosts on test-owned loopback ports and
retain screenshots, traces and console evidence. Follow [owned browser fixtures](docs/development/testing.md#owned-website-and-themesandbox-browser-fixtures)
for the focused website first-success command. Provisioned SQL/HA integration
and simulator lanes have separate documented prerequisites.

A bare website `dotnet run` is not a secrets-free demo: its startup configuration
needs explicit SQL/Entra settings and Key Vault access when enabled. Startup
seeding applies EF migrations to its configured SQL target, so use only an
approved development database. There is no root `.env.example`; `.env` alone
does not configure mounted gateway files or physical interfaces. See
[current website deployment](deploy/hvo-docker/README.md) for an authorized runtime
and [Pi commissioning](deploy/pi-gateways/README.md) for hardware prerequisites.

## Container Publishing

Images publish independently to the self-hosted registry. The publisher defaults
to `deploy/hvo-docker/.env` or explicit `HVO_PUBLISH_ENV_FILE`; root `.env` is a
separate bootstrap/gist cache. Build/push/materialization require their own
operational authorization and are unnecessary for the hardware-free quick start.

EG4 is built natively through the remote Pi Docker context. The retired `hvo-solarassistant` and `hvo-tplinkkasa` images are not active publishing targets.

See [docs/CONTAINER_PUBLISHING.md](docs/CONTAINER_PUBLISHING.md) for the
self-hosted registry inventory, the Key Vault synchronization and versioning
workflow, and the commands used to inspect published tags.

---

## Dev Container

This repository includes a [dev container](.devcontainer/) configuration for a consistent development environment. Open in VS Code or GitHub Codespaces to get started automatically.

Recommended workflow:

- Use the repo devcontainer on `hvo-dev` as the primary development environment.
- Use owned non-live fixtures first; physical Pi validation/deployment needs
  separate applicable authorization and the commissioning runbook.
- Keep direct Pi development available for host-level diagnostics, but treat Pi systems primarily as edge deployment targets.

Docker contexts:

```bash
docker context ls
```

Inspect the selected context before Docker work. Use the named deployment
scripts/runbooks when a rollout is authorized; a generic root Compose command
does not establish gateway config/secret/device prerequisites.

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
| [Website Deployment](deploy/hvo-docker/README.md) | Current self-hosted configuration, identity, key-ring and rollout prerequisites |
| [Former ACA Record](docs/archive/website-container-app.md) | Historical Azure deployment and cryptographic migration evidence |

---

## License

See [LICENSE](LICENSE). Internal use only. Not for external distribution.
