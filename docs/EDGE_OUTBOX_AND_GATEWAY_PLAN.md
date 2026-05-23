# Edge Outbox And Gateway Plan

Status: planning baseline.

This document captures the near-term direction after the RabbitMQ/Service Bus ingest POC. The active production path remains:

```text
edge service -> local SQLite outbox -> typed HVO.WebSite ingest API -> Azure SQL
```

The goal is to extract the common outbox/API-forwarding pattern from the current Davis and JK BMS services, validate the shape against SolarAssistant discovery, then bring Davis and JK BMS into the shared pattern with minimal behavior change.

## Scope Decisions

- Keep `HVO.Hardware.DavisVantagePro2` and `HVO.Hardware.JkBms` as independent deployable services.
- Hold TPLink work until its UI/control/monitoring boundary is discussed. Do not create TPLink project shells yet.
- Use SolarAssistant as the first new gateway candidate because it is already available and can be inspected now.
- Defer ESPHome implementation until hardware is deployed and real topics/API behavior are known.
- Keep the website as the central API, validation, persistence, migration, dashboard, and future command/control boundary.
- Keep each edge service isolated with its own image, configuration, data directory, SQLite outbox, logs, and status surface.

## Current Outbox References

### Davis Current Shape

The Davis collector has a simple single-destination outbox:

| Area | Current Davis behavior |
|------|------------------------|
| Local entity | `OutboxRecord` with `RecordedAtUtc`, `Payload`, status, attempts, retry timestamps, last error, created time, and `IsArchiveRecord` |
| Idempotency | Unique `RecordedAtUtc` only |
| Payload | Pre-serialized weather JSON written by `WeatherStationWorker` |
| Forwarding | Single `OutboxForwarder` posts a JSON array to `Outbox:ApiEndpoint + "/batch"` |
| API result handling | Parses batch response and can mark individual records failed from per-record validation failures |
| Retry | Exponential backoff with max attempts and max backoff seconds |
| Retention | No sent-record compaction yet |
| Status UI | Reads public forwarder properties for pending, failed, last sent, last error, and last batch count |
| Extra local tables | Station settings and station info snapshots are stored in the same SQLite database |

Design notes:

- The Davis per-record failure handling is important for website APIs that report permanent validation failures inside an otherwise successful batch.
- The unique key works because one Davis station currently writes one weather record per timestamp, but it is not general enough for multi-device gateways.
- Davis mixes domain snapshot tables with the outbox DbContext. A shared outbox should not force every service to share those domain tables.

### JK BMS Current Shape

The JK BMS collector has the richer current outbox/forwarding pattern:

| Area | Current JK BMS behavior |
|------|-------------------------|
| Local entity | `OutboxRecord` with `DeviceAddress`, `DeviceAlias`, `RecordedAtUtc`, `Payload`, status, attempts, retry timestamps, last error, and created time |
| Idempotency | Unique `(DeviceAddress, RecordedAtUtc)` |
| Payload | Pre-serialized `BmsIngressRecord` containing reading plus changed config/device-info snapshots |
| Forwarding | `ForwarderCoordinator` fans out each batch to registered `IReadingForwarder` implementations |
| API result handling | Current HTTP forwarder treats non-success status as batch failure and relies on API idempotency for retries |
| Retry | Exponential backoff with max attempts and max backoff seconds |
| Retention | Daily compaction deletes old sent records based on `SentRetentionDays` |
| Status UI | Uses thread-safe backing fields for pending, failed, last sent, last error, and last batch count |
| Development behavior | HTTP forwarder no-ops when endpoint/API key are placeholders |

Design notes:

- Device-scoped idempotency is the right baseline for services that handle more than one physical or logical source.
- The coordinator/fan-out pattern is useful, but the near-term shared production path only needs one typed website API forwarder.
- Sent-record compaction should be shared so local SQLite databases do not grow forever.
- Thread-safe status state should be part of the shared coordinator/status snapshot.

## Shared Outbox Design Implications

The first shared outbox should start small and preserve behavior. It should support the cases already proven by Davis and JK BMS, plus the expected SolarAssistant gateway shape.

Recommended shared concepts:

