# SolarAssistant Rollout Plan

Status: active plan for completing SolarAssistant as the first shared edge outbox migration and as a complete power gateway, not just a single aggregate telemetry forwarder.

Last updated: 2026-05-28

## Goals

- Make SolarAssistant the first gateway migrated to the shared `HVO.Edge.Outbox` runtime pattern.
- Preserve the current working aggregate power ingest path while adding a complete plan for inventory, configuration, energy, and inverter detail data.
- Keep the gateway read-only until command/control has a separate safety, authorization, audit, and edge-execution design.
- Ensure SolarAssistant is not considered complete until every discovered data category is either implemented, explicitly local-only, or explicitly deferred with a reason.

## Required Process For Gateway Work

Before migrating or extending any gateway, review the gateway's device communication implementation first.

For SolarAssistant this means reviewing:

- REST `/api/v1/metrics` polling in `SolarAssistantRestClient` and `SolarAssistantSnapshotWorker`.
- MQTT discovery/state subscription in `SolarAssistantMqttClient`, `SolarAssistantMqttDiscoveryWorker`, and `SolarAssistantMqttInventoryStore`.
- Current local UI usage in `Status.razor`, `RestInventory.razor`, and `MqttInventory.razor`.
- Existing mapper coverage in `SolarAssistantPowerMapper` and `PowerReadingPayload`.
- Website ingest and persistence shape in `PowerIngestController` and `PowerModels`.

Do not start by extracting abstractions. Start by confirming what the gateway can observe, what the UI already uses, what the website can ingest, and what still needs semantic validation.

## Current SolarAssistant Sources

| Source | Current implementation | Current use | Documented observed shape |
|--------|------------------------|-------------|---------------------------|
| REST `/api/v1/metrics` | `SolarAssistantRestClient` | Primary source for normalized aggregate power readings and REST inventory metadata | `124` metrics: `inverter_1` `102`, `total` `17`, `battery_1` `5` |
| MQTT `homeassistant/#` and `solar_assistant/#` | `SolarAssistantMqttClient` and discovery worker | Read-only discovery/state inventory, state-topic availability, command-topic inventory, device metadata | `90` unique topics: `48` Home Assistant discovery entities and `42` state topics; live deployment also reported `14` command topics and `1` device |
| WebSocket `/api/websocket` | Not implemented as a production gateway path | Discovery context only | `104` definition/data topics |
| Local gateway UI | Blazor Server pages | Current status, local rolling trends, REST inventory, MQTT inventory, outbox/API sync | In-memory rolling power history only |

MQTT currently records discovery metadata and state payload kinds, not the state payload values. REST currently provides the mapped telemetry values sent to the cloud.

## Existing Mapped Aggregate Power Snapshot

Current destination: `POST /api/v1/power/readings`.

Current outbox metadata should use `PayloadType = power.reading` and `PayloadVersion = 1`. When written as a human-readable stream identifier, this document uses `power.reading.v1`.

| HVO field | Current SolarAssistant topics or aliases |
|-----------|------------------------------------------|
| `SourceId` | configured `TotalSourceId` |
| `SourceSystem` | `solarassistant` |
| `DeviceId` | configured `TotalDeviceId` |
| `RecordedAtUtc` | gateway REST poll time |
| `PvPowerW` | `total/pv_power` |
| `LoadPowerW` | `total/load_power` |
| `GridPowerW` | `total/grid_power` |
| `BatteryPowerW` | `total/battery_power` |
| `SystemPowerW` | `total/system_power`, `total/power` |
| `BatteryStateOfChargePercent` | `total/battery_state_of_charge` |
| `BatteryVoltageV` | `total/battery_voltage`, `battery_1/voltage` |
| `BatteryCurrentA` | `total/battery_current`, `battery_1/current` |
| `BatteryCapacityKwh` | `total/battery_capacity`, `battery_1/capacity` |
| `GridVoltageV` | `total/grid_voltage`, `inverter_1/grid_voltage` |
| `GridFrequencyHz` | `total/grid_frequency`, `inverter_1/grid_frequency` |
| `OutputVoltageV` | `total/ac_output_voltage`, `inverter_1/ac_output_voltage`, `inverter_1/output_voltage` |
| `OutputFrequencyHz` | `total/ac_output_frequency`, `inverter_1/ac_output_frequency`, `inverter_1/output_frequency` |
| `LoadPercentage` | `total/load_percentage`, `inverter_1/load_percentage` |
| `InverterMode` | `total/inverter_mode`, `inverter_1/device_mode` |
| `OutputSourcePriority` | `total/output_source_priority`, `inverter_1/output_source_priority` |
| `ChargerSourcePriority` | `inverter_1/charger_source_priority` |

