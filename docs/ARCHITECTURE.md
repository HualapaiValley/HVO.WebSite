# HVO Architecture Baseline

Last updated: 2026-06-14

This document captures the current architecture baseline for HVO.WebSite and the expected direction for near-term hardware integrations. It is a current-state reference, not a full implementation plan. Use `docs/PROJECT_HISTORY.md` for recent session context and decision notes. The validated RabbitMQ/Service Bus ingest POC was removed from the active repo after being deferred and remains available in git history if needed.

## System Purpose

HVO.WebSite is the central observatory monitor for Hualapai Valley Observatory. The system collects hardware telemetry from local edge services, stores normalized observatory data in Azure SQL, and presents web dashboards through the Blazor website.

The central website should stay focused on ingest, persistence, dashboards, administration, and read APIs. Hardware-specific device access should stay in edge collectors that run near the equipment.

## Current Deployment Units

| Unit | Project | Runtime Location | Primary Responsibility |
|------|---------|------------------|------------------------|
| Website | `src/HVO.WebSite.v9` | Azure Container Apps or local Docker | Blazor UI, read/admin APIs, auth, dashboards |
| Davis collector | `src/HVO.Hardware.DavisVantagePro2` | Local edge Docker host | Davis console TCP protocol, weather polling, local SQLite outbox, local status UI, website API forwarding |
| JK BMS collector | `src/HVO.Hardware.JkBms` | Local edge Docker host with BLE access | JK BMS BLE polling, battery readings, alarms, local SQLite outbox, local status UI, website API forwarding |
| Data models | `src/HVO.DataModels` | Shared library | EF Core DbContexts, entities, migrations |
| Theme assets | `src/HVO.WebSite.Themes` | Shared Razor class library | Shared visual theme assets |

Local application orchestration is defined in `docker-compose.yml`. Each edge service owns its own local data directory, SQLite outbox, logs, and hardware configuration.

## High-Level Data Flow

```text
Local hardware, ESPHome, SolarAssistant, and collectors/gateways
  -> source/provider-specific edge service
  -> local SQLite outbox
  -> typed HVO.WebSite ingest API
  -> Azure SQL v9 schema
  -> HVO.WebSite dashboards and read/admin APIs
```

The local reliability boundary is a per-edge-service SQLite outbox. The website API is the central validation and persistence boundary for Azure SQL. Edge services initiate outbound HTTP calls and retry from the outbox when connectivity or the website is unavailable.

The RabbitMQ -> Azure Service Bus -> Azure Functions path was validated as a POC and archived. It remains a future option if observatory scale or integration needs justify the added operational complexity.

OpenTelemetry is used for operational telemetry such as logs, traces, and metrics. It is not the primary domain-data ingest path.

## Central Website

`HVO.WebSite.v9` owns the central website surface:

| Area | Current State |
|------|---------------|
| UI | Blazor Server dashboard and admin pages |
| Browser auth | Microsoft Entra ID OIDC and app roles |
| Hardware auth | Scoped API keys for REST ingest paths |
| API versioning | URL segment routes under `/api/v{version}/...` |
| Persistence | Website APIs and services own v9 reads and writes through EF Core |
| Legacy data | `HvoDbContext` can read legacy `dbo` schema where needed |
| Health | Liveness, readiness, and full health endpoints |
| API docs | OpenAPI JSON and Scalar UI in development |

The website should not directly connect to LAN hardware, BLE devices, serial devices, or local MQTT brokers. Those dependencies belong in edge collectors and local gateways. The website is the central API, persistence, read/admin, and future command/control surface.

## Authentication And Authorization

Browser users authenticate with Microsoft Entra ID. The current app roles are `Admin` and `User`.

The current REST ingest path authenticates hardware services with scoped API keys:

