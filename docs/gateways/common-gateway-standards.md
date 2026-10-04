# Common Gateway Standards

This document defines shared HVO gateway behavior for the active direct Davis, JK BMS, EG4, and SmartShunt collectors and future direct collectors. Device-specific drivers may extend these standards, but should not redefine common lifecycle, outbox, telemetry, or health semantics without documenting why.

Status: Current standard. Use this as the baseline for current gateways and future gateway work.

Headless vNext executables also follow
`docs/architecture/EDGE_VNEXT_RUNTIME.md`, including `AddHvoEdgeRuntime`, mounted
configuration under `/app/config`, durable data under `/app/data`, secret files
under `/run/secrets`, and the shared protected diagnostics route group.

## Goals

- Keep gateway behavior consistent across device types.
- Put common retry, dead-letter, telemetry, and health rules in shared code where practical.
- Allow device-specific payloads, protocol diagnostics, and capabilities without duplicating infrastructure behavior.
- Make local gateway state understandable during outages without requiring cloud access.
- Avoid blocking live telemetry because of historical failures unless current forwarding is also failing.

## Shared Gateway Identity

Every gateway should expose and use a stable identity model.

| Field | Required | Purpose |
|-------|----------|---------|
| `GatewayId` | Yes | Stable HVO gateway identifier, for example `hvo-davis` or `hvo-eg4`. |
| `GatewayType` | Yes | Gateway implementation family, for example `davis-vantage-pro2`, `eg4`, or `jk-bms`. |
| `SiteId` | Yes where configured | Observatory/site identity used by cloud ingest and operations. |
| `SourceId` | Yes | Per-source identity used for idempotency and downstream attribution. For single-device gateways this can equal `GatewayId`. |
| `DeviceId` | Optional | Per-device identity for multi-device gateways such as BMS packs or inverter components. |
| `PayloadType` | Yes for shared outbox | Domain payload category, for example `weather.raw`, `power.reading`, `bms.reading`, `gateway.status`. |
| `PayloadVersion` | Yes for shared outbox | Payload contract version. Increment when the JSON contract changes incompatibly. |

## Common Outbox Standard

The edge outbox is a local, one-way, telemetry-only store-and-forward queue. It is not a command channel and must not carry cloud-to-device instructions.

### Required Record Fields

| Field | Purpose |
|-------|---------|
| `Id` | Local database identity. |
| `SourceId` | Source/gateway identity for idempotency and attribution. |
| `DeviceId` | Optional per-device identity. |
| `PayloadType` | Domain payload category. |
| `PayloadVersion` | Payload contract version. |
| `RecordedAtUtc` | Observation timestamp, not send timestamp. |
| `PayloadJson` | Serialized domain payload. |
| `Status` | Common lifecycle status. |
| `AttemptCount` | Number of delivery attempts. |
| `LastAttemptedAtUtc` | Last send attempt time. |
| `SentAtUtc` | Time the cloud accepted or safely skipped the record. |
| `NextRetryAtUtc` | Earliest retry time for pending records. |
| `LastError` | Bounded diagnostic text for the most recent failure. |
| `FailureKind` | Common failure classification. |
| `CreatedAtUtc` | Local enqueue time. |

Gateway-specific data belongs in `PayloadJson` or in gateway-owned extension tables. It should not change common status/retry semantics.

## Time Handling Standard

Gateway storage and API contracts should treat timestamps as instants first and display values second.

- Store observation, enqueue, retry, sent, and health timestamps as UTC. Field names should end in `Utc` where the contract shape allows it. Existing v9 `datetime2` columns without a suffix must continue to be interpreted as UTC until a schema migration explicitly changes them.
- Use `DateTimeOffset` for API DTOs when the offset is part of the payload contract. Use `DateTime` only for internal/storage models that are explicitly UTC and documented as such.
- Do not use server/container `ToLocalTime()` for operator display. In a deployed container this reflects the host/container timezone, which may not be the observatory, browser, gateway, or device timezone.
- Browser UIs may render UTC timestamps in the viewer's local timezone when the view is clearly user-local. Gateway/operator status pages should prefer a configured gateway display timezone so a headless wall display and remote browser see the same site-relative time.
- Gateways that read a device timezone, such as Davis, should use the device timezone for protocol-local values and convert source-local timestamps to UTC before storage or forwarding.
- Headless vNext collectors expose UTC diagnostics. Any future operator UI whose device lacks a reliable local-time concept should use an explicitly configured display timezone while continuing to store and forward UTC.
- Multi-device gateways may allow a per-device display timezone override. This is presentation metadata only; it must not change `RecordedAtUtc`, `ObservedAtUtc`, idempotency keys, or stale-age calculations.

