# Gateway Foundation Review

Status: active design baseline for the next edge-gateway work. This document turns the current Davis, JK BMS, SolarAssistant, and SmartShunt experience into a concrete foundation plan before deeper UI polish or additional hardware models.

Last updated: 2026-05-28

## Recommendation Sequence

Use this order for near-term work:

1. Architecture review and gateway foundation.
2. Shared edge outbox foundation.
3. Continue hardware and cross-discipline models.
4. Main website UI built on stable gateway contracts.

The goal is to avoid building the main website or more polished local UIs around temporary gateway-specific shapes.

## Current Gateway Inventory

| Gateway | Project | Domain | Current maturity | Notes |
|---------|---------|--------|------------------|-------|
| Davis | `src/HVO.Hardware.DavisVantagePro2` | Weather | High | Best current UI/shell baseline; mature local state and outbox behavior. |
| JK BMS | `src/HVO.Hardware.JkBms` | Battery bank fleet | Medium-high | Strong live BLE telemetry and BMS ingest; session model and shared BLE constraints still evolving. |
| SolarAssistant | `src/HVO.Gateway.SolarAssistant` | Inverter, solar, load, battery aggregate | Medium-low | Shared outbox storage, typed power/inventory/config/detail/status streams, central UI, and historical-failure health semantics are implemented. Remaining risk is fixture/simulator coverage for MQTT value streams and local UI. |
| SmartShunt | `src/HVO.Hardware.VictronSmartShunt` | Battery monitor | Medium | Read-only public BLE telemetry works; private enrichment/write paths intentionally deferred. |

## Shared Gateway Shape

All current gateways now follow the same broad shape:

| Layer | Responsibility |
|-------|----------------|
| Device/source adapter | Talks to TCP, BLE, REST, MQTT, or vendor protocol. |
| Worker/poller | Schedules reads, applies reconnect/backoff, and maps source data to HVO payloads. |
| Local state | Tracks latest sample, health inputs, diagnostics, and UI summary. |
| Local SQLite outbox | Durably stores records before upstream API delivery. |
| API forwarder | Sends batches to typed HVO.WebSite ingest endpoints. |
| Health/status endpoints | Support container health, diagnostics, and local operator views. |
| Local dashboard shell | Shows gateway identity, live state, sample timestamp, outbox, and API sync state. |

## Shared Contracts To Define

The next implementation should extract contracts before extracting heavy runtime code.

### Gateway Identity

Purpose: identify what the gateway is and what source/device it represents.

Suggested fields:

| Field | Meaning |
|-------|---------|
| `GatewayId` | Stable service ID, for example `davis-vp2`, `jkbms`, `solarassistant`, `smartshunt`. |
| `DisplayName` | Operator-facing name. |
| `Domain` | Weather, BMS, Power, BatteryMonitor, Environment, etc. |
| `SourceId` | Upstream ingest source ID. |
| `DeviceId` | Optional physical device or fleet ID. |
| `RuntimeHost` | Optional local host/container identity. |

### Gateway Runtime Status

Purpose: expose current runtime state consistently across dashboards and future main website cards.

Suggested fields:

| Field | Meaning |
|-------|---------|
| `ObservedAtUtc` | Latest source sample timestamp. |
| `SampleState` | Waiting, Live, Stale, Disabled, Error. |
| `LastError` | Last source/worker error, if any. |
| `Capabilities` | Read-only, writes-supported, private-enrichment, live-hardware, etc. |
| `DataPath` | Gateway-specific but normalized enough for UI, for example `public`, `REST`, `BLE`, `MQTT`. |

### Gateway Health Snapshot

Purpose: separate process health from domain health.

Suggested fields:

| Field | Meaning |
|-------|---------|
| `State` | Healthy, Warning, Critical, Unknown. |
| `EvaluatedAtUtc` | Time health was evaluated. |
| `Alerts` | Structured code/severity/message list. |
| `SourceFreshness` | Source sample freshness state. |
| `OutboxState` | Outbox state summary. |
| `ApiSyncState` | Current forwarding state summary. |

### Gateway Outbox Snapshot