| Scope | Current Meaning |
|-------|-----------------|
| `ingest:weather` | Weather ingest from the Davis collector |
| `ingest:bms` | BMS ingest from the JK BMS collector |
| `ingest:images` | Reserved for image ingest |
| `ingest:power` | Reserved for power ingest |
| `read:weather` | Weather read API access |
| `read:api` | Reserved API read access |

Raw API keys are runtime secrets. The website stores only SHA-256 hashes in the `v9.ApiKey` tables. Local edge collectors receive raw keys through environment variables or another local secret mechanism.

Future SQL authentication should move away from shared SQL password auth toward least-privilege Entra identities. Runtime website access and EF migrations should eventually use separate identities.

## Data Ownership

New development targets the EF-owned `v9` schema through `HvoV9DbContext`. The legacy `dbo` schema is retained as a reference and compatibility source, but new features should not extend the legacy schema.

Current v9 domains:

| Domain | Current Status |
|--------|----------------|
| Weather | Implemented raw ingest and recent read endpoints |
| BMS | Implemented batch ingest, device upsert, readings, cells, config/info snapshots, alarms |
| Power | Scope exists, but v9 models and controller are not implemented yet |
| Images | Scope and some image metadata model support exist, but ingest is not the current focus |
| Commands | Not implemented |

Minute and hourly aggregate entities exist for weather and BMS, but the website does not yet have a complete rollup worker populating all aggregate tables.

## Existing REST Ingest APIs

| Route | Auth | Purpose |
|-------|------|---------|
| `POST /api/v1/weather/raw` | `ingest:weather` | Single weather reading ingest |
| `POST /api/v1/weather/raw/batch` | `ingest:weather` | Batch weather ingest |
| `GET /api/v1/weather/raw/recent` | `read:weather` | Recent raw weather readings |
| `GET /api/v1/weather/hourly/recent` | `read:weather` | Recent hourly weather aggregates |
| `POST /api/v1/bms/readings` | `ingest:bms` | Batch BMS readings with related cell/config/info/alarm data |

These APIs are the current production ingest boundary. Future domain persistence should stay normalized rather than vendor-oriented, with vendor/provider adapters living in edge services.

## Deferred Brokered Ingest Option

The brokered ingest POC proved this path end-to-end for `hvo.weather.raw.v1`:

```text
Davis collector -> RabbitMQ -> Azure Service Bus -> Azure Functions -> Azure SQL
```

The design is deferred because the current system can meet reliability and operational needs with a simpler per-edge SQLite outbox and typed website APIs. The old POC artifacts were removed from the active repo and remain only in git history.

If brokered ingest is revived later, the same general responsibilities apply:

| Layer | Responsibility |
|-------|----------------|
| Local publishers | Davis, JK BMS, ESPHome, SolarAssistant, TPLink, and future collectors publish MQTT or AMQP |
| RabbitMQ on `hvo-docker` | Local broker, durable queues, local dead-lettering, shovel runtime |
| Local normalizers | Convert fragmented/vendor MQTT topics into canonical HVO packets when needed |
| Azure Service Bus `hvoobs` | Cloud broker, retry, subscriptions, dead-lettering |
| Azure Functions | Validate, deserialize, write to Azure SQL, publish SignalR updates |
| HVO.WebSite.v9 | Read dashboards, admin UI, command/control APIs |

Canonical schemas start with:

| Schema | Purpose |
|--------|---------|
| `hvo.weather.raw.v1` | Davis raw weather observation |
| `hvo.weather.sensor.v1` | Weather auxiliary sensor event |
| `hvo.weather.alarm.v1` | Weather-station alarm/event |
| `hvo.environment.reading.v1` | Temperature/humidity/battery environment reading |
| `hvo.power.reading.v1` | Inverter/load/charge/battery power reading |
| `hvo.bms.reading.v1` | JK BMS pack/cell reading |

## Edge Collector Pattern

The Davis and JK BMS collectors are independent ASP.NET Core applications. They share the same architectural shape:

| Layer | Responsibility |
|-------|----------------|
| Device protocol | Talk to the local hardware using TCP, BLE, serial, MQTT, or vendor API |
| Poller/worker | Schedule reads, manage reconnect/backoff, map device payloads to HVO records |
| Local SQLite outbox | Store telemetry durably before API transfer |
| API forwarder | POST batches to typed website ingest endpoints with retry/backoff |
| Local UI | Show device, outbox, and forwarding state near the hardware |
| Health endpoint | Support container/runtime health checks |
| Telemetry | Emit operational logs, traces, and metrics |

New collectors and gateways should use the local SQLite outbox + website API pattern unless there is a concrete need for a brokered path.

## Davis Collector

`HVO.Hardware.DavisVantagePro2` connects to the Davis Vantage Pro 2 console through the WeatherLink IP TCP bridge. It implements Davis protocol commands, LOOP packet parsing, archive catch-up support, local SQLite outbox storage, and a rich Blazor local admin UI.

Current responsibilities:

| Responsibility | Current State |
|----------------|---------------|
| TCP protocol | Implemented in `DavisConsoleClient` and related protocol classes |
| Weather polling | Implemented in `WeatherStationWorker` |
| Archive handling | Implemented with configurable catch-up mode |
| Local outbox | SQLite durable queue |
| Forwarding | Posts weather batches to the website API |
| Local UI | Mature hardware admin shell and weather/status pages |

The Davis UI is currently the best baseline for future hardware admin shells.

## JK BMS Collector

`HVO.Hardware.JkBms` connects to JK BMS units over Bluetooth LE through the host BlueZ stack. It uses a protocol/transport boundary, polls multiple devices, stores readings in a local outbox, and forwards to the website BMS ingest API.

Current responsibilities:

| Responsibility | Current State |
|----------------|---------------|
| BLE transport | Implemented through a transport abstraction and BlueZ-backed transport |
| Protocol parsing | Implemented for cell info, settings/config, and device info |
| Multi-device polling | Implemented with per-device state and backoff |
| Local outbox | SQLite durable queue |
| Forwarding | HTTP forwarder to website BMS ingest API |
| Local UI | Basic device/status pages |

The JK BMS forwarder shape is the better baseline for future shared collector infrastructure.

## Deployment Baseline

The website can run locally or in Azure Container Apps. In Azure, the website should listen on HTTP and let ACA ingress terminate TLS. Locally, Docker Compose can enable HTTPS with a local certificate while keeping HTTP available for service-to-service calls.

Collectors are edge services. They should run on hardware that can reach the physical devices or local protocols they depend on:

| Collector Type | Expected Runtime Location |
|----------------|---------------------------|
| Davis TCP | Same LAN as the WeatherLink IP adapter |
| JK BMS BLE | Host with Bluetooth adapter and BlueZ access |
| MQTT gateway/normalizer | Same LAN as the MQTT broker or device publisher |
| ESPHome gateway | Same LAN as ESPHome nodes or MQTT broker |
| SolarAssistant gateway | Same LAN as SolarAssistant MQTT/REST endpoint |
| TPLink dedicated app | Deferred until UI/control/monitoring boundary is decided |

The website should not require inbound access to edge collectors for telemetry ingest. Edge collectors initiate outbound API calls to the website.

## Secrets And Configuration

Configuration is split by purpose:

| Kind | Preferred Storage |
|------|-------------------|
| Cloud secrets | Azure Key Vault |
| Local edge secrets | Local `.env`, Docker secrets, or host secret store on `hvo-docker` |
| Runtime non-secret settings | `v9.SiteConfiguration` when central editing is needed |
| Build/runtime image settings | `appsettings.json` plus environment overrides |

Raw API keys, Azure client secrets, SQL connection strings, MQTT credentials, SolarAssistant credentials, and device control credentials are secrets. RabbitMQ and Service Bus credentials are also secrets if the archived brokered pattern is revived.

