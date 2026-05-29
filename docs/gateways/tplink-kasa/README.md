# TP-Link / Kasa Gateway

## Status

- Phase 0 status: research baseline plus sanitized read-only live discovery captured; implementation not started.
- Last updated: 2026-05-29
- Confidence: high that the observed HVO devices in the scanned subnets include legacy Kasa LAN responders on TCP `9999`; medium for complete production scope because connected loads and command safety are not confirmed.
- Primary owner: HVO

## Document Map

| Document | Purpose | Audience |
|----------|---------|----------|
| [manufacturer-protocol.md](manufacturer-protocol.md) | TP-Link/Kasa/Tapo protocol families, transports, commands, auth, and known variants. | Anyone implementing a compatible driver. |
| [hvo-implementation.md](hvo-implementation.md) | Proposed HVO implementation scope, code layout, safety decisions, and rollout plan. | HVO developers. |
| [hvo-api-contracts.md](hvo-api-contracts.md) | Planned local APIs, outbox payloads, identity, and cloud treatment. | HVO API/storage/UI developers. |
| [validation-notes.md](validation-notes.md) | Evidence, simulator strategy, live validation plan, and open questions. | HVO developers validating behavior. |

## References

| Type | Reference | Status | Notes |
|------|-----------|--------|-------|
| Community library/docs | `python-kasa` repository and docs | Found | Broadest current reference for Kasa/Tapo device families, auth, discovery, protocols, and supported device list. Not official TP-Link documentation. |
| Reverse-engineering reference | `softScheck/tplink-smartplug` | Found | Documents legacy Smart Home protocol on TCP port 9999 with XOR autokey JSON framing for HS100/HS110/KP115-class devices. |
| Command list | `softScheck/tplink-smartplug/tplink-smarthome-commands.txt` | Found | Community command inventory for HS100/HS110-era devices. |
| JavaScript library | `plasticrake/tplink-smarthome-api` | Found | Supports legacy Kasa Smart Home devices; explicitly does not support Tapo. |
| Simulator | `plasticrake/tplink-smarthome-simulator` | Found | Node-based simulator for legacy TP-Link Smart Home devices. Useful for comparison; HVO can also build an in-process fake. |
| Live read-only discovery | HVO scan of `192.168.1.0/24` and `192.168.2.0/24` on 2026-05-29 | Captured | Used only `system.get_sysinfo` and `emeter.get_realtime` over legacy TCP `9999`; no write/switch commands sent. Committed docs keep aggregate/sanitized model and field data only. |
| Official TP-Link docs | TP-Link/Kasa/Tapo local protocol docs | Needed | No official local API reference found during initial research. |
| Exact HVO hardware | Installed/planned model and firmware | Partially captured | Legacy responders observed for EP25, HS300, KP200, HS105, and KL130. Final production device list and connected loads still need operator confirmation. |

## Identity

This table is HVO documentation metadata unless a row explicitly says it comes from the device/API. Do not treat these rows as vendor/API fields.

| Field | Value |
|-------|-------|
| HVO manual subject | TP-Link/Kasa/Tapo smart plugs, strips, switches, bulbs, and adjacent devices |
| HVO integration role | Source/controller TBD; initial recommendation is source/status only |
| Hardware model | Live scan observed legacy EP25(US), HS300(US), KP200(US), HS105(US), and KL130(US) responders |
| Firmware/software version | Observed versions are listed in the live discovery summary below |
| HVO project/service | Proposed: `src/HVO.Gateway.TplinkKasa` |
| HVO deployment target | Proposed Pi gateway container, compose path TBD |
| Native UI exists | Yes, Kasa/Tapo app depending on device family |
| Native UI is primary | Yes for pairing, firmware, account/cloud, schedules, and full device management |
| HVO UI responsibility | Level 1-2: local status, inventory, health, diagnostics; commands only after safety design |
| HVO safety classification | Read-only telemetry initially; on/off/cycle commands can be low-risk or high-risk depending on connected load |

## Capability Summary

| Capability group | Read-only | Read-write | Command/action | Local UI | Outbox/cloud | Notes |
|------------------|-----------|------------|----------------|----------|--------------|-------|
| Device discovery | Yes | No | No | Yes | Inventory/status candidate | Legacy devices can be discovered via UDP 9999; newer devices may respond on UDP 20002 and need credentials. |
| Device metadata | Yes | No | No | Yes | Inventory snapshot candidate | Observed field sets vary by device/protocol. |
| Outlet/switch state | Yes | No initially | On/off/toggle deferred | Yes | Status/event candidate | Commands must be gated by connected-load safety. |
| Power telemetry | Model dependent | No | No | Yes | Candidate telemetry | EP25 and HS300 energy fields were observed in milli-units/Wh; other models may differ. |
| Energy usage statistics | Model dependent | No initially | Reset commands deferred | Yes | Candidate low-rate telemetry | Reset semantics are dangerous for historical counters. |
| LED/night mode | Model dependent | Deferred | Command deferred | Optional | Local-only | Not needed for initial telemetry. |
| Schedule/countdown/away rules | Model dependent | Deferred | Commands deferred | Link/native UI | Local-only initially | Native app should remain primary until safety/design requirements are explicit. |
| Bulb/light features | Model dependent | Deferred | Commands deferred | Status only initially | Candidate status only | Brightness/color commands are different capability class than outlets. |
| Tapo camera/media | Out of initial scope | No | No | Native UI | No | Should not be mixed with smart plug/switch gateway without a separate media/security design. |