These fields are already represented by the website `PowerReading` ingest model and central power-system snapshot composition.

## Existing Local UI Data Uses

The current SolarAssistant local gateway UI already demonstrates the data categories the rollout must treat intentionally.

| UI area | Current data used | Completion implication |
|---------|-------------------|------------------------|
| Gateway summary | Health state, REST status, MQTT status, forwarding status, last snapshot, metric counts, source id | Keep as local gateway runtime status and later expose normalized gateway status centrally. |
| Current Snapshot | PV, load, grid, battery, system power, load percentage | Keep as high-cadence `power.reading.v1`. |
| Battery State | SoC, voltage, current, capacity, inverter mode | Keep aggregate values in `power.reading.v1`; validate duplicated battery source meanings before adding more. |
| Recent Power Trends | In-memory PV/load/grid/battery power history | Central charts should use persisted `power.reading.v1`; local charts can remain bounded in-memory. |
| REST Metric Inventory | Topic, group, unit, classification | Promote durable inventory/classification snapshots where useful for central UI and rollout validation. |
| MQTT Discovery | Entities, devices, state topics, command topics, state payload metadata | Promote device/config/capability snapshots; do not execute command topics yet. |
| Read-only Configuration | Host, REST/MQTT ports, poll interval | Expand with SolarAssistant/inverter configuration and settings snapshots. |
| Website Forwarding | Pending, failed, last sent, endpoint, errors | Move to shared outbox snapshot and health semantics. |

## Target Data Streams

Use typed domain APIs and typed payload streams. Do not create one large SolarAssistant-specific cloud payload and do not send all gateway data through one generic endpoint.

The payload type/version names below are planning names. Each stream still needs a DTO, validation rules, idempotency key, retention expectation, and read-model/UI use before implementation.

| Stream | Payload type | Cadence | Initial cloud API direction | Retention/use | Purpose |
|--------|--------------|---------|-----------------------------|---------------|---------|
| Aggregate power reading | `power.reading.v1` | High cadence, current REST poll cadence | Existing `POST /api/v1/power/readings` | Time-series history and latest snapshot composition | Central power status, recent history, charts, source precedence. |
| Energy counters | `power.energy.v1` | Medium cadence or change-detected | New typed power energy API/table or a deliberate extension after validation | Long-term history/rollups | Daily/monthly power UI, capacity planning, energy import/export history. |
| Inverter detail | `power.inverter-detail.v1` | Medium cadence or same as power if cheap | New typed inverter detail API/table | Diagnostic history, probably shorter retention than aggregate/energy | PV string diagnostics, inverter temperature, apparent power, status bits. |
| Device inventory | `power.device-inventory.v1` | Low cadence or change-detected | New typed inventory/config API/table | Latest plus change history | Model, serial, firmware, manufacturer, discovered device identity. |
| Configuration snapshot | `power.configuration.v1` | Low cadence or change-detected | New typed config snapshot API/table | Latest plus change history | Output/charger priority, charge limits, voltage thresholds, available select options. |
| Command/capability inventory | `power.command-capability.v1` or included in configuration | Low cadence or change-detected | Inventory only | Latest plus change history | Record command topics/options without enabling writes. |
| Gateway runtime status | `gateway.status.v1` | Low cadence or poll/read endpoint | Future normalized gateway status endpoint | Latest/recent operational status | Source freshness, outbox health, current sync state, local gateway state. |