Recommended configuration shape:

| Scope | Field | Purpose |
|-------|-------|---------|
| Gateway | `DisplayTimeZoneId` | IANA/Windows timezone ID used by local gateway UI when a device does not override it. |
| Device | `DisplayTimeZoneId` | Optional per-device display timezone for multi-subnet or remote-source gateways. |
| Device-derived | `DeviceTimeZoneId` or gateway-specific equivalent | Timezone reported by the device/protocol, when available and trustworthy. |

APIs may expose both the UTC instant and the display timezone metadata, for example `ObservedAtUtc` plus `DisplayTimeZoneId`. They should not replace UTC instants with preformatted local strings.

### Status Values

| Status | Meaning |
|--------|---------|
| `Pending` | Ready for future delivery when `NextRetryAtUtc <= now`. |
| `Sent` | Accepted by the cloud, or skipped as a duplicate by an idempotent cloud endpoint. |
| `Failed` | Not currently retried by the normal sweeper. Requires classification in `FailureKind`. |

### Failure Kinds

| FailureKind | Retry by default | Meaning |
|-------------|------------------|---------|
| `None` | N/A | No failure classification. Pending/sent records should normally use this. |
| `Permanent` | No | Dead-lettered record caused by local invalid payload, explicit remote rejection or a sender-specific permanent HTTP outcome. Requires code/configuration/operator action. |
| `RetryExhausted` | Periodically | A transient failure reached the configured retry limit. Gateway requeue workers return these rows to `Pending` periodically, without requiring a prior successful forward. |

### Sender HTTP outcome and recovery matrix

This table describes current source behavior, rather than a proposed uniform
policy. HTTP success must also pass the sender's batch acknowledgement validation.

| Sender | HTTP 401/403 | HTTP 408/429/5xx | Other unsuccessful HTTP | Invalid successful ACK | Explicit per-record rejection |
|---|---|---|---|---|---|
| [Davis](../../src/HVO.Hardware.DavisVantagePro2/Outbox/DavisOutboxBatchSender.cs) | Transient | Transient | Permanent | Transient | Permanent |
| [SmartShunt](../../src/HVO.Hardware.VictronSmartShunt/Outbox/SmartShuntOutboxBatchSender.cs) | Transient | Transient | Permanent | Transient | Permanent |
| [JK BMS](../../src/HVO.Hardware.JkBms/Outbox/JkBmsOutboxBatchSender.cs) | **Permanent** | Transient | Permanent | Transient | Permanent |
| [EG4](../../src/HVO.Hardware.Eg4/Outbox/Eg4OutboxBatchSender.cs) | **Permanent** | Transient | Permanent | Transient | Permanent |
| [HA exporter](../../src/HVO.Edge.Exporter.HomeAssistant/HomeAssistantOutboxBatchSender.cs), disabled in production | Permanent | Transient | Permanent | Transient | Permanent |

Transport exceptions and request timeouts are transient; locally invalid payloads
and unsupported payload contracts are permanent. Missing, duplicate or unknown
per-record outcomes are scheduled for retry by the shared forwarder.

After a Davis/SmartShunt credential correction, pending rows retry, and
retry-exhausted rows return through periodic requeue. Correcting JK/EG4 credentials
allows new observations to forward, but does **not** automatically requeue rows
already dead-lettered as `Permanent`. Observe failure kinds and counts before
claiming backlog recovery. Any desired alignment of these classifications needs
a separate reviewed code issue and regression tests; #415 changes no sender policy.

### Retry Rules