| Concept | Recommendation |
|---------|----------------|
| Project | Start with `HVO.Edge.Outbox` |
| Optional later split | Add `HVO.Edge.ApiForwarding` only if HTTP forwarding grows enough to justify separation |
| Record key | Use an explicit `IdempotencyKey` or `(SourceId, RecordedAtUtc, RecordKind)` rather than timestamp-only uniqueness |
| Source identity | Store stable `SourceId`/`DeviceId` for all records; Davis can use station ID, JK BMS can use configured device ID or address |
| Payload | Store opaque JSON payload; domain mapping stays in the owning edge service |
| Destination | Store a destination or record kind if one service can emit multiple payload types |
| Status | Share `Pending`, `Sent`, and `Failed` states plus attempt/retry/error timestamps |
| Retry | Share exponential backoff, max attempts, max backoff, sweep interval, and batch size |
| Retention | Share sent-record compaction with configurable retention days |
| Status snapshot | Share thread-safe pending/failed/last-sent/last-error/last-batch status state |
| API response mapping | Make per-record permanent failure handling pluggable so Davis-style batch responses are supported |
| Domain tables | Keep domain-specific local tables in the owning service, not in the shared outbox package |

Non-goals for the first extraction:

- Do not introduce RabbitMQ, Service Bus, or brokered delivery back into active code.
- Do not build a generic domain model inside the outbox library.
- Do not create TPLink project shells.
- Do not extract UI components until Davis and JK BMS converge on the same status model.

## Naming And Isolation

Recommended names:

| Boundary | Naming |
|----------|--------|
| Shared outbox library | `HVO.Edge.Outbox` |
| Shared API forwarding library if needed | `HVO.Edge.ApiForwarding` |
| Existing Davis service | `HVO.Hardware.DavisVantagePro2` |
| Existing JK BMS service | `HVO.Hardware.JkBms` |
| Future SolarAssistant service | `HVO.Gateway.SolarAssistant` |
| Future ESPHome service | `HVO.Gateway.ESPHome`, after hardware deployment |
| TPLink | Deferred; naming depends on UI/control decision |

Use `Hardware` for services that directly own a hardware protocol and local device UI. Use `Gateway` for services that adapt an existing source/provider feed, such as SolarAssistant MQTT/API, into HVO typed API payloads.

Each deployable edge service should own:

- Its own image and container.
- Its own data volume.
- Its own SQLite outbox database.
- Its own logs directory.
- Its own configuration namespace and environment prefix.
- Its own health/readiness endpoint.
- Its own status/admin UI when the service has enough operational state to justify one.

Recommended container/data naming:

| Service | Image/container | Data volume | In-container data path |
|---------|-----------------|-------------|------------------------|
| Davis | `hvo-davis` | `hvo-davis-data` | `/data` |
| JK BMS | `hvo-jkbms` | `hvo-jkbms-data` | `/data` |
| SolarAssistant | `hvo-solarassistant` | `solarassistant-outbox` | `/app/data` |

## SolarAssistant Discovery POC

SolarAssistant should be inspected before creating production gateway code. The discovery POC should answer how SolarAssistant publishes data, what data exists, and how the timestamps/source identities should map to HVO records.

References:

- MQTT API: `https://solar-assistant.io/help/integration/mqtt`
- REST API: `https://solar-assistant.io/help/integration/rest-api`
- WebSocket API: `https://solar-assistant.io/help/integration/websocket-api`
- CLI: `https://solar-assistant.io/help/integration/sacli`

Discovery helper:

- `tools/solarassistant-discovery/probe.py` is a non-deployable read-only probe for REST and MQTT metadata.
- The helper reads credentials from environment variables and prints sanitized summaries only.
- Current unauthenticated manual probe found HTTP and MQTT ports reachable, but REST and MQTT both require credentials.
- Temporary `sacli` validation confirmed the CLI also requires a local password argument or saved credential; clearing the local password does not make REST/WebSocket discovery passwordless.
- Local REST is validated with credentials and returned metrics under `total`, `inverter_1`, and `battery_1` topic prefixes.
- The actual local WebSocket endpoint accepted by this device is `/api/websocket?password=<password>&vsn=2.0.0`; the documented `/api/socket/websocket` path returned `404` during discovery.
- Local WebSocket is validated with credentials and streamed metric definitions/data for `total`, `inverter_1`, and `battery_1` topic prefixes.
- MQTT is validated with separate MQTT credentials. It exposes retained Home Assistant discovery/config topics and live `solar_assistant/.../state` topics.
- Sanitized live discovery findings are tracked in `docs/SOLARASSISTANT_DISCOVERY.md`.

