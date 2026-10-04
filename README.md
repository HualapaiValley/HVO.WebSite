# HVO.WebSite

Hualapai Valley Observatory's .NET 10 website ingests observatory telemetry,
stores canonical SQL Server history and presents Blazor dashboards. Four headless
collectors acquire Davis weather, JK BMS, EG4 inverter/MPPT and Victron SmartShunt
data. Home Assistant owns Kasa/Govee acquisition; its implemented HVO exporter
remains disabled with no production mappings or source claims.

Start with the [documentation index and all 32 project owners](docs/README.md),
[current architecture](docs/ARCHITECTURE.md), [contributor procedure](CONTRIBUTING.md)
and [test guide](tests/README.md). Agent work follows [AGENTS.md](AGENTS.md).

## First local success

Use the exact SDK in [global.json](global.json); roll-forward is disabled.
[Development prerequisites and integration lanes](docs/development/testing.md)
describe the required host tools. From the repository root:

```bash
dotnet restore HVO.WebSite.sln --locked-mode
dotnet build HVO.WebSite.sln --no-restore
pwsh tests/HVO.WebSite.PlaywrightTests/bin/Debug/net10.0/playwright.ps1 install --with-deps chromium
dotnet test HVO.WebSite.sln --no-build --no-restore --filter "TestCategory!=Integration&TestCategory!=Live"
```

These tests use owned fixtures. Browser tests start their own website and sandbox
hosts; they do not need production SQL, Home Assistant or physical hardware.
[Browser prerequisites and artifacts](tests/HVO.WebSite.PlaywrightTests/README.md)
explain Chromium/system dependencies. Integration lanes have separate disposable
or loopback prerequisites; Live execution requires an explicit assignment.

The ordinary website needs real SQL, Entra and Key Vault configuration, including
seed/migration startup behavior. Use test-owned hosts for a hardware-free first
success. See [website setup](src/HVO.WebSite.v9/README.md),
[configuration ownership](deploy/hvo-docker/README.md) and
[Key Vault materialization](docs/development/key-vault-materialization.md).
There is no root `.env.example` to copy.

## Current runtime ownership

| Unit | Useful entry point | Boundary |
|---|---|---|
| Website | [website README](src/HVO.WebSite.v9/README.md) | UI, admin/read APIs, canonical ingest and SQL persistence |
| SQL model | [DataModels](src/HVO.DataModels/README.md) | EF entities/context/migrations own current v9 schema |
| Davis | [Davis README](src/HVO.Hardware.DavisVantagePro2/README.md) | TCP weather raw/archive delivery and local station persistence |
| JK BMS | [JK README](src/HVO.Hardware.JkBms/README.md) | Persistent BLE sessions; bounded secret-gated password command |
| EG4 | [EG4 README](src/HVO.Hardware.Eg4/README.md) | Read-only USB HID/serial acquisition |
| SmartShunt | [SmartShunt README](src/HVO.Hardware.VictronSmartShunt/README.md) | Paired public-GATT acquisition and atomic observations |
| Shared edge | [hosting](src/HVO.Edge.Hosting/README.md), [outbox](src/HVO.Edge.Outbox/README.md), [contracts](src/HVO.Edge.Contracts/README.md), [HA MQTT](src/HVO.Edge.HomeAssistant.Mqtt/README.md) | Mounted config/secrets, health, durable retry and bounded HA presentation |
| HA exporter | [exporter](src/HVO.Edge.Exporter.HomeAssistant/README.md) | Allowlisted sources; production authority/enablement remain open |
| UI | [themes](src/HVO.WebSite.Themes/README.md), [sandbox](src/HVO.ThemeSandbox/README.md) | Shared styles and design reference app |
| SDK bridge | [Staging](src/HVO.Staging/README.md) | Production Davis moon projection plus tests; promotion awaits #372/SDK #82 |

The complete [project map](docs/README.md#project-documentation-owners) covers all
test assemblies, the fixture host and operational tools.

## Operations and contracts

- [Website on hvo-docker](deploy/hvo-docker/README.md), [Pi gateways](deploy/pi-gateways/README.md) and [Home Assistant](deploy/home-assistant/README.md)
- [SQLite backup and rollback](docs/gateways/sqlite-backup-and-rollback.md)
- [Gateway manuals](docs/gateways/README.md), [headless runtime](docs/architecture/EDGE_VNEXT_RUNTIME.md) and [edge data flows](docs/architecture/EDGE_DATA_FLOWS.md)
- [Weather queries](docs/development/canonical-weather-queries.md), [retry durability](docs/development/canonical-ingest-retries.md), [power observation identity](docs/development/power-observation-identity.md) and [UTC power history](docs/development/power-history-utc.md)
- [Decision history](docs/PROJECT_HISTORY.md), [changelog](CHANGELOG.md) and [open decisions](docs/FUTURE_WORK.md)

Raw ingest keys are scoped runtime secrets; browser sign-in uses Entra roles.
Endpoint/authentication/units/source identity details belong to these owners and
their linked controller source.

## Historical environment

The prior README recorded Linux ARM64 `devPi5`, BlueZ/D-Bus and a seven-device
JK workload running bare-host and Docker; observation dates were unrecorded.
Those are historical constraints, not a new hardware qualification. The complete
[prior README](docs/archive/undated-root-readme.md) preserves the evidence and
old endpoint/deployment descriptions. Former ACA hosting is
[superseded history](docs/archive/website-container-app.md).

Dependencies use central package pins and locked restore. Other repositories,
including [HVO.SDK](https://github.com/RoySalisbury/HVO.SDK), retain separate ownership.
