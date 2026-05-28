# Motion Sensors TBD Gateway Manual

## Status

- Phase 0 status: purchase/integration criteria placeholder.
- Last updated: 2026-05-28
- Confidence: low; brand/model not selected.

## Identity

This table is HVO documentation metadata. It is not a list of motion-sensor API fields.

| Field | Value |
|-------|-------|
| HVO manual subject | Motion sensors TBD |
| HVO integration role | Event source |
| Hardware model | Not selected |
| HVO project/service | None |
| Native UI exists | Depends on vendor/ecosystem |
| HVO UI responsibility | Level 1: events, notifications, health |
| HVO safety classification | Telemetry/event unless paired with commands |

## Selection Criteria

| Criterion | Preference | Why |
|-----------|------------|-----|
| Local API/event support | Strongly preferred | Avoid cloud dependency and latency. |
| Webhook/MQTT/event push | Preferred | Better than polling for motion. |
| Battery level | Preferred for wireless sensors | Maintenance visibility. |
| Signal/RSSI/link quality | Preferred | Diagnose missed events. |
| Timestamp quality | Required | Event ordering and notification correctness. |
| Cloud-only dependency | Avoid if possible | Reliability/security. |
| Library ecosystem | Prefer stable local libraries | Reduces reverse-engineering burden. |

## Expected Capability Areas

| Capability group | Read-only | Read-write | Command/action | Local UI | Outbox/cloud | Notes |
|------------------|-----------|------------|----------------|----------|--------------|-------|
| Motion event | Yes | No | No | Notifications | Event stream | Primary capability. |
| Tamper event | Maybe | No | No | Notifications | Event stream | Model dependent. |
| Battery | Maybe | No | No | Status | Gateway/device status | Model dependent. |
| Signal/health | Maybe | No | No | Status | Local-only/status | Model dependent. |

## Design Notes

- Treat motion as an event stream, not a periodic measurement, unless the hardware only exposes current occupancy state.
- Design idempotency around event timestamp + sensor id + event type.
- Main site should show recent events/notifications, not raw high-volume event spam.

## Open Questions

| Question | Why it matters | Status |
|----------|----------------|--------|
| Which sensor ecosystem will be purchased? | Protocol and reliability | Open |
| Should motion events integrate with Blue Iris events? | Avoid duplicate notifications | Open |
| Are sensors indoor, outdoor, wired, wireless? | Power/range/weatherproofing | Open |
