# Common Gateway Standards

This document defines shared HVO gateway behavior that should be common across Davis, SolarAssistant, JK BMS, SmartShunt, and future gateways. Gateway-specific drivers may extend these standards, but should not redefine common lifecycle, outbox, telemetry, or health semantics without documenting why.

Status: Draft. Use this as the baseline before implementing the next gateway and as the target for refactoring older gateway-specific implementations.

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
| `TransientExhausted` | No, but manually requeueable | A retryable cloud/network/configuration failure exceeded the configured retry limit. |
| `RemoteValidation` | No | Cloud ingest rejected the specific record as permanently invalid. |
| `InvalidLocalPayload` | No | Gateway produced malformed or unparseable payload JSON. |
| `Unauthorized` | No until configuration changes | Cloud rejected authentication/authorization. Usually indicates bad API key or permissions. |
| `ConfigurationError` | No until configuration changes | Gateway cannot forward because required local configuration is missing or placeholder. |
| `UnsupportedPayloadVersion` | No until software/configuration changes | Cloud does not support the payload version. |

Current Davis code uses `ApiValidation` and `InvalidPayload`; these should be renamed to the common names when Davis moves to the shared outbox implementation.

### Retry Rules

- Only `Pending` records are selected by the normal sweeper.
- Transient HTTP failures, timeouts, DNS failures, and temporary cloud outages keep records `Pending` with exponential backoff until `MaxRetryAttempts` is reached.
- When retry attempts are exhausted, set `Status=Failed`, `FailureKind=TransientExhausted`, and preserve a useful `LastError`.
- Remote validation failures from the cloud are dead letters. Set `Status=Failed`, `FailureKind=RemoteValidation`, and store the cloud-provided reason.
- Locally invalid payload JSON is a dead letter. Set `Status=Failed`, `FailureKind=InvalidLocalPayload`, and avoid sending the batch until malformed records are removed from it.
- Authentication/configuration failures should not burn through thousands of retries silently. Prefer classifying them as `Unauthorized` or `ConfigurationError` and surfacing a critical health alert.
- Requeue operations must be explicit and should target only retryable classifications unless an operator intentionally overrides dead-letter handling.

### Requeue Rules

- Safe automatic requeue: historical unclassified failed rows may be requeued once during a migration when evidence suggests they were caused by old transient cloud outages.
- Safe operator requeue: `TransientExhausted` rows may be moved back to `Pending` after the cloud endpoint or network is healthy.
- Unsafe automatic requeue: `RemoteValidation`, `InvalidLocalPayload`, `Unauthorized`, `ConfigurationError`, and `UnsupportedPayloadVersion` should not automatically return to `Pending` without a code/configuration fix and operator decision.
- Requeue should reset `FailureKind=None`, set `NextRetryAtUtc=DateTime.MinValue`, and append a bounded note to `LastError` explaining why the record was requeued.

### Cloud Batch Semantics

- Batch endpoints should be idempotent by `SourceId` or station/device identity plus `RecordedAtUtc`.
- Inserted and duplicate/skipped records both count as successfully delivered from the edge perspective.
- Per-record failures in an otherwise successful batch must include enough information to map the failure back to a local outbox row.
- If the batch response body is missing or unparsable, treat the whole batch as transient unless the HTTP status clearly indicates a permanent class.

### Health Treatment

- Current forwarding failures should degrade or fail health depending on severity and age.
- Historical `TransientExhausted` records should warn/degrade but must not block current telemetry if new records are forwarding successfully.
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

Metric names are draft names. Prefer counters for event totals, histograms for latency, and observable gauges for current state.

| Metric | Type | Unit | Purpose |
|--------|------|------|---------|
| `hvo.gateway.sample.count` | Counter | samples | Samples successfully read from the local device/protocol. |
| `hvo.gateway.sample.age_seconds` | Gauge | seconds | Age of the latest successful sample. |
| `hvo.gateway.sample.consecutive_failures` | Gauge | failures | Current consecutive sample/read failures. |
| `hvo.gateway.connection.reconnects` | Counter | reconnects | Reconnect attempts to local device/protocol. |
| `hvo.gateway.outbox.pending` | Gauge | records | Pending outbox records. |
| `hvo.gateway.outbox.failed` | Gauge | records | Failed outbox records, tagged by `failure.kind` where practical. |
| `hvo.gateway.outbox.forwarded` | Counter | records | Records accepted or skipped as duplicates by cloud ingest. |
| `hvo.gateway.outbox.retry_scheduled` | Counter | records | Records scheduled for retry after transient failure. |
| `hvo.gateway.outbox.dead_lettered` | Counter | records | Records moved to failed for permanent reasons, tagged by `failure.kind`. |
| `hvo.gateway.forward.latency_ms` | Histogram | ms | Cloud forwarding request latency. |
| `hvo.gateway.forward.request.count` | Counter | requests | Cloud forwarding attempts, tagged by status class/result. |
| `hvo.gateway.health.state` | Gauge | state | Numeric gateway health state if exported as a metric. |

### Common Traces/Operations

| Operation | Purpose |
|-----------|---------|
| `Gateway.Startup` | Startup, schema checks, configuration checks. |
| `Gateway.Device.Connect` | Local device/protocol connect. |
| `Gateway.Device.Poll` | One poll/sample batch. |
| `Gateway.Outbox.Enqueue` | Local outbox enqueue. Usually sampled or debug-level. |
| `Gateway.Outbox.Sweep` | Outbox batch sweep. |
| `Gateway.Outbox.Forward` | Cloud forwarding request. |

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
- `HVO.Edge.Telemetry`: common metric names, tags, gateway telemetry helper, outbox telemetry helper.
- `HVO.Edge.Contracts`: gateway status payloads, health states, and domain payload envelope types.

Migration sequence:

1. Add common `EdgeOutboxFailureKind` and requeue/dead-letter helpers to `HVO.Edge.Outbox`.
2. Align SolarAssistant and other gateways already using `HVO.Edge.Outbox` with the common failure classifications.
3. Move Davis from its gateway-local outbox to the shared implementation when weather batching and current local UI needs are supported.
4. Add shared telemetry helpers and migrate gateway-specific metrics to common names/tags without removing useful domain metrics.
5. Update gateway docs and deployment runbooks to reference this standard.

## Implementation Rules For New Gateways

- Start with the shared outbox unless there is a documented blocker.
- Use common status/failure/health semantics even when a gateway needs custom payloads.
- Add gateway-specific metrics only after mapping common metrics first.
- Keep commands and writes out of the outbox.
- Do not add cloud commands or bidirectional behavior without a separate safety design.
- Document any deviation from this standard in the gateway's `hvo-implementation.md`.
- Add tests for retryable failures, dead-letter failures, requeue behavior, and health classification before deployment.

## Open Decisions

- Exact final names for common failure kinds before changing shared enums.
- Whether `Unauthorized` and `ConfigurationError` should be `Failed` records, gateway health states only, or both.
- Whether outbox records need `FirstFailedAtUtc` or `LastFailureKindChangedAtUtc`.
- Whether requeue should be exposed through a local admin endpoint, CLI/tooling, or manual SQLite operation only.
- How much gateway status should be sent to cloud versus kept local-only.
- Retention/compaction policy for sent records and permanent dead letters per gateway class.