Purpose: standardize local durable-delivery status.

Suggested fields:

| Field | Meaning |
|-------|---------|
| `PendingCount` | Records waiting or scheduled for retry. |
| `FailedCount` | Records marked terminal failed. |
| `LastSentAtUtc` | Last successful upstream send. |
| `LastBatchCount` | Last successful sent batch size. |
| `LastError` | Last forwarder error. |
| `CurrentSyncState` | Idle, Pending, Sending, Healthy, Degraded, Failing. |
| `HistoricalFailureState` | None, Present, OverThreshold. |

### Telemetry Envelope

Purpose: avoid every gateway inventing its own metadata wrapper around domain payloads.

Suggested fields:

| Field | Meaning |
|-------|---------|
| `SourceId` | Source key used by central ingest. |
| `DeviceId` | Optional physical device ID. |
| `RecordedAtUtc` | Domain sample timestamp. |
| `PayloadType` | Weather, BmsReading, PowerReading, BatteryMonitor, etc. |
| `PayloadVersion` | Contract version. |
| `Payload` | Domain-specific DTO. |

## Footer And Local UI Contract

The gateway footer now uses a consistent five-slot contract:

| Slot | Meaning | Examples |
|------|---------|----------|
| 1 | Live/health | `Live loop active`, `Gateway warning`, `7/7 banks connected` |
| 2 | Source identity | `Davis VP2`, `JK fleet: 7 bank(s)`, `solarassistant-total`, `smartshunt-lifepo4` |
| 3 | Full sample datetime | `26 May 2026 - 8:08:13 PM`, with freshness indicator where the gateway has a freshness threshold |
| 4 | Outbox counts | `Outbox: 0 pending - 0 failed` |
| 5 | API sync state | `API sync healthy`, `API sync pending`, `API sync degraded` |

Keep this contract unless a later shared shell package replaces the duplicated layout-state classes.

## Shared Edge Outbox Direction

SolarAssistant is the first planned shared-outbox migration target, but its rollout is broader than an outbox refactor. Use `docs/SOLARASSISTANT_ROLLOUT_PLAN.md` as the active completion plan for SolarAssistant field coverage, cloud API stream shape, local UI data needs, and typed payload rollout.

### Decision

Start with a shared library, not a required standalone daemon.

Recommended package name:

```text
src/HVO.Edge.Outbox
```

This keeps every gateway independently deployable while eliminating copied durable-delivery logic. A daemon can come later if multiple gateways on the same Pi need one shared queue and one retry engine.

### Why Library First

Benefits:

- Lowest deployment risk.
- Preserves gateway independence.
- Reduces copied schema, retry, health, and cleanup code.
- Gives the future daemon a proven internal implementation.

Avoid daemon-first because:

- It creates a new runtime dependency for every gateway.
- It adds local API/auth/availability decisions before the shared behavior is stable.
- It complicates failure modes during active hardware work.

### Initial Library Scope

Status: implemented for the SolarAssistant migration, with gateway-specific destination routing still owned by each gateway.

Include:

- common outbox entity model
- SQLite DbContext or reusable EF model configuration
- enqueue/dedupe helper
- forwarder base abstractions
- retry/backoff policy
- sent-record cleanup policy
- common `GatewayOutboxSnapshot`
- common health evaluator for pending/failed/current-sync state

Do not include yet:

- cross-process daemon API
- command/control queue
- broad UI components
- database migrations for central website domains
- hardware-specific DTOs

## Outbox Health Semantics

The current gateways sometimes treat historical failed rows as a gateway-critical condition even when new records are forwarding successfully. That is operationally noisy.

Recommended health split:

| State | Meaning | UI/health treatment |
|-------|---------|---------------------|
| Current API sync | Whether new sends are succeeding now | Should drive `API sync healthy/pending/failing`. |
| Pending backlog | Queue depth waiting for send/retry | Warning when above threshold. |
| Historical failures | Terminal failed rows requiring operator cleanup or replay | Warning/degraded unless current sends are also failing. |

Initial rule:

- Current forwarding failure plus backlog can be critical.
- Historical failed rows alone should not keep the gateway critical forever if fresh records are forwarding successfully.
- The UI should still expose the failed count clearly.

