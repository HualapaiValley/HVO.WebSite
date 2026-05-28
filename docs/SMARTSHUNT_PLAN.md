# SmartShunt Plan

Last updated: 2026-05-25

This document is the current SmartShunt handoff/reference. It replaces the earlier exploratory plan with the validated collector state, the fields we trust, and the remaining gaps.

## Current Status

- Device: `SmartShunt LiFePo4`
- BLE address: `E2:21:F0:89:A7:C0`
- Working Bluetooth adapter on `devPi5`: `hci0`
- Local SmartShunt gateway: `http://localhost:5400/status`
- Deploy path on `devPi5`: `/home/roys/src/HVO.WebSite/`

Current practical state:

- Public paired GATT `6597...` is the production baseline for live telemetry.
- Private `306b...` is useful optional enrichment for product/history/internal state.
- The gateway is deployable and forwarding successfully on `devPi5`.
- SmartShunt BLE write/sync work is deferred until a real VictronConnect capture exists.

## Validated Public Telemetry

These fields are production-usable and already flow through the gateway:

| Field | Meaning | Notes |
|------|---------|-------|
| `soc` | State of charge | Sometimes invalid on the public path; private SoC overlay is used when needed |
| `voltage` | Battery voltage | Stable |
| `current` | Battery current | Stable when present |
| `power` | Battery power | Stable |
| `consumed_ah` | Consumed amp-hours | Stable |
| `starter_voltage` | Starter voltage | Routed through; often `n/a` on this device/session |
| `temperature` | Temperature-like public field | Routed through; often `n/a` |
| `remaining_time` | Remaining time / time to go | Routed through; often `n/a` |

## Validated Private Enrichment

### Product / identity metadata

| Private Field | Current Interpretation | Current Value / Status |
|------|------------------------|------------------------|
| `product 0x02` | Firmware version | `v4.25` |
| `product 0x0a` | Serial number | `HQ211369CMY` |
| `product 0x00` | Raw product / family metadata | currently `0089a3fe` in the explicit request path |
| `product 0x09` | Raw device-id-like metadata | `3aaa47ca2438e600` |
| `product 0x50` | Stable undocumented metadata | `497` |

### History / statistics

| Private Field | Meaning |
|------|---------|
| `history 0x00` | Deepest discharge |
| `history 0x01` | Last discharge |
| `history 0x02` | Average discharge |
| `history 0x03` | Total charge cycles |
| `history 0x04` | Full discharges |
| `history 0x05` | Cumulative Ah drawn |
| `history 0x06` | Min battery voltage |
| `history 0x07` | Max battery voltage |
| `history 0x08` | Time since last full |
| `history 0x09` | Synchronizations; sometimes absent |
| `history 0x0a` | Low voltage alarms |
| `history 0x0b` | High voltage alarms |
| `history 0x0e` | Min starter voltage |
| `history 0x0f` | Max starter voltage |
| `history 0x10` | Discharged energy |
| `history 0x11` | Charged energy |

### Private internal/runtime fields

These are useful for local visibility but are not treated as primary telemetry:

| Private Field | Current Interpretation |
|------|------------------------|
| `latest 0x8f` | `CurrentCoarseA`; coarse current representation |
| `streaming 0x87` | `ChargeStatusCoarsePercent`; rounded/coarse SoC |
| `streaming 0x5a` | `StreamingCounter`; runtime/session counter |
| `streaming 0x0e` | session/state flag |
| `streaming 0x0f` | session/state flag |

## Gateway Behavior

- Public telemetry is the primary live source.
- Private `306b...` data is merged as optional enrichment.
- Public `soc == 0` is treated as suspicious when voltage/current show a live battery.
- Private SoC is used as an overlay only when the overlay is fresh enough or public SoC looks clearly invalid.
- The local UI can show `CurrentCoarseA` as `approx` when public current is missing.
- Forwarded payloads remain public-first and do not use coarse-current fallback.

## Deploy / Runtime Notes

- Current SmartShunt container on `devPi5`: `smartshunt-hvo-smartshunt-1`
- Compose file: `deploy/pi-gateways/smartshunt/docker-compose.yml`
- Preserve the remote `deploy/pi-gateways/smartshunt/.env` during sync/deploy work.
- Use `roys@devPi5`, not the default `ssh devPi5` alias.
- SmartShunt and JK should not be treated as a stable mixed BLE workload on the same `hci0`.

## Known Limitations

- No verified safe BLE write/sync path exists yet.
- Do not treat VE.Direct register knowledge as proof of BLE write framing.
- Battery settings and alarm-threshold configuration pages are still not reachable as stable private reads.
- Remaining time is often unavailable on this installation/session.
- Shared-adapter `hci0` contention is still real when SmartShunt and JK are both active.
- Extra USB Bluetooth adapters tested on `devPi5` have not been usable; continue assuming `hci0` only.

## Key Decisions

- Keep public `6597...` as the production baseline.
- Keep private `306b...` as optional enrichment only.
- Keep SmartShunt BLE work read-only until a real VictronConnect capture exists.
- Treat the SmartShunt's low SoC relative to the JK fleet as a device-state/synchronization issue, not a collector decode issue.
- Prefer minimal, evidence-backed additions over speculative private request expansion.

## Verification State

- SmartShunt gateway builds locally.
- SmartShunt tests currently pass: `19/19`
- Live forwarding to the website power API has been validated on `devPi5`.
- Local SmartShunt status has been repeatedly validated against live device behavior and screenshots.

## If Work Resumes Later

Highest-value next steps:

1. Leave the current read-only surface stable unless a new field has strong live evidence.
2. If sync/settings work resumes, get a real VictronConnect Bluetooth capture first.
3. Only revisit split-adapter BLE work once a Linux-proven second Bluetooth adapter is available.