The first migration keeps only `power.reading.v1` forwarding behavior unchanged. The rollout is complete only after the other stream categories are implemented or explicitly classified as local-only/deferred.

## Field Matrix

| Category | Examples | Source today | Destination | Completion requirement |
|----------|----------|--------------|-------------|------------------------|
| Aggregate live power | `total/pv_power`, `total/load_power`, `total/grid_power`, `total/battery_power`, `total/system_power` | REST | `power.reading.v1` | Already mapped; preserve behavior during outbox migration. |
| Aggregate AC and battery status | grid/output voltage/frequency, load percentage, SoC, battery voltage/current/capacity, inverter mode, source priorities | REST | `power.reading.v1` | Already mapped where central model exists; preserve and test aliases. |
| Energy counters | `total/pv_energy`, `total/load_energy`, `total/grid_energy_in`, `total/grid_energy_out`, `total/battery_energy_in`, `total/battery_energy_out` | MQTT discovery advertised; not confirmed as observed REST values | `power.energy.v1` | Confirm values, units, reset behavior, monotonicity, sign convention, and cadence before persistence. |
| PV string diagnostics | `inverter_1/pv_power_1`, `pv_power_2`, `pv_voltage_1`, `pv_voltage_2`, `pv_current_1`, `pv_current_2` | REST/MQTT discovery candidates | `power.inverter-detail.v1` or local-only if not useful centrally | Decide whether central string-level diagnostics are needed; document result. |
| Inverter load detail | `inverter_1/load_power`, `inverter_1/load_apparent_power`, `inverter_1/system_and_load_power` | REST/MQTT discovery candidates | `power.inverter-detail.v1` | Validate whether values differ from aggregate totals before persistence. |
| Battery detail | `battery_1/power`, `battery_1/state_of_charge`, `inverter_1/battery_voltage`, `inverter_1/battery_current`, `inverter_1/battery_power` | REST/MQTT discovery candidates | `power.reading.v1`, `power.inverter-detail.v1`, or local-only | Avoid duplicate central fields unless source meaning differs from aggregate totals. |
| Hardware identity | `model_name`, `model_number`, `serial_number`, `firmware_version`, MQTT device manufacturer/model/software version | REST/MQTT discovery | `power.device-inventory.v1` | Implement low-frequency/change-detected snapshots for central UI. |
| Settings/selects | source priorities, max charge current, max grid charge current, shutdown/back-to-battery voltage, select options | REST/MQTT discovery | `power.configuration.v1` | Implement read-only configuration snapshots; no writes. |
| Command topics | `solar_assistant/.../set`, Home Assistant `command_topic` | MQTT discovery | command capability inventory only | Record capabilities if useful; command execution is deferred. |
| Temperatures/status bits | inverter temperature, `status_1` through `status_4`, diagnostic flags | REST/MQTT discovery candidates | `power.inverter-detail.v1` or local-only | Centralize if useful for alerts/history; otherwise local-only with documented reason. |
| WebSocket topics | definition/data topics | WebSocket discovery only | deferred | Do not add production WebSocket dependency unless REST/MQTT cannot supply needed values. |

## Cloud API Strategy

Use typed domain APIs by data purpose:

- Keep aggregate power telemetry on `/api/v1/power/readings`.
- Add typed power endpoints for energy, inverter detail, device inventory, and configuration only when their schemas are defined.
- Keep gateway identity in `SourceId`, `SourceSystem`, `DeviceId`, and payload metadata.
- Avoid `/api/v1/solarassistant/...` for normalized power-domain data.
- Avoid a generic catch-all ingest endpoint until typed domain contracts are stable.
- Reuse existing domain scopes where appropriate, but add narrower scopes if inventory/config/control boundaries need different authorization than high-cadence telemetry ingest.

