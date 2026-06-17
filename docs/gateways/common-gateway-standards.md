# Common Gateway Standards

This document defines shared HVO gateway behavior that should be common across Davis, SolarAssistant, JK BMS, SmartShunt, and future gateways. Gateway-specific drivers may extend these standards, but should not redefine common lifecycle, outbox, telemetry, or health semantics without documenting why.

Status: Current standard. Use this as the baseline for current gateways and future gateway work.

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
| `GatewayId` | Yes | Stable HVO gateway identifier, for example `hvo-davis` or `solarassistant`. |
| `GatewayType` | Yes | Gateway implementation family, for example `davis-vantage-pro2`, `solarassistant`, `jk-bms`. |
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
- Gateways whose devices do not have a reliable local-time concept, such as JK BMS, SmartShunt, SolarAssistant, and live Kasa polling, should use a configured gateway display timezone for local UI labels while continuing to store and forward UTC.
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
| `Permanent` | No | Dead-lettered record caused by local invalid payload, remote validation rejection, authentication/authorization failure, unsupported endpoint, or unsupported payload version. Requires code/configuration/operator action. |
| `RetryExhausted` | Yes, automatically after recovery | A retryable cloud/network failure exceeded the configured retry limit. These rows may be moved back to `Pending` after a successful forward proves connectivity has recovered. |

### Retry Rules

- Only `Pending` records are selected by the normal sweeper.
- Transient HTTP failures, timeouts, DNS failures, and temporary cloud outages keep records `Pending` with exponential backoff until `MaxRetryAttempts` is reached.
- When retry attempts are exhausted, set `Status=Failed`, `FailureKind=RetryExhausted`, and preserve a useful `LastError`.
- Remote validation failures from the cloud are dead letters. Set `Status=Failed`, `FailureKind=Permanent`, and store the cloud-provided reason.
- Locally invalid payload JSON is a dead letter. Set `Status=Failed`, `FailureKind=Permanent`, and avoid sending the batch until malformed records are removed from it.
- Authentication/configuration failures should not burn through thousands of retries silently. Classify HTTP 400/401/403/404 as `Permanent` and surface the error.
- Requeue operations must only automatically target `RetryExhausted`; permanent dead letters require operator/code/configuration action.

### Requeue Rules

- Safe automatic requeue: `RetryExhausted` rows may be moved back to `Pending` after a successful forward proves the cloud endpoint or network is healthy.
- Safe operator requeue: `RetryExhausted` rows may also be moved back to `Pending` after operator review.
- Unsafe automatic requeue: `Permanent` rows should not automatically return to `Pending` without a code/configuration fix and operator decision.
- Requeue should reset `FailureKind=None`, set `NextRetryAtUtc=DateTime.MinValue`, and append a bounded note to `LastError` explaining why the record was requeued.

### Cloud Batch Semantics

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
| `gateway.id` | Stable gateway identifier. |
| `gateway.type` | Gateway implementation family. |
| `site.id` | Observatory/site identity. |
| `source.id` | Source identity used for payload attribution. |
| `device.id` | Per-device identity for multi-device gateways. |
| `payload.type` | Outbox payload category. |
| `payload.version` | Payload contract version. |
| `failure.kind` | Common failure classification. |
| `http.status_code` | HTTP status code for forwarding requests. |
| `operation.name` | Trace/span operation name. |

### Common Metrics

Metric names are defined in `GatewayTelemetryConventions`. Prefer counters for event totals, histograms for latency, and observable gauges for current state.

| Metric | Type | Unit | Purpose |
|--------|------|------|---------|
| `gateway.outbox.depth` | Gauge | records | Pending outbox records. |
| `gateway.outbox.failed` | Gauge | records | Failed outbox records, tagged by `hvo.failure.kind` where practical. |
| `gateway.outbox.forward.success` | Counter | records | Records accepted or skipped as duplicates by cloud ingest. |
| `gateway.outbox.forward.failure` | Counter | records | Records that failed a forward attempt. |
| `gateway.device.freshness.seconds` | Gauge | seconds | Age of the latest successful sample. |
| `gateway.device.poll.failure` | Counter/Gauge | failures | Device poll failures or skipped polls. |

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

Gateway-specific operations should use a stable prefix such as `Davis.Console.*`, `SolarAssistant.Mqtt.*`, or `JkBms.Ble.*`.

## Gateway-Specific Telemetry Extensions

Gateway-specific metrics should not duplicate common metrics. They should expose protocol details needed to diagnose that gateway.

| Gateway | Examples |
|---------|----------|
| Davis Vantage Pro2 | console wake failures, ACK/CRC failures, LOOP packet counts, archive catchup records/pages, WeatherLink/IP reconnects. |
| SolarAssistant | MQTT connection state, topic freshness, stale inventory/readings, inverter count, battery count. |
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

Health states should distinguish these cases:

- `Healthy`: sampling and forwarding are current.
- `Degraded`: sampling or forwarding has a warning condition, but current telemetry may still be flowing.
- `Offline`: local device/protocol is unavailable or sample age is beyond the critical threshold.
- `Misconfigured`: required configuration is missing, placeholder, or unauthorized.
- `Starting`: process is running but has not completed initialization.

## Shared Code Direction

Target shared components:

- `HVO.Edge.Outbox`: common record model, status, failure kind, store, retry policy, dead-letter/requeue helpers, compaction.
- `HVO.Edge.Contracts`: gateway status payloads, diagnostics responses, health states, telemetry names/tags, API-key matcher, and domain payload envelope types.

Current implementation status:

- SolarAssistant uses the shared outbox plus typed power, inventory, configuration, energy, inverter detail, and gateway-status streams.
- TPLink Kasa uses the shared outbox for energy and inventory payloads.
- SmartShunt uses the shared outbox for battery monitor power readings.
- JK BMS uses the shared outbox for BMS readings/config/device-info and per-record permanent-failure isolation.
- Davis uses the shared outbox for weather raw/archive telemetry, with station settings/info split into gateway-owned local persistence.

Open future work is tracked in `docs/FUTURE_WORK.md`.

## Implementation Rules For New Gateways

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