- Only `Pending` records are selected by the normal sweeper.
- Transient HTTP failures, timeouts, DNS failures, and temporary cloud outages keep records `Pending` with exponential backoff until `MaxRetryAttempts` is reached.
- When retry attempts are exhausted, set `Status=Failed`, `FailureKind=RetryExhausted`, and preserve a useful `LastError`.
- Remote validation failures from the cloud are dead letters. Set `Status=Failed`, `FailureKind=Permanent`, and store the cloud-provided reason.
- Locally invalid payload JSON is a dead letter. Set `Status=Failed`, `FailureKind=Permanent`, and avoid sending the batch until malformed records are removed from it.
- HTTP classification is sender-specific as listed above; surface authentication
  failures instead of assuming every sender treats them identically.
- Requeue operations must only automatically target `RetryExhausted`; permanent dead letters require operator/code/configuration action.

### Requeue Rules

- Each gateway's retry-requeue worker calls
  [RequeueRetryExhaustedAsync](../../src/HVO.Edge.Outbox/EdgeOutboxStore.cs)
  at startup and periodically (default `RetryExhaustedRequeueMinutes=15`). It does
  not check last-success state first. Only `Failed` + `RetryExhausted` rows qualify.
- Requeue resets status to `Pending`, attempt count to zero, failure kind to
  `None`, next retry to `DateTime.MinValue`, and appends a bounded UTC note.
- Unsafe automatic requeue: `Permanent` rows should not automatically return to `Pending` without a code/configuration fix and operator decision.
- `outbox-maintenance.sh` provides `summary`, `schema`, `archive` and `compact`;
  there is no requeue subcommand or published blanket SQL recovery recipe.
  Permanent-row recovery requires a separately approved, backed-up, reviewed
  operation selecting the affected source/records, correcting the actual reason,
  and proving central idempotency/continuity. Use the
  [SQLite checkpoint/isolated restore contract](sqlite-backup-and-rollback.md)
  before any such mutation. Preserve the original store and post-checkpoint data;
  do not bulk-requeue validation failures or bypass source-authority claims.

Shared [forwarder options](../../src/HVO.Edge.Outbox/EdgeOutboxOptions.cs) default
to 10 attempts and a 300-second maximum exponential backoff, with one-day sent
retention and 30-day failed retention. Gateway configuration can override them.

#### Compaction

Deployed gateways can accumulate millions of outbox rows during long-running deployments and outage backfills. Compaction runs once per day and deletes terminal rows older than the configured retention.

- `SentRetentionDays` (default 1) controls deletion of sent records. Outboxes are
  delivery queues; canonical history belongs in the central database.
- `FailedRetentionDays` (default 30) controls deletion of failed records.
- Compaction only removes records that have been in their terminal state for longer than the retention window. Pending and retrying records are never compacted.
- Compaction does not resolve a backlog; it only reclaims disk space for already-delivered or already-failed data.
- Record-level history after compaction is limited to what is available in the cloud database or monitoring systems.

### Runtime-Configurable Settings

All gateways support runtime adjustment of outbox batch size and sweep interval
without restarting the container. This is useful during outage backfill or when
tuning forwarding throughput.

- **API endpoint** (API-key protected): `PUT /diagnostics/outbox/settings`
  - Body: `{"batchSize": 500, "sweepIntervalSeconds": 1}`
  - Body: `{"reset": true}` — reverts to configured defaults
- **Shared defaults**: `Outbox:BatchSize=50`, `Outbox:SweepIntervalSeconds=5`;
  mounted gateway examples/Compose overrides can select different values.
- The forwarder skips the sweep interval delay entirely when the queue has work,
  so backlog draining proceeds at maximum rate.
- **HTTP policy**: Current senders use the named `HvoEdge` client registered with
  `AddStandardResilienceHandler()` by
  [the shared runtime](../../src/HVO.Edge.Hosting/EdgeWebApplicationBuilderExtensions.cs).
  It does not set the older claimed 60/120-second overrides. Changing client
  timeout/resilience behavior needs reviewed code, not runtime outbox tuning.

## Cloud Batch Semantics

- Batch endpoints should be idempotent by `SourceId` or station/device identity plus `RecordedAtUtc`.
- Inserted and duplicate/skipped records both count as successfully delivered from the edge perspective.
- Per-record failures in an otherwise successful batch must include enough information to map the failure back to a local outbox row.
- If the batch response body is missing or unparsable, treat the whole batch as transient unless the HTTP status clearly indicates a permanent class.

### Health Treatment