Rationale:

- Typed APIs allow field-level validation and idempotency rules that match the domain.
- Different data classes have different cadence, retention, and UI behavior.
- Inventory/config/detail records should not bloat high-cadence power history.
- The website can still render a rich SolarAssistant UI by joining typed streams.

Endpoint design rules:

- Each ingest endpoint returns per-record inserted/skipped/failed results where a gateway can safely dead-letter individual bad records.
- Each endpoint defines its central idempotency key before gateway forwarding is implemented.
- Each endpoint documents validation ranges, string length limits, and whether duplicate observations are skipped or update an existing latest snapshot.
- Read APIs should support central UI use without the website directly reaching the LAN SolarAssistant instance.

Initial scope mapping:

| Stream | Likely ingest scope | Notes |
|--------|---------------------|-------|
| `power.reading.v1` | `ingest:power` | Already implemented through `/api/v1/power/readings`. |
| `power.energy.v1` | `ingest:power` | Power-domain telemetry, but may need separate read models/rollups. |
| `power.inverter-detail.v1` | `ingest:power` | Power-domain diagnostics. |
| `power.device-inventory.v1` | `ingest:power` or future inventory scope | Decide when the central inventory API is designed. |
| `power.configuration.v1` | `ingest:power` or future config scope | Read-only snapshot ingest only; not control. |
| Command/capability inventory | future inventory/config scope | Must not grant command execution. |

## Shared Local Outbox Strategy

The shared outbox should be a gateway library first, not a required local daemon.

The shared outbox owns:

- SQLite persistence for durable telemetry records.
- Idempotent enqueue/dedupe.
- Pending selection and batch limits.
- Retry/backoff scheduling.
- Terminal failure/dead-letter marking.
- Sent-record retention and cleanup.
- Outbox snapshot and health-state inputs.

The gateway owns:

- Device communication and protocol-specific polling.
- Source-specific field mapping and semantic validation.
- Local UI state and local-only history.
- Choosing which payload types are produced.
- Payload-specific HTTP response mapping where the cloud endpoint returns domain-specific failures.

The cloud API owns:

- Authentication and authorization scope checks.
- Domain validation.
- Idempotency at the central persistence boundary.
- Central tables/read models.
- API response shape for inserted/skipped/failed records.

Recommended shared outbox record metadata:

| Field | Purpose |
|-------|---------|
| `SourceId` | Stable ingest source id. |
| `DeviceId` | Optional physical/logical device id. |
| `PayloadType` | Routing and schema identity, for example `power.reading`. |
| `PayloadVersion` | Contract version, for example `1`. |
| `RecordedAtUtc` | Source observation timestamp and idempotency component. |
| `PayloadJson` | Serialized typed payload. |
| `Status` | Pending, sent, failed. |
| `AttemptCount` | Retry counter. |
| `LastAttemptedAtUtc` | Diagnostic and retry metadata. |
| `NextRetryAtUtc` | Backoff schedule. |
| `SentAtUtc` | Successful send timestamp. |
| `LastError` | Last current or terminal error. |
| `CreatedAtUtc` | Local enqueue timestamp. |

Initial idempotency should include `SourceId`, `PayloadType`, and `RecordedAtUtc`, with `DeviceId` included where the source can emit multiple device records at the same timestamp.

Forwarding rules:

- The shared outbox selects and updates records generically, but each payload type has an explicit destination mapping.
- A gateway may send multiple API calls per sweep when pending records target different payload types or endpoints.
- A gateway must not combine different payload types into one cloud request unless the target API explicitly supports that envelope.
- A transient failure for one destination should not block unrelated destinations in the same gateway.
- The shared outbox should keep enough metadata to show per-payload-type backlog and last error in local UI.

Migration compatibility:

- Existing SolarAssistant local SQLite outbox rows do not contain `PayloadType` or `PayloadVersion` today.
- The migration PR must decide whether to migrate existing rows, tolerate only new rows, or provide a safe compatibility path for unsent old rows.
- If compatibility is not needed because the deployment can tolerate draining/clearing the local queue, that must be stated explicitly before deploy.

## Isolation Rules

- One payload type backlog must not block another payload type.
- One device's bad data must not block another device's records.
- One invalid record should be dead-lettered independently when the cloud API reports a per-record validation failure.
- Historical failed rows should degrade/warn but should not keep current healthy forwarding critical forever.
- Current sync failure plus active backlog can be critical.
- MQTT command topics are telemetry/config inventory only; they are not commands in the telemetry outbox.

## Validation And Evidence Requirements

Some SolarAssistant fields are valuable but cannot be persisted centrally until their semantics are known. For every field family added after `power.reading.v1`, capture evidence in docs or tests for:

- Unit and scale.
- Sign convention, especially grid and battery flow values.
- Reset behavior for counters.
- Whether the value is instantaneous, averaged, cumulative, configured, or enumerated.
- Expected cadence and whether unchanged values should be resent.
- Source precedence when REST and MQTT expose the same apparent value.
- Whether the value belongs in high-cadence history, low-frequency snapshots, latest-only UI, or local-only diagnostics.

MQTT state values need extra review because the current gateway tracks state-topic availability and payload kind, not the actual value stream. Adding MQTT values to telemetry requires a separate mapper and tests.

## Testing And Simulation Strategy

Testing must cover the whole data path, not only the outbox table. SolarAssistant is a live local gateway with a frontend, a background collector, a local durable queue, and cloud-facing API calls. The test strategy should prove each seam independently and then prove the integrated flow with fakes or simulators.

Existing useful patterns in this repo:

- SolarAssistant tests already use a fake `ISolarAssistantClient` for worker-level REST polling tests.
- SolarAssistant outbox tests already use an in-memory SQLite database and a capturing `HttpMessageHandler` for forwarding tests.
- SolarAssistant mapper and inventory tests already cover known REST aliases, metric classification, and MQTT discovery parsing.
- Website power tests cover the ingest controller, API endpoint behavior, power-system snapshot composition, view-model formatting, and bUnit rendering for the live power card.
- Davis and JK BMS tests show the preferred simulator pattern for protocols that need a fake server/transport.
- Hardware-dependent tests should be marked `TestCategory=Live` and excluded from normal CI unless explicitly requested.

Required test layers:

| Layer | Purpose | Preferred approach |
|-------|---------|--------------------|
| REST client/collector | Prove SolarAssistant metric payloads are parsed and poll failures are handled | Fake `ISolarAssistantClient` for normal tests; optional local HTTP simulator if `SolarAssistantRestClient` behavior needs coverage. |
| MQTT discovery/state | Prove discovery payloads, state topics, command topics, and payload kinds are classified correctly | Unit tests for `SolarAssistantMqttInventoryStore`; add a fake MQTT packet/server simulator only when value-stream mapping or reconnect behavior is implemented. |
| Field mapping | Prove REST/MQTT values map into typed DTOs with correct aliases, units, signs, and null behavior | Mapper unit tests with sanitized fixture metrics. |
| Local UI state | Prove latest snapshot, inventory summaries, health chips, and local rolling history are populated without blocking the UI | Worker tests with fake clients plus bUnit tests for components when central or local UI behavior changes. |
| Shared outbox storage | Prove enqueue, dedupe, payload metadata, per-payload-type isolation, retry scheduling, dead-lettering, and compaction | In-memory SQLite tests against shared outbox services. |
| Forwarding | Prove batches go to the right typed cloud endpoint and failures are handled per record | Capturing `HttpMessageHandler` or fake `IHttpClientFactory`; test multiple payload types/destinations once added. |
| Website ingest | Prove typed APIs validate, persist, skip duplicates, and return per-record failures | Controller/API tests using existing website test patterns. |
| Website live UI/read models | Prove central cards/pages render from persisted typed streams, not direct LAN access | Service/view-model tests plus bUnit component tests. |
| Deployment smoke | Prove container startup, REST/MQTT status, outbox forwarding, and website ingest work in the target environment | Manual or scripted verification after deploy; no secrets printed. |