## Cross-Discipline Model Direction

After the shared outbox library starts, the next useful domain model is a normalized power-system view.

Suggested model:

```text
PowerSystemSnapshot
```

Inputs:

- SolarAssistant: inverter/load/PV/grid aggregate.
- JK BMS: per-bank SoC, voltage, current, temperature, balance/alarms.
- SmartShunt: independent battery monitor voltage/current/power/SoC where trustworthy.

Purpose:

- Give the main website one coherent power status model.
- Preserve raw gateway detail locally.
- Make source precedence explicit when devices disagree.

Open source-precedence question:

| Value | Likely preferred source | Notes |
|-------|-------------------------|-------|
| PV/load/grid power | SolarAssistant | Native inverter aggregate. |
| Bank balance/cell health | JK BMS | Only source with per-cell/per-bank detail. |
| Battery bus current/power | SmartShunt or SolarAssistant | Needs live comparison and sign convention confirmation. |
| Fleet SoC | JK BMS aggregate or SolarAssistant | SmartShunt SoC is currently not trusted until device synchronization is resolved. |

## Main Website UI Direction

Do not start with deep main-site UI polish. Start with cards backed by stable contracts.

First useful pages:

| Page | Purpose |
|------|---------|
| `/status` | Whole observatory status summary. |
| `/gateways` | Gateway health/outbox/source freshness cards. |
| `/weather` | Davis weather summary and trends. |
| `/power` | Cross-discipline power system summary. |
| `/alerts` | Active and recent operational alerts. |

The main site should link back to local gateway dashboards for deep diagnostics until central models are mature.

## Proposed Implementation PRs

The first two implementation PRs in this plan are complete for SolarAssistant. The next useful work is no longer another SolarAssistant field-audit/schema PR; it is migrating the next gateway while preserving each gateway's protocol boundaries.

### PR 1: Foundation Contracts And Outbox Design

Status: complete.

Scope:

- Add `HVO.Edge.Outbox` skeleton or shared contract package.
- Add gateway identity/status/outbox snapshot contracts.
- Add tests for outbox health-state evaluation.
- Do not migrate existing gateways yet unless needed for test coverage.

Acceptance criteria:

- Shared contracts compile.
- Health-state evaluator distinguishes current sync failure, pending backlog, and historical failures.
- No gateway behavior changes except optional compile-only references.

### PR 2: Migrate One Gateway To Shared Outbox

Preferred first target: SolarAssistant.

Status: complete for SolarAssistant.

Reason:

- Its historical failed rows currently demonstrate the health-semantics problem.
- It is REST/MQTT based, so migration does not add BLE risk.

Acceptance criteria:

- SolarAssistant uses shared outbox status/health semantics.
- Existing API forwarding behavior remains equivalent.
- Historical failures degrade health without masking current successful sends.
- Focused tests cover the new evaluator and SolarAssistant mapping.
- Tests or simulators cover the collector, mapper, outbox, forwarding, website ingest, read-model, and UI states touched by the migration.
- The SolarAssistant rollout plan remains current and every observed REST/MQTT data category is implemented, explicitly local-only, or explicitly deferred with a reason before SolarAssistant is considered complete.
- The cloud API direction remains typed by data purpose, starting with existing `power.reading.v1` on `/api/v1/power/readings` and adding inventory/config/energy/detail streams only with defined schemas.

### PR 3: Migrate SmartShunt Or JK BMS

Preferred next target: SmartShunt.

Reason:

- It shares the power outbox shape with SolarAssistant.
- It avoids changing JK BMS while BLE/session behavior is still being hardened.

Acceptance criteria:

- SmartShunt uses shared outbox status/health semantics.
- BLE polling behavior is unchanged.
- Private/write behavior remains unchanged and intentionally deferred.
- Existing SmartShunt tests continue passing.

### PR 3B: JK BMS Fan-Out Isolation

Scope:

- Address fan-out isolation before or during any JK BMS shared-outbox migration.
- Ensure one bad device record cannot block unrelated BMS records.
- Ensure one failed destination cannot block destinations that succeeded.

