# Roof / Dome System Gateway Manual

## Status

- Phase 0 status: placeholder.
- Last updated: 2026-05-28
- Confidence: low until the provided API documentation is added/reviewed.

## Identity

This table is HVO documentation metadata unless a row explicitly references the roof/dome API. It is not a list of roof/dome API fields.

| Field | Value |
|-------|-------|
| HVO manual subject | Observatory roof/dome system |
| HVO integration role | Safety controller/source |
| Hardware/software model | Existing external system; details needed |
| HVO project/service | None |
| Native UI exists | Own system/API exists |
| Native UI is primary | Yes until HVO safety design is complete |
| HVO UI responsibility | Level 2-3 depending on command scope; status first |
| HVO safety classification | Safety-critical |

## Research Targets

| Topic | What to find | Status |
|-------|--------------|--------|
| API documentation | Status endpoints, command endpoints, auth | Needed from owner |
| State model | Open/closed/opening/closing/fault/manual/auto/etc. | Needed |
| Interlocks | Weather, telescope position, physical sensors, manual override | Needed |
| Command semantics | Open, close, stop, park, reset, acknowledge fault | Needed |
| Failure modes | Lost connection, partial movement, sensor disagreement | Needed |
| Audit requirements | Who commanded what, when, and from where | Needed |

## Expected Capability Areas

| Capability group | Read-only | Read-write | Command/action | Local UI | Outbox/cloud | Notes |
|------------------|-----------|------------|----------------|----------|--------------|-------|
| Roof/dome state | Yes | No | No | Yes | Status/event | First priority. |
| Motion state | Yes | No | No | Yes | Status/event | Opening/closing/stopped. |
| Faults/alarms | Yes | Maybe ack | Ack/reset maybe | Yes | Event/status | Safety critical. |
| Interlock state | Yes | No | No | Yes | Status | Weather-safe, telescope-safe, manual override. |
| Commands | Maybe | Yes | Open/close/stop/etc. | Local only initially | No cloud initially | Requires safety design. |

## Safety Rules Draft

- Status-only integration comes first.
- No cloud commands until explicitly approved.
- Local commands, if added, require authentication, confirmation, interlock display, and audit log.
- Weather state, telescope/park state, and manual override state must be visible before any automated command is considered.
- Fail-safe behavior must be documented from the vendor/system API, not inferred.

## Open Questions

| Question | Why it matters | Status |
|----------|----------------|--------|
| What is the API status response schema? | Local status model | Open |
| What commands exist, and are they idempotent? | Safety/command design | Open |
| What interlocks are enforced by the roof system itself? | Avoid unsafe assumptions | Open |
| Should HVO ever issue commands, or only monitor? | Scope | Open |