## Observability And Health

Each deployable service should expose a health endpoint and emit structured logs. The website has liveness and readiness endpoints. The collectors currently expose health, but their readiness is still mostly process-oriented rather than domain-oriented.

Recommended health baseline for collectors:

| Signal | Purpose |
|--------|---------|
| Process liveness | Container/runtime restart decisions |
| Device freshness | Detect stale hardware polling |
| SQLite outbox depth | Detect local backlog or website/API forwarding problems |
| Last API forward result | Detect authentication, validation, or connectivity failures |
| Last device error | Surface protocol or connectivity failures |

## Testing Baseline

Routine tests should use mocks, fakes, simulators, or local test servers. Live hardware tests should be explicitly categorized as `TestCategory=Live` and excluded from normal CI/test loops unless specifically requested.

The repo has `test.runsettings` with a test session timeout. Test runs should also use hang protection such as `--blame-hang-timeout` when appropriate.

## Future Hardware Integration Baseline

Future integrations should fit into the existing edge collector model.

| Integration | Recommended Boundary |
|-------------|----------------------|
| SolarAssistant / EG4 6500EX | First-pass read-only source/provider gateway that inventories via REST, prefers validated MQTT live state topics, keeps WebSocket as a fallback/diagnostic stream, writes a local SQLite outbox, then posts to a typed power ingest API |
| Victron SmartShunt | Edge collector; public paired GATT for production baseline, optional private enrichment later |
| TPLink outlets/lights | Deferred; likely closer to a dedicated Davis-style app with its own UI than a pure monitoring gateway |
| Govee BLE sensors | ESPHome or BLE edge collector that decodes sensor values locally |
| ESPHome nodes | Treat as edge decoders that expose values through MQTT or ESPHome native API to a provider-specific gateway, not as a generic transparent BLE adapter |

Power domains should remain distinct where the underlying meaning is distinct. SolarAssistant inverter/load/charge data is not the same as TPLink per-device outlet usage, even when both produce watts or watt-hours.

## ESPHome And BLE Baseline

ESPHome is useful as a distributed edge BLE decoder. It should not be assumed to be a transparent BLE relay for arbitrary .NET clients.

Preferred ESPHome patterns:

| Pattern | Fit |
|---------|-----|
| BLE advertisement decoding | Good for simple sensors such as many temperature/humidity beacons |
| ESPHome `ble_client` | Good for fixed known devices with known GATT services and characteristics |
| MQTT publishing | Good boundary for normalized edge telemetry into a .NET gateway |
| Home Assistant Bluetooth Proxy | Good if Home Assistant owns BLE integrations, but it adds Home Assistant as a dependency |
| Transparent BLE adapter | Not the recommended path |

For HVO, the practical path is ESPHome nodes near BLE devices, decoded values published through MQTT, and a local gateway that maps those values into typed API payloads with its own outbox.

## Command And Control Baseline

The current architecture is ingest-first. Command/control is not implemented yet.

If command/control is added, the preferred model is a cloud-side command inbox plus edge-side polling and acknowledgements:

```text
Website/admin action
  -> command inbox row
  -> edge collector polls for pending commands
  -> edge collector executes against local hardware
  -> edge collector posts acknowledgement/result
  -> website records audit/status
```

This avoids requiring inbound network access from the cloud website to local observatory hardware.

Expected command/control additions:

| Area | Expected Addition |
|------|-------------------|
| Scopes | New control scopes such as `control:command`, or domain-specific control scopes |
| Data model | `CommandInbox`, command status, command result/audit rows |
| Website API | Enqueue, claim/poll, acknowledge, complete/fail endpoints |
| Edge collectors | Background command worker separate from telemetry forwarding |
| Security | Strong authorization, audit trail, command expiry, idempotency |

## Shared Collector Infrastructure Opportunities

