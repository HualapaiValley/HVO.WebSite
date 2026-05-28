# TP-Link / Kasa Gateway Manual

## Status

- Phase 0 status: placeholder.
- Last updated: 2026-05-28
- Confidence: low; exact hardware is not selected or confirmed in repo.

## Identity

This table is HVO documentation metadata. It is not a list of TP-Link/Kasa API fields.

| Field | Value |
|-------|-------|
| HVO manual subject | TP-Link / Kasa devices |
| HVO integration role | Source/controller TBD |
| Hardware model | Needs selection/confirmation |
| HVO project/service | None |
| Native UI exists | Kasa/Tapo app |
| Native UI is primary | TBD |
| HVO UI responsibility | TBD; likely Level 1-2 for selected devices |
| HVO safety classification | Low-risk to high-risk depending on controlled loads |

## Research Targets

| Topic | What to find | Status |
|-------|--------------|--------|
| Exact model numbers | Plug, switch, dimmer, power meter, etc. | Needed |
| Protocol family | Kasa local LAN, Tapo cloud/local, Matter, or other | Needed |
| Authentication | Local token, cloud auth, pairing, encryption | Needed |
| Libraries | .NET or stable command-line/library support | Needed |
| Power telemetry | Whether model exposes voltage/current/power/energy | Needed |
| Command safety | What can be switched and what interlocks are needed | Needed |

## Expected Capability Areas

| Capability group | Read-only | Read-write | Command/action | Local UI | Outbox/cloud | Notes |
|------------------|-----------|------------|----------------|----------|--------------|-------|
| Outlet/switch state | Likely | Yes | On/off | Yes | Candidate event/status | Command safety depends on load. |
| Power telemetry | Model dependent | No | No | Yes if available | Candidate | Units/reset behavior TBD. |
| Energy counters | Model dependent | No | No | Yes if available | Candidate | Counter reset semantics needed. |
| Device metadata | Likely | No | No | Yes | Inventory | Model/firmware/MAC. |

## Design Notes

- Separate current state (`on/off`) from command actions (`turn on`, `turn off`, `toggle`).
- Do not allow cloud commands until device load, safety class, auth, and audit requirements are documented.
- Some TP-Link ecosystems have changed local protocol/auth over time; exact model and firmware matter.

## Open Questions

| Question | Why it matters | Status |
|----------|----------------|--------|
| Which TP-Link/Kasa/Tapo models are planned? | Protocol and capabilities differ | Open |
| Is local-only control available and stable? | Avoids cloud dependency | Open |
| Which loads are connected to these devices? | Safety classification | Open |