Known scope:

- Treat the first SolarAssistant gateway pass as read-only monitoring.
- Do not publish MQTT setting-change messages such as `solar_assistant/.../set` topics in the first gateway pass.
- Do not design the project in a way that prevents future SolarAssistant write/control support.
- Add write/control later only through a separate safety, auth, audit, validation, and rollback design.
- Keep the SolarAssistant host, password, bearer token, and any MQTT credentials in local secrets or environment variables, not committed docs/config.
- The current local SolarAssistant password should be added to the broader credential rotation backlog.

Known interface options:

| Interface | Current fit | Notes |
|-----------|-------------|-------|
| REST `/api/v1/metrics` | Best first inventory probe | Returns topic, group, name, value, and unit; supports topic glob filters and single-value reads |
| MQTT on port `1883` | Preferred live monitoring candidate | Validated locally with separate MQTT credentials; broker semantics, retained state topics, Home Assistant discovery metadata, and bridge support fit the gateway model |
| WebSocket `/api/websocket` | Secondary live monitoring candidate | Validated locally with the configured REST/WebSocket password; streams metric definitions and data updates; supports topic filters and `max_frequency_s` throttling |
| `sacli` | Useful discovery and diagnostics helper | Can read snapshots with REST, stream metrics with WebSocket, output JSON/NDJSON, and handle local or cloud auth |

Common documented topics:

| Topic | Meaning |
|-------|---------|
| `total/pv_power` | Combined PV power in W |
| `total/load_power` | Total load power in W |
| `total/grid_power` | Grid power in W; negative means export |
| `total/battery_power` | Battery power in W; negative means charging |
| `total/battery_state_of_charge` | Battery state of charge in percent |
| `inverter_1/device_mode` | Current inverter mode |
| `battery_1/voltage` | Battery voltage in V |

Initial interface preference:

- Use REST first to inventory topics and metadata.
- Use `sacli` as an optional diagnostics shortcut if installing it is faster than maintaining probe code for a specific question.
- Prefer MQTT for production live monitoring now that separate MQTT credentials are validated.
- Keep WebSocket as a fallback or diagnostic stream because it is also validated and provides definition/data messages with topic filters.
- Keep REST as an inventory/snapshot probe rather than the default live-monitoring path unless MQTT and WebSocket are unsuitable.
- The gateway should still coalesce or rate-limit unchanged snapshots before writing to its outbox if SolarAssistant emits too frequently.

Documented write/control options for later:

| Option | Example | Notes |
|--------|---------|-------|
| MQTT setting topics | `solar_assistant/inverter_1/output_source_priority/set` | Publishes a new inverter setting value |
| Charger source priority | `solar_assistant/inverter_1/charger_source_priority/set` | Example documented by SolarAssistant |
| Max grid charge current | `solar_assistant/inverter_1/max_grid_charge_current/set` | Example documented by SolarAssistant |
| Shutdown battery voltage | `solar_assistant/inverter_1/shutdown_battery_voltage/set` | Example documented by SolarAssistant |
| Response topic | `solar_assistant/set/response_message/state` | SolarAssistant publishes setting-change responses here |
| Discovery of commands | Home Assistant discovery `command_topic` values | Can enumerate available settable topics for the specific inverter |

Future write/control requirements:

- Keep read monitoring and write/control code paths separate.
- Use an explicit allowlist of supported set topics and value ranges.
- Require website-side authorization, audit trail, request expiry, and operator-visible result state.
- Prefer the future cloud command inbox plus edge polling/acknowledgement pattern described in `docs/ARCHITECTURE.md`.
- Log command metadata and outcomes, but never log credentials or sensitive tokens.

Credential rotation backlog:

| Credential | Reason |
|------------|--------|
| SolarAssistant local development password | Required for API/CLI discovery; rotate before long-term production use |

Recommended approach:

- Use the non-deployable probe under `tools/solarassistant-discovery/` when connection details are available.
- Consider `sacli site <host> metrics --json` or `sacli site <host> metrics --watch` for manual diagnostics, but keep any configured credentials outside the repo.
- Configure a temporary local SolarAssistant password for REST/WebSocket/CLI discovery. Passwordless local access did not work during the initial probe.
- Keep REST/WebSocket credentials separate from MQTT credentials in local configuration.
- Do not add website writes or local outbox writes in the first probe.
- Do not commit credentials, raw connection strings, or unsanitized payload captures.
- Capture sanitized topic/API samples and document field names, units, cadence, retained flags, timestamps, and source/device identifiers.
- Set a local SolarAssistant password before REST or WebSocket probing; do not rely on default or blank credentials for committed workflows.