- Current forwarding failures should degrade or fail health depending on severity and age.
- Historical `RetryExhausted` records should warn/degrade but must not block current telemetry if new records are forwarding successfully.
- Permanent dead letters should be visible with counts by `FailureKind` and recent examples, but should not block current forwarding.
- Pending count should be interpreted with sample age and last success time. A growing pending queue with recent failures is more severe than a draining backlog.

## Common Telemetry Standard

Gateways should emit a common set of metrics and traces with consistent names and tags, plus domain-specific metrics for each protocol.

### Required Tags

Use these tags consistently across metrics and traces when available.

| Tag | Purpose |
|-----|---------|
| `hvo.gateway.id` | Stable gateway identifier; normally a resource attribute. |
| `hvo.gateway.type` | Gateway implementation family; normally a resource attribute. |
| `hvo.site.id` | Observatory/site identity; normally a resource attribute. |
| `hvo.source.id` | Configured source identity used for payload attribution. |
| `hvo.device.id` | Stable configured device identity for multi-device gateways. |
| `hvo.device.type` | Bounded device/model family. |
| `hvo.result` | Bounded `success`, `failure`, `degraded`, `skipped`, or `unknown`. |
| `hvo.payload.type` | Bounded outbox payload category. |
| `hvo.failure.kind` | Bounded failure classification; never an exception message. |
| `hvo.health.state` | Bounded gateway health state. |

Hostnames, IPs, serial paths, raw frames, MQTT topics, record IDs, exception messages, timestamps, and battery/weather/power measurements must not be metric tags. Runtime host identity belongs in resource attributes, while physical measurements belong in typed payloads and SQL.

### Common Metrics

Metric names are defined in `GatewayTelemetryConventions`. Prefer counters for event totals, histograms for latency, and observable gauges for current state.

| Metric | Type | Unit | Purpose |
|--------|------|------|---------|
| `gateway.device.connect.attempt` | Counter | `{attempt}` | Device connection attempts. |
| `gateway.device.connect.failure` | Counter | `{failure}` | Failed connection attempts. |
| `gateway.device.reconnect` | Counter | `{reconnect}` | Reconnection attempts. |
| `gateway.device.connect.duration` | Histogram | `s` | Connection duration. |
| `gateway.device.read.attempt` | Counter | `{attempt}` | Protocol read attempts. |
| `gateway.device.read.failure` | Counter | `{failure}` | Failed reads. |
| `gateway.device.read.duration` | Histogram | `s` | Read duration. |
| `gateway.device.poll.attempt` | Counter | `{attempt}` | Poll/sample attempts. |
| `gateway.device.poll.failure` | Counter | `{failure}` | Failed or classified skipped polls. |
| `gateway.device.poll.duration` | Histogram | `s` | Poll duration. |
| `gateway.device.freshness.seconds` | Gauge | `s` | Age of the latest successful sample. |
| `gateway.outbox.depth` | Gauge | `{record}` | Pending outbox records. |
| `gateway.outbox.failed` | Gauge | `{record}` | Failed outbox records. |
| `gateway.outbox.forward.success` | Counter | `{record}` | Records accepted or skipped as duplicates by cloud ingest. |
| `gateway.outbox.forward.failure` | Counter | `{record}` | Records that failed a forward attempt. |
| `gateway.outbox.forward.duration` | Histogram | `s` | Forward request duration. |
| `gateway.health.evaluation` | Counter | `{evaluation}` | Health evaluations by state. |
| `gateway.health.evaluation.duration` | Histogram | `s` | Health evaluation duration. |

The common meter and activity source are both `HVO.Edge`. Existing active-collector compatibility aliases remain temporary. Remove them only after externally stored Grafana dashboards have been exported, checked for those names, and migrated.

### Common Traces/Operations

| Operation | Purpose |
|-----------|---------|
| `gateway.device.connect` | Local device/protocol connect. |
| `gateway.device.poll` | One poll/sample batch. |
| `gateway.device.read` | Local device/protocol read. |
| `gateway.outbox.enqueue` | Local outbox enqueue. Usually sampled or debug-level. |
| `gateway.outbox.sweep` | Outbox batch sweep. |
| `gateway.outbox.forward` | Cloud forwarding request. |
| `gateway.outbox.retry` | Records scheduled for retry after transient failure. |
| `gateway.outbox.dead_letter` | Records moved to failed for permanent reasons. |
| `gateway.outbox.requeue` | Retry-exhausted records returned to pending after recovery. |
| `gateway.health.evaluate` | Gateway health/status evaluation. |