Current risk:

- `ForwarderCoordinator` marks a batch sent only when all registered `IReadingForwarder` implementations succeed.
- On partial destination failure, every record in the batch remains pending and retries, even if some destinations accepted the data.

Acceptance criteria:

- Delivery state is tracked independently enough to avoid resending successful destination fan-outs solely because another destination failed.
- Per-device validation or serialization failures dead-letter only the affected device record.
- Existing BLE polling/session behavior remains unchanged.

### PR 3C: Davis Outbox Split

Scope:

- Separate telemetry outbox records from local UI/history/settings persistence before any Davis shared-outbox migration.
- Keep Davis console settings, station info snapshots, archive/history, and local operational database concerns gateway-owned.

Current risk:

- The Davis `OutboxDbContext` currently owns both telemetry outbox records and local `StationSettingsSnapshots`/`StationInfoSnapshots` tables.
- A direct shared-outbox migration would mix transport durability with gateway-owned console state unless the persistence boundary is split first.

Acceptance criteria:

- Weather telemetry outbox can move to shared outbox primitives without moving station settings/history ownership to shared code.
- Davis local pages continue to read/write their gateway-owned local state.
- Existing Davis tests continue passing.

### PR 4: Cross-Discipline Power Model

Status: partially implemented for the central power card. Keep expanding only after source semantics are documented.

Scope:

- Define `PowerSystemSnapshot` contract.
- Map SolarAssistant, JK BMS, and SmartShunt into a source-precedence model.
- Keep raw gateway UIs as detail surfaces.

Acceptance criteria:

- Source precedence documented in code/tests.
- No control/write paths introduced.
- Main website can consume a stable summary later.

### PR 5: Main Website Starter Status UI

Scope:

- Add simple main website gateway/system cards.
- Use shared gateway status and outbox contracts.
- Link to local dashboards for detail.

Acceptance criteria:

- No vendor-specific assumptions in the main UI.
- Status cards render from normalized contracts.
- UI remains intentionally simple until models stabilize.

## Follow-Up Questions

1. Should `HVO.Edge.Outbox` own EF Core types directly, or should it expose storage interfaces and provide SQLite as one implementation?
2. Should outbox failed rows have an operator reset/replay endpoint in each gateway?
3. Should local gateway `/status` payloads converge before or after outbox extraction?
4. Should shared gateway contracts live in `HVO.Edge.Contracts` separately from `HVO.Edge.Outbox`?
5. What is the first main-site audience: public observatory status, private operator dashboard, or both?

## Follow-On Roadmap Status

| Item | Status | Notes |
|------|--------|-------|
| SolarAssistant field audit PR | Complete for the implemented typed streams | REST inventory, MQTT discovery, mapper coverage, PV strings, inventory/config snapshots, command capabilities, inverter status, and gateway status are centrally represented through typed payloads. Energy counters are supported but only persisted when observed; the live deployment currently reports empty energy counters. No write/control path was added. |
| SmartShunt migration | Pending | Reuse the shared outbox path. Do not change BLE polling or private/write behavior. This remains the recommended next gateway migration because it is a power telemetry source but lower fan-out risk than JK BMS. |
| JK BMS migration | Pending, blocked on fan-out isolation | Fix per-destination/per-device isolation before migrating. One bad BMS record or one failed destination must not block unrelated records. |
| Davis split | Pending | Split telemetry outbox from gateway-owned local UI/history/settings persistence before migration. Davis local operational database concerns stay in the gateway. |
| Central power model expansion | Partial | Central UI now joins SolarAssistant typed streams and JK BMS bank detail. Add fields/endpoints only after source semantics are documented; prefer source-specific config/identity snapshots and typed normalized telemetry for high-cadence values. |

## Current Recommendation

`HVO.Edge.Contracts` plus `HVO.Edge.Outbox` now exist as small shared libraries and SolarAssistant has been migrated first. Keep them boring and testable. Next, migrate SmartShunt with unchanged BLE behavior. After that, fix JK BMS fan-out isolation before migration, and split Davis telemetry outbox from local operational persistence before migrating Davis.