Discovery questions:

| Question | Why it matters |
|----------|----------------|
| MQTT, WebSocket, REST, or combination? | Determines whether production gateway subscribes, streams, polls, or combines sources |
| Topic structure | Determines source/provider mapping and whether one gateway can own the full SolarAssistant domain |
| Timestamp source | Determines idempotency key and whether event time or receive time should be persisted |
| Update cadence | Determines outbox write rate, batch size, retention, and database growth |
| Retained/QoS behavior | Determines startup behavior and duplicate handling |
| Device/source identifiers | Determines stable `SourceId`/`DeviceId` mapping |
| Units and sign conventions | Determines v9 power model shape and conversion needs |
| Snapshot vs event semantics | Determines whether to persist every update or coalesce repeated state |
| Control topics present? | Document settable topics for future write/control work while excluding them from first-pass gateway writes |

POC success criteria:

- Connect to SolarAssistant REST from the edge host or dev environment and inventory `/api/v1/metrics`.
- Connect to SolarAssistant MQTT live metrics from the edge host or dev environment.
- Connect to SolarAssistant WebSocket live metrics as a fallback/diagnostic stream.
- Produce a sanitized topic/API inventory.
- Identify candidate HVO power payloads and fields.
- Identify a stable idempotency key strategy.
- Estimate write volume for the local outbox and website API.
- Decide whether the production gateway should use MQTT, WebSocket, REST polling, or a combination.
- Confirm that first-pass production code remains read-only and does not publish setting changes.
- Document candidate write/control topics for a later implementation phase.

## Phased Implementation Plan

### Phase A - Document And Confirm

- Keep this document as the baseline for naming, isolation, and sequencing.
- Confirm the shared outbox requirements against Davis and JK BMS behavior.
- Hold TPLink and ESPHome implementation work.

### Phase B - SolarAssistant Discovery POC

- Build a small non-deployable probe for SolarAssistant REST, MQTT, and WebSocket connectivity.
- Capture sanitized samples and topic/API inventory.
- Document the initial power-domain mapping and update cadence.
- Confirm read-only first-pass scope and document future write/control candidates separately.
- Use the results as the third reference point before extracting shared outbox code.

### Phase C - Website Power Ingest Design

- Define normalized v9 power entities and typed ingest API shape.
- Keep inverter/load/charge/battery semantics distinct from per-device outlet power.
- Add tests around idempotency, validation, and duplicate handling before production forwarding.

### Phase D - Extract Shared Outbox

- Add `HVO.Edge.Outbox` with shared record state, retry, compaction, batch dequeue, and status snapshot.
- Keep domain payload mapping inside each edge service.
- Include tests for idempotency keys, retry scheduling, max-attempt failure, compaction, and status snapshots.

### Phase E - Bring Davis Inline

- Migrate Davis onto the shared outbox with no API or UI behavior change.
- Preserve archive/live distinction as metadata or a Davis-owned domain field.
- Preserve Davis per-record batch failure handling.
- Keep the current Davis image/container behavior stable unless a separate deployment PR changes it.

### Phase F - Bring JK BMS Inline

- Migrate JK BMS onto the shared outbox/coordinator.
- Preserve multi-device idempotency and changed config/device-info snapshots.
- Preserve sent-record compaction and placeholder/no-op development behavior.

### Phase G - Standardize Compose And Volumes

- Standardize edge image names, container names, volumes, labels, health checks, and `/data` layout.
- Keep edge services independently startable and deployable.
- Avoid a single monolithic gateway process.

### Phase H - SolarAssistant Gateway

- Create `HVO.Gateway.SolarAssistant` only after discovery and website power ingest design are complete.
- Give it its own local data directory, outbox, logs, config, health endpoint, and status UI if useful.
- Forward only typed website API payloads, not raw vendor topics.

### Phase I - Later Work

- Defer ESPHome until hardware/topics are available.
- Defer TPLink until the desired UI/control/monitoring boundary is decided.
- Revisit brokered ingest only if observatory scale or integration needs justify the added operational complexity.