Simulator expectations:

- Add lightweight simulators where they reduce reliance on live SolarAssistant hardware or local network state.
- Start with fixture-driven fakes for REST metrics and MQTT discovery messages because they are cheaper and deterministic.
- Add a local HTTP SolarAssistant simulator if REST auth, timeout, response-body handling, or malformed payload behavior needs end-to-end client coverage.
- Add a fake MQTT broker/server only when MQTT state values become an input to telemetry or reconnect/keepalive behavior becomes part of acceptance.
- Keep fixture payloads sanitized: no credentials, local hostnames, tokens, or raw sensitive values.

Frontend/live website expectations:

- The central website must consume typed persisted/read-model data, not call SolarAssistant directly.
- UI tests should cover empty, waiting, stale, partial, warning, and healthy states where the user would see different behavior.
- Central UI should tolerate missing optional streams. For example, `power.reading.v1` can be live while inventory/config is still pending.
- Local gateway UI should stay responsive when REST polling fails, MQTT is reconnecting, or the outbox has backlog.

Recommended minimum tests by rollout phase:

| Phase | Minimum tests |
|-------|---------------|
| Phase 1 | Field classification tests for any new matrix rules and sanitized fixture coverage for REST/MQTT examples. |
| Phase 2 | Shared outbox storage tests, SolarAssistant worker tests with fake REST client, forwarding tests with captured HTTP requests, health/outbox status tests, and regression tests for latest snapshot hydration. |
| Phase 3 | Inventory/config DTO validation tests, REST/MQTT fixture mapping tests, website ingest/read API tests, and UI tests for inventory/config rendering and missing-data states. |
| Phase 4 | Energy/detail semantic tests for units, signs, counter resets, duplicate handling, source precedence, API validation, and chart/read-model behavior. |
| Phase 5 | Website page/card component tests for live, stale, partial, and missing-stream states, plus end-to-end API/read-model tests with seeded data. |

## Current Implementation Limits

Phase 1 is a plan and field audit. It intentionally documents several gaps that later phases must not treat as already solved:

- `HVO.Edge.Outbox` currently provides health/status contracts and evaluation only. It does not yet provide shared durable SQLite storage, enqueue, forwarding, retry, compaction, or payload-type routing.
- SolarAssistant still has a gateway-specific SQLite outbox. Its current rows store `Payload` but do not store `PayloadType` or `PayloadVersion`.
- `PowerApiForwarder` is currently hard-wired to deserialize all pending records as `PowerReadingPayload` and post them to one configured power readings endpoint.
- Existing SolarAssistant outbox tests cover the current single-payload behavior, but not shared runtime behavior such as payload-type routing, per-payload-type isolation, compaction, mixed destinations, or dedupe including `PayloadType` and `DeviceId`.
- MQTT inventory tests currently cover parser/store behavior. There is no fake MQTT broker/server or reconnect/keepalive simulator because MQTT state values are not telemetry inputs yet.
- The current MQTT store records state-topic payload kind, length, count, retain flag, QoS, and last-seen time, not the actual state values.
- Existing mapper tests use targeted inline metric sets. There is not yet a sanitized full fixture representing all observed REST metrics or MQTT discovery/state topics.
- Local SolarAssistant gateway UI does not currently have bUnit coverage for status chips, current snapshot cards, rolling history, REST inventory, MQTT inventory, or outbox panels.
- Current website UI tests cover generic power snapshot/card behavior, not future SolarAssistant inventory, configuration, energy, or inverter-detail views.
- Latest snapshot hydration currently reads the latest local outbox `Payload` without payload-type filtering. The shared outbox migration must update this seam before non-`power.reading.v1` records are stored in the same outbox.

