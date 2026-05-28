# Digital Loggers Gateway Manual

## Status

- Phase 0 status: placeholder.
- Last updated: 2026-05-28
- Confidence: low; exact model/API docs needed.

## Identity

This table is HVO documentation metadata. It is not a list of Digital Loggers API fields.

| Field | Value |
|-------|-------|
| HVO manual subject | Digital Loggers power controller / PDU |
| HVO integration role | Source/controller |
| Hardware model | Needs confirmation |
| HVO project/service | None |
| Native UI exists | Web UI likely |
| Native UI is primary | Yes until HVO command safety is designed |
| HVO UI responsibility | Level 2: operational status and carefully gated commands if approved |
| HVO safety classification | High-risk command if switching observatory power |

## Research Targets

| Topic | What to find | Status |
|-------|--------------|--------|
| Exact model | Web Power Switch, PDU, Pro Switch, etc. | Needed |
| Official API docs | HTTP endpoints, auth, response formats | Needed |
| Outlet capabilities | Per-outlet state, labels, current/power telemetry | Needed |
| Command semantics | On/off/cycle/pulse, idempotency, timing | Needed |
| Authentication | Basic auth, digest, token, HTTPS support | Needed |
| Safety constraints | Which outlets control critical equipment | Needed |

## Expected Capability Areas

| Capability group | Read-only | Read-write | Command/action | Local UI | Outbox/cloud | Notes |
|------------------|-----------|------------|----------------|----------|--------------|-------|
| Outlet state | Yes | Yes | On/off/cycle likely | Yes | Status/event candidate | Exact API TBD. |
| Outlet names/config | Yes | Maybe | Rename maybe | Yes | Inventory/config | TBD. |
| Current/power telemetry | Model dependent | No | No | Yes if available | Power/status candidate | Units TBD. |
| Device health | Yes | No | No | Yes | Gateway status | Ping/API health. |

## Design Notes

- Treat all power-switching commands as local-only until explicit safety, confirmation, audit, and interlock requirements exist.
- Prefer representing outlet state as current property and on/off/cycle as command operations.
- Main site should probably show state/health first, not expose commands.

## Open Questions

| Question | Why it matters | Status |
|----------|----------------|--------|
| What exact Digital Loggers model is installed/planned? | API differs by model | Open |
| Which outlets are safety-critical? | Command gating | Open |
| Does the device support HTTPS or only HTTP? | Credential exposure risk | Open |