## Sanitized Live Discovery Summary

Read-only discovery on 2026-05-29 scanned `192.168.1.0/24` and `192.168.2.0/24` for legacy Kasa TCP `9999` responders. The scan sent only `system.get_sysinfo` and `emeter.get_realtime`; it did not send on/off/toggle, schedule, reset, reboot, or configuration commands.

The latest sanitized scan observed 22 legacy responders. One earlier shorter-timeout scan observed 21 responders, so implementation should tolerate intermittent/offline devices.

| Model | Count | Hardware version | Software version | Device family | Children/outlets observed | Energy fields observed | Notes |
|-------|------:|------------------|------------------|---------------|---------------------------|------------------------|-------|
| EP25(US) | 8 | `1.0` | `1.0.14 Build 240424 Rel.094105` | `IOT.SMARTPLUGSWITCH` | none | `current_ma`, `power_mw`, `total_wh`, `voltage_mv` | Single-outlet plug with top-level `relay_state`. |
| HS300(US) | 4 | `1.0` | `1.0.21 Build 210524 Rel.161309` | `IOT.SMARTPLUGSWITCH` | 6 children | `current_ma`, `power_mw`, `total_wh`, `voltage_mv` | Power strip; per-outlet state observed in `children[].state`. |
| HS300(US) | 2 | `2.0` | `1.0.12 Build 220121 Rel.175814` | `IOT.SMARTPLUGSWITCH` | 6 children | `current_ma`, `power_mw`, `slot_id`, `total_wh`, `voltage_mv` | Power strip; `slot_id` appears in realtime energy response for this hardware/firmware group. |
| KP200(US) | 5 | `1.0` | `1.0.9 Build 200618 Rel.140140` | `IOT.SMARTPLUGSWITCH` | 2 children | none | Dual-outlet wall plug; per-outlet state observed in `children[].state`. |
| HS105(US) | 1 | `1.0` | `1.5.6 Build 191114 Rel.104204` | `IOT.SMARTPLUGSWITCH` via `type` | none | unsupported response with `err_msg` and `err_code` `-1` | Single-outlet plug with top-level `relay_state`. |
| KL130(US) | 2 | `1.0` | `1.8.11 Build 191113 Rel.105336` | `IOT.SMARTBULB` | none | none | Bulb status includes `light_state`; light commands are deferred. |

## Recommended Initial Scope

Start with legacy Kasa LAN `IOT`/XOR devices only, because that protocol is the observed installed-device family and can be implemented and simulated deterministically without cloud credentials.

Initial candidate capabilities:

- static host polling for configured devices.
- optional UDP discovery for legacy port `9999` devices.
- read-only `system.get_sysinfo`.
- read-only `emeter.get_realtime` when supported.
- read-only outlet state from top-level `relay_state`, child `children[].state`, and bulb `light_state.on_off` only after parser tests cover the observed shapes.
- local status dashboard and gateway health.
- shared edge outbox for device status/power telemetry if a power-metering model is confirmed.

Explicitly deferred:

- on/off/toggle commands.
- schedule/countdown/away-mode changes.
- factory reset, reboot, firmware, cloud bind/unbind, Wi-Fi provisioning.
- Tapo/new Kasa authenticated `SMART`/KLAP/AES implementation.
- Matter integration.

## Open Questions

| Question | Why it matters | Status |
|----------|----------------|--------|
| Which exact TP-Link/Kasa/Tapo models are installed or planned? | Determines protocol, auth, capabilities, and safety. | Partially answered by sanitized live scan; final production list still open |
| Are the devices legacy Kasa LAN devices, newer authenticated Kasa/Tapo devices, or Matter devices? | These are materially different protocol families. | Observed responders are legacy TCP `9999`; newer/authenticated devices not needed for initial implementation unless new hardware appears |
| What loads are connected to each outlet/switch? | Determines command safety and whether any commands can be exposed. | Open |
| Is power telemetry needed, or only outlet state/inventory? | Determines central storage/outbox model. | Open; EP25 and HS300 energy fields are available if needed |
| Should HVO ever control these devices, or only monitor them? | Affects local UI, auth, audit, and cloud policy. | Open |
