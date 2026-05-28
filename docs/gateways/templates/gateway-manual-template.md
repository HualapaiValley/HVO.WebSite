# Gateway Documentation Set Template

Use this template for every gateway, device, native system, or adjacent integration. Complex gateways should use a folder with the five files below. Small placeholder systems can start as one file, but should split before local APIs, outbox payloads, central storage, or UI contracts are locked.

## Required Split For Complex Gateways

```text
docs/gateways/<gateway-name>/
  README.md
  manufacturer-protocol.md
  hvo-implementation.md
  hvo-api-contracts.md
  validation-notes.md
```

## Cross-Cutting Provenance Rules

- Do not invent device/API fields, enum values, commands, or response structures.
- Keep manufacturer/vendor protocol facts separate from HVO design choices.
- Every vendor field, command, operation, enum, and data structure must come from an official document, source repository, live capture, or existing implementation code.
- If a name is HVO-created for UI, storage, source identity, normalized units, or documentation organization, label it as `HVO metadata`, `HVO normalized field`, or `HVO local concept`.
- If a value is inferred but not verified, mark it `Needs validation` and include the evidence.
- Keep vendor/API names separate from HVO aliases and normalized names.

---

# README.md Template

## Status

- Phase 0 status:
- Last updated:
- Confidence:
- Primary owner:

## Document Map

| Document | Purpose | Audience |
|----------|---------|----------|
| [manufacturer-protocol.md](manufacturer-protocol.md) | Vendor-defined protocol/API. | Anyone implementing a compatible driver/integration. |
| [hvo-implementation.md](hvo-implementation.md) | HVO code structure, classes, public methods, examples, and design decisions. | HVO developers. |
| [hvo-api-contracts.md](hvo-api-contracts.md) | Local APIs, outbox payloads, central ingest/storage mappings, aliases, and cloud treatment. | HVO API/storage/UI developers. |
| [validation-notes.md](validation-notes.md) | Evidence, live test steps, open questions, and unresolved behavior. | HVO developers validating hardware behavior. |

## References

| Type | Reference | Status | Notes |
|------|-----------|--------|-------|
| Official docs | | Needed/Found/Validated | |
| Protocol docs | | Needed/Found/Validated | |
| Source repo | | Needed/Found/Validated | |
| Library | | Needed/Found/Validated | |
| Live capture | | Needed/Found/Validated | |
| Existing HVO code | | Needed/Found/Validated | |

## Identity

This table is HVO documentation metadata unless a row explicitly says it comes from the device/API. Do not treat these rows as vendor/API fields.

| Field | Value |
|-------|-------|
| HVO manual subject | |
| HVO integration role | Source / Consumer / Both / Proxy / Standalone / Safety controller |
| Hardware model | |
| Firmware/software version | |
| HVO project/service | |
| HVO deployment target | |
| Native UI exists | Yes/No |
| Native UI is primary | Yes/No |
| HVO UI responsibility | Level 0/1/2/3 |
| HVO safety classification | Telemetry-only / Low-risk command / High-risk command / Safety-critical |

## Capabilities Summary

| Capability group | Read-only | Read-write | Command/action | Local UI | Outbox/cloud | Notes |
|------------------|-----------|------------|----------------|----------|--------------|-------|

---

# manufacturer-protocol.md Template

This document describes manufacturer/vendor-defined behavior only. Do not put HVO implementation choices here except as source/provenance notes.

## Provenance

| Source | Use |
|--------|-----|

## Known Hardware / Firmware Variants

| Variant | Differences | Detection method | Impact |
|---------|-------------|------------------|--------|

## Communication Summary

| Field | Value |
|-------|-------|
| Transport | TCP / BLE / REST / MQTT / WebSocket / HTTP / File / Webhook / Cloud |
| Protocol | |
| Default endpoint/port | |
| Authentication | |
| Authorization model | |
| Polling/subscription model | |
| Rate limits/timing | |
| Reconnect behavior | |
| Time source | |

## Data Types And Encodings

| Type/encoding | Description | Null/not-available | Notes |
|---------------|-------------|--------------------|-------|

## Unit Handling

Use this section when a device has configurable display units, protocol-native units, raw counts, bucket sizes, scale factors, or locale-dependent presentation.

| Measurement area | Device/protocol raw unit | Device display/config unit | Does config affect raw values? | Validation status | Notes |
|------------------|--------------------------|----------------------------|-----------------------------|-------------------|-------|

## Vendor Fields

| Vendor/API name | Type | Protocol/raw unit | Source operation | Access | Semantics | Possible values/range | Timestamp | Notes |
|-----------------|------|-------------------|------------------|--------|-----------|-----------------------|-----------|-------|

Access values:

- `Read-only`
- `Read-write`
- `Command`
- `Derived`
- `Application-only`
- `Local-only`

Semantics values:

- `Instantaneous`
- `Cumulative counter`
- `Interval total`
- `Configuration`
- `Metadata`
- `Alarm`
- `Event`
- `Health`