The Davis and JK BMS collectors intentionally started independently. With more collectors planned, shared infrastructure is becoming worthwhile.

Good candidates for future shared edge projects:

| Candidate | Reason |
|-----------|--------|
| `HVO.Edge.Outbox` | Same durable local delivery, retry, compaction, idempotency, and status pattern across collectors/gateways |
| `HVO.Edge.ApiForwarding` | Shared typed HTTP forwarding if the HTTP client/response mapping grows beyond the outbox package |
| Ingest DTO/client package | Reduce payload drift between collectors and website |
| Gateway host | Shared worker shell for SolarAssistant, ESPHome, and vendor topic transforms |
| Collector options | Common endpoint, API key, batch size, retry, DB path, retention settings |
| Health checks | Common stale-device and outbox-backlog checks |
| Status state models | Common UI footer/status patterns |

Do not extract abstractions before the shape is stable. The first extraction should be based on the observed Davis and JK BMS differences, plus SolarAssistant discovery as the third reference point.

## Known Gaps

| Gap | Impact | Status |
|-----|--------|--------|
| Brokered ingest is archived, not active | The POC is available for future reference, but active collectors should not depend on it | Will remain archived unless scale justifies revival |
| v9 power persistence gaps | Aggregated power readings, energy counters, inverter detail, device inventory, and gateway status snapshots are implemented; remaining detail/inventory streams need normalized targets | Partially resolved (power.reading, power.energy, power.inverter-detail, gateway.status are live) |
| Weather and BMS aggregates are incomplete | Some minute/hourly tables exist but are not fully populated by website workers | Ongoing |
| Collector health is not domain-rich enough | Process health can pass while device polling or forwarding is unhealthy | Ongoing |
| Shared outbox/API-forwarding infrastructure is not extracted | New gateways repeat collector delivery code until a common package is added | Planned for SolarAssistant migration |
| SolarAssistant source shape discovery | REST/MQTT/WebSocket topics, cadence, timestamps documented in SOLARASSISTANT_DISCOVERY.md | Resolved |
| Victron SmartShunt integration | Public paired GATT telemetry working; private enrichment read-only; writes deferred | Resolved |
| Command/control is not designed in code yet | Future control operations need security, audit, queueing, and edge execution semantics | Not started |
| ESPHome integration is not implemented | BLE gateway strategy needs a prototype against real devices and topics | Deferred |

## Near-Term Recommended Sequence

1. Keep PR-sized changes small and preserve the current outbox-first reliability model.
2. Run a non-deployable SolarAssistant REST, MQTT, and WebSocket discovery POC and document sanitized samples.
3. Extract only the shared SQLite outbox/API-forwarding pieces justified by Davis, JK BMS, and SolarAssistant discovery.
4. Implement normalized v9 power persistence before SolarAssistant production ingest.
5. Add a SolarAssistant gateway with its own local data directory, outbox, config, and status UI if useful.
6. Defer ESPHome until hardware/topics are available and defer TPLink until its dedicated app boundary is decided.
7. Harden SQL auth with Entra/managed identities and separate migration/runtime permissions.
8. Revisit brokered ingest only if operational scale justifies it.

## Architectural Decisions Captured Here

| Decision | Baseline |
|----------|----------|
| Central website role | Ingest, persistence, dashboards, auth, and admin |
| Hardware access | Edge collectors near devices |
| Delivery reliability | Per-edge SQLite outboxes with website API retry/backoff |
| Domain ingest | Prefer normalized domain APIs over vendor-specific central models |
| Power model | Keep inverter/load/charge and per-device outlet power distinct where needed |
| SolarAssistant scope | First-pass gateway is read-only; MQTT write/control topics may be added later through a separate safety and audit design |
| BLE gateways | Use ESPHome as edge decoder, not transparent BLE relay |
| Command/control | Use cloud inbox plus edge polling when implemented |
| Tests | Mock/simulated by default, live hardware only by explicit category |