These are not Phase 1 blockers because Phase 1 is documentation and audit. They are explicit inputs to Phase 2 and later acceptance criteria.

## SolarAssistant Completion Definition

SolarAssistant is complete only when:

- `power.reading.v1` continues to work through the shared outbox with equivalent behavior.
- REST and MQTT observed fields are classified in a maintained matrix.
- Energy counters are either implemented or deferred with documented missing validation.
- Device inventory snapshots are implemented for central UI or explicitly kept local with a reason.
- Configuration snapshots are implemented as read-only data or explicitly kept local with a reason.
- Inverter detail/status/PV string metrics are implemented or explicitly classified local-only/deferred.
- Command topics are inventoried or explicitly ignored, and no write path is introduced.
- Tests cover mapper aliases, field classification, outbox enqueue/dedupe/retry/dead-letter behavior, and cloud API response handling.
- Tests or simulators cover the collector-to-UI path: source fixtures, mapper, worker state, local outbox, forwarding, website ingest, read model, and relevant UI states.
- Documentation states which SolarAssistant data powers local UI, central UI, charts, and diagnostics.
- A deployment/verification checklist exists for the first SolarAssistant container rollout that changes forwarding behavior.

## Rollout Phases

### Phase 1: Plan And Field Audit

Scope:

- Keep this document current.
- Review REST, MQTT, and WebSocket discovery context.
- Confirm local UI usage and current central API coverage.
- Produce or update the field matrix above as live evidence changes.

Acceptance criteria:

- Every discovered field family has an intended destination or a deferred/local-only reason.
- No secrets or raw sensitive payloads are documented.
- Current implementation limits and missing test/simulator coverage are documented so later phases do not assume they already exist.

### Phase 2: Shared Outbox For `power.reading.v1`

Scope:

- Add minimal shared outbox primitives needed by SolarAssistant.
- Add `PayloadType` and `PayloadVersion` to local outbox metadata.
- Preserve existing `/api/v1/power/readings` request shape and endpoint.
- Preserve local dashboard history and latest snapshot hydration.

Acceptance criteria:

- SolarAssistant posts equivalent aggregate power batches.
- Existing SolarAssistant power mapper and outbox tests still pass or are migrated to the shared implementation.
- Shared outbox tests cover enqueue/dedupe/retry/dead-letter/snapshot behavior.
- Worker tests prove fake REST metrics still populate latest snapshot, local history, and outbox records without a live SolarAssistant device.
- Forwarding tests prove the shared outbox sends only `power.reading.v1` records to `/api/v1/power/readings` and handles per-record validation failures.
- This phase alone does not make SolarAssistant complete; it only completes the first shared-outbox migration for the existing aggregate reading stream.

### Phase 3: Inventory And Configuration Streams

Scope:

- Define low-frequency/change-detected payloads for device inventory and read-only configuration.
- Add central persistence/read APIs needed for central UI.
- Produce inventory/config records from REST/MQTT discovery without enabling writes.

Acceptance criteria:

- Central UI can identify the SolarAssistant inverter/device, firmware/model, and available read-only settings/options.
- Command topics are represented only as capabilities/inventory.
- Configuration snapshots are read-only evidence of current settings; no endpoint or UI action can publish to SolarAssistant command topics.
- Fixture-driven tests cover REST and MQTT discovery examples for inventory/config mapping.
- Website API and UI tests cover present, missing, stale, and partial inventory/config states.

### Phase 4: Energy And Inverter Detail Streams

Status: implemented in `power.energy.v1` and `power.inverter-detail.v1` streams.

Scope:

- Confirm observed energy counter values, reset behavior, monotonicity, sign convention, and cadence.
- Decide which PV string, inverter load detail, temperature, and status-bit values belong centrally.
- Add typed payloads and APIs for implemented values.

Implementation notes:

- Energy counters are persisted only when observed and are kept as separate import/export and charge/discharge counters; no signed net-energy inference is made.
- Counter drops are flagged as reset evidence instead of being smoothed or rewritten.
- Inverter detail is bounded to PV string, load, inverter-side battery, temperature, and status fields; arbitrary raw metrics remain out of central history.

Acceptance criteria:

- Energy and inverter detail data can support website charts/diagnostics without overloading aggregate power readings.
- Any remaining fields are explicitly marked local-only or deferred.
- Tests cover energy counter reset/monotonicity assumptions and sign conventions before central persistence is enabled.

### Phase 5: Central UI Integration

Status: implemented on the central power status card.

Scope:

- Build central website views from typed streams.
- Keep local gateway pages as deep diagnostics.
- Link central SolarAssistant/device cards back to the local gateway dashboard where appropriate.

Acceptance criteria:

- Website UI can show live power, trends, energy summaries, device identity, and configuration snapshots without requiring direct LAN access to SolarAssistant.

Implementation notes:

- The central power card now joins live aggregate power, JK BMS bank detail, SolarAssistant inventory/configuration, energy counters, and inverter detail snapshots from typed website read models.
- The website still does not connect directly to SolarAssistant REST/MQTT; a configurable diagnostics link can point operators back to the local gateway UI.
- Local gateway pages remain the detailed troubleshooting surface for REST/MQTT/outbox state.

## Non-Goals

- Do not add SolarAssistant write/control behavior in this rollout.
- Do not require a shared local outbox daemon.
- Do not replace typed domain APIs with a generic ingest endpoint.
- Do not persist every SolarAssistant metric at high cadence by default.
- Do not store raw secrets, credentials, or unsanitized payload captures in docs or central tables.

## Deployment And Verification Expectations

Any PR that changes SolarAssistant container behavior or forwarding behavior must include a deployment/verification note before being considered complete.

Minimum verification:

- Container starts with existing `.env` preserved on the target host.
- REST polling succeeds or reports an expected disabled/error state.
- MQTT discovery connects or reports an expected disabled/error state.
- Local dashboard loads and shows current outbox/API sync state.
- Existing `power.reading.v1` records are queued and forwarded to `/api/v1/power/readings` when configured.
- Website ingest responds with inserted/skipped/failed counts and per-record validation failures are handled correctly.
- Historical failed rows degrade/warn without masking current successful sends.
- No secrets or API keys are printed in logs, docs, or verification output.

## References

- `docs/SOLARASSISTANT_DISCOVERY.md`
- `docs/GATEWAY_FOUNDATION_REVIEW.md`
- `src/HVO.Gateway.SolarAssistant/SolarAssistant/SolarAssistantRestClient.cs`
- `src/HVO.Gateway.SolarAssistant/SolarAssistant/SolarAssistantPowerMapper.cs`
- `src/HVO.Gateway.SolarAssistant/SolarAssistant/PowerReadingPayload.cs`
- `src/HVO.Gateway.SolarAssistant/SolarAssistant/SolarAssistantMetricInventory.cs`
- `src/HVO.Gateway.SolarAssistant/SolarAssistant/Mqtt/SolarAssistantMqttInventoryStore.cs`
- `src/HVO.Gateway.SolarAssistant/Workers/SolarAssistantSnapshotWorker.cs`
- `src/HVO.Gateway.SolarAssistant/Workers/SolarAssistantMqttDiscoveryWorker.cs`
- `src/HVO.Gateway.SolarAssistant/Components/Pages/Status.razor`
- `src/HVO.Gateway.SolarAssistant/Components/Pages/RestInventory.razor`
- `src/HVO.Gateway.SolarAssistant/Components/Pages/MqttInventory.razor`
- `src/HVO.WebSite.v9/Controllers/PowerIngestController.cs`
- `src/HVO.WebSite.v9/Models/PowerModels.cs`
- `src/HVO.Edge.Contracts/TelemetryEnvelope.cs`
- `src/HVO.Edge.Outbox/EdgeOutboxHealthEvaluator.cs`