## API Calls / Protocol Operations

| Operation | Direction | Request/framing | Response/framing | Auth | Side effects | Timeout/retry | Notes |
|-----------|-----------|-----------------|------------------|------|--------------|---------------|-------|

## Command Coverage Table

Use this as the checklist for claiming protocol coverage. Include every official command/API operation, even when HVO intentionally does not support it.

| Command/API operation | Vendor purpose | Access/safety | HVO support | HVO method/class | Unit test | Simulator test | Live test | Notes |
|-----------------------|----------------|---------------|-------------|------------------|-----------|----------------|-----------|-------|

## Commands And Writable Settings

| Command/setting | Parameters | Read-back path | Side effects | Safety risk | Confirmation required | Notes |
|-----------------|------------|----------------|--------------|-------------|-----------------------|-------|

## Events / Notifications / Webhooks

| Event | Trigger | Payload | Delivery semantics | Idempotency key | Notes |
|-------|---------|---------|--------------------|-----------------|-------|

## Manufacturer Quirks

| Issue | Evidence | Impact | Workaround | Validation needed |
|-------|----------|--------|------------|-------------------|

---

# hvo-implementation.md Template

This document describes HVO's code and design. It should include what classes/interfaces HVO created, how they are used, public methods, and small examples.

## Design Summary

## Implemented Capabilities

| Capability | HVO status | Notes |
|------------|------------|-------|

## Project Layout

| Path | Purpose |
|------|---------|

## Main Classes And Interfaces

| Class/interface | Responsibility | Used by |
|-----------------|----------------|---------|

## Public Methods And Samples

### `<ClassOrInterface>.<Method>`

Purpose:

Side effects:

Validation/safety:

```csharp
// Minimal example call.
```

## Data Models

| Model | Purpose | Notes |
|-------|---------|-------|

## Worker Flow

1.

## Error Handling And Retries

| Scenario | HVO behavior | Notes |
|----------|--------------|-------|

## Safety Decisions

| Decision | Reason | Notes |
|----------|--------|-------|

## Implementation Decision Log

| Decision | Status | Rationale | Consequences / follow-up |
|----------|--------|-----------|--------------------------|

## Implementation Caveats And Debt

| Caveat | Impact | Follow-up |
|--------|--------|-----------|

## Implementation Readiness Assessment

| Area | Current HVO status | Readiness | Next action |
|------|--------------------|-----------|-------------|

---

# hvo-api-contracts.md Template

This document describes HVO local APIs, outbox payloads, central ingest/storage mappings, aliases, idempotency, versioning, and cloud treatment.

## HVO Normalization And Aliases

| Vendor/API source name | HVO local name | HVO normalized unit | Conversion | Notes |
|------------------------|----------------|---------------------|------------|-------|

## Local Configuration

| Setting | Type | Required | Secret | Runtime editable | Default | Notes |
|---------|------|----------|--------|------------------|---------|-------|

## Local APIs

| Endpoint | Purpose | Auth | Request | Response | Notes |
|----------|---------|------|---------|----------|-------|

## Outbox Payloads

| Payload/stream | Field | Source | Unit/shape | Required | Notes |
|----------------|-------|--------|------------|----------|-------|

## Central Ingest / Storage Mapping

| Outbox field | Central field/table | Conversion | Status | Notes |
|--------------|---------------------|------------|--------|-------|

## Candidate Streams

| Stream | Payload type | Cadence | Idempotency key | Retention | Cloud treatment | Notes |
|--------|--------------|---------|-----------------|-----------|-----------------|-------|

## Main Site Candidate UI

| UI area | Data needed | Source stream/API | Notes |
|---------|-------------|-------------------|-------|

---

# validation-notes.md Template

This document tracks evidence, live validation, packet captures, open questions, and unresolved behavior.

## Confirmed So Far

| Area | Evidence | Confidence |
|------|----------|------------|

## Live Validation Checklist

| Validation | Why it matters | Status |
|------------|----------------|--------|

## Test And Simulation Plan

| Test type | Scenario | Tooling/fake | Notes |
|-----------|----------|--------------|-------|

## Test Strategy

| Level | Purpose | Tooling | Should cover | Limitations |
|-------|---------|---------|--------------|-------------|

## Test Coverage Matrix

| Area | Unit | Mocked | Simulator | Live | Priority | Notes |
|------|------|--------|-----------|------|----------|-------|

## Protocol And Implementation Gap Register

| Gap | Type | Risk | Proposed resolution | Status |
|-----|------|------|---------------------|--------|

## Definition Of Done

| Criterion | Required for full implementation? | Current status | Notes |
|-----------|-----------------------------------|----------------|-------|

## Known Issues And Quirks

| Issue | Evidence | Impact | Workaround | Validation needed |
|-------|----------|--------|------------|-------------------|

## Open Questions

| Question | Why it matters | Owner/source | Status |
|----------|----------------|--------------|--------|