Gateway-specific operations should use a stable prefix such as `Davis.Console.*`, `Eg4.Hid.*`, or `JkBms.Ble.*`.

## Gateway-Specific Telemetry Extensions

Gateway-specific metrics should not duplicate common metrics. They should expose protocol details needed to diagnose that gateway.

| Gateway | Examples |
|---------|----------|
| Davis Vantage Pro2 | console wake failures, ACK/CRC failures, LOOP packet counts, archive catchup records/pages, WeatherLink/IP reconnects. |
| EG4 | USB HID/serial connection state, identity rejection, PI30/Modbus read failures, tracker freshness. |
| JK BMS | BLE connection failures, adapter lock contention, frame decode failures, per-device poll success, alarm state changes. |
| Victron SmartShunt | BLE read failures, protocol decode failures, write/sync confidence, stale value age. |

## Common Health/Status Contract

Each gateway should expose local status suitable for browser diagnostics and optional cloud gateway-status payloads.

Required status concepts:

- gateway identity and version.
- startup/configuration validity.
- local device connection state.
- latest successful sample timestamp and age.
- consecutive local read failures.
- outbox pending count.
- outbox failed count by failure kind where practical.
- last forward success timestamp.
- last forward error and failure kind.
- last batch count.
- whether historical failures are present but current forwarding is healthy.

The current shared health states are `Unknown`, `Healthy`, `Warning` and `Critical`.
Reasons/device snapshots supply context such as startup, stale acquisition,
configuration or forwarding failure. See the
[endpoint/probe/auth table](../GATEWAY_OPERATIONS.md#health-contract) for actual
HTTP mappings; `Warning` does not itself produce 503. Older prose using
`Degraded` does not name a current shared enum value.

## Shared Code Direction

Target shared components:

- `HVO.Edge.Outbox`: common record model, status, failure kind, store, retry policy, dead-letter/requeue helpers, compaction.
- `HVO.Edge.Contracts`: gateway status payloads, diagnostics responses, health states, telemetry names/tags, API-key matcher, and domain payload envelope types.

Current implementation status:

- EG4 uses the shared outbox for typed read-only power and device-detail observations.
- SmartShunt uses the shared outbox for battery monitor observations.
- JK BMS uses one canonical shared-outbox record per poll, with changed config/device-info embedded in the BMS reading contract and strict per-record outcome accounting.
- Davis uses the shared outbox for weather raw/archive telemetry, with station settings/info split into gateway-owned local persistence.

Home Assistant, rather than a direct HVO gateway, owns Kasa and Govee acquisition and presentation. The HA exporter is implemented but intentionally disabled in production with no mappings or source claims. The retired direct SolarAssistant and TP-Link/Kasa applications are not covered as current gateways by this standard.

Open future work is tracked in `docs/FUTURE_WORK.md`.

## Implementation Rules For New Gateways

- Use the headless `AddHvoEdgeRuntime` composition and
  `MapHvoEdgeRuntimeEndpoints`; do not add Razor, Blazor, MudBlazor, Themes, or
  static assets.
- Mount non-secret configuration read-only at `/app/config/gateway.json`, data
  read-write at `/app/data`, and secret files read-only under `/run/secrets`.
- Register device workers after the shared runtime so validation and SQLite
  initialization complete first.
- Start with the shared outbox unless there is a documented blocker.
- Use common status/failure/health semantics even when a gateway needs custom payloads.
- Add gateway-specific metrics only after mapping common metrics first.
- Keep commands and writes out of the outbox.
- Do not add cloud commands or bidirectional behavior without a separate safety design.
- Document any deviation from this standard in the gateway's `hvo-implementation.md`.
- Add tests for retryable failures, dead-letter failures, requeue behavior, and health classification before deployment.

## Remaining Decisions

- Whether requeue should be exposed through a local admin endpoint, CLI/tooling, or manual SQLite operation only.
- How much gateway status should be sent to cloud versus kept local-only.
- Whether some gateway classes need retention values different from the current shared defaults.
