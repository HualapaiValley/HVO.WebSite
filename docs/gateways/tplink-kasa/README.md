# TP-Link / Kasa Gateway

## Status

- Phase 0 status: research baseline plus sanitized read-only live discovery captured; prototype read-only legacy TCP client/probe implemented. Observed device types are sufficient to start library/model design, but full physical inventory is incomplete.
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
| Prototype read-only probe | `src/HVO.Gateway.TplinkKasa` and tests | Implemented | Covers allowlisted read-only metadata probes, sanitized shape summaries, fake TCP server tests, and explicit rejection of mixed read/write payloads. |
| Official product pages | TP-Link/Kasa pages for observed models | Partially checked | EP25 page explicitly says HomeKit. Checked HS200, HS210, HS220, KP200, HS300, KL130, and LB230 pages for feature context; do not infer HomeKit/Matter when not stated. |
| Official TP-Link docs | TP-Link/Kasa/Tapo local protocol docs | Needed | No official local API reference found during initial research. |
| Exact HVO hardware | Installed/planned model and firmware | Device types mostly captured | Legacy responders observed for EP25, HS105, HS200, HS210, HS220, HS300, KP200, KL130, and LB230. Final physical device list and connected loads still need operator confirmation. |

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

## Identity And Locator Rules

Device identity must not be based on IP address. TP-Link/Kasa IP addresses can change through DHCP or network moves, and a stale IP could point to a different device later.

HVO identity rules:

- Use the vendor device ID from `system.get_sysinfo` as the primary device identity.
- Treat IP address/DNS host as a current connection locator only.
- Treat MAC address as a secondary locator/validation hint; it can help with ARP-assisted lookup but does not replace device ID validation.
- Every poll should read system info first and validate the connected device against configured identity before accepting data.
- Any future command path must fail closed unless the connected device identity matches the configured device ID and guard fields.
- UI actions must target configured device identity, not an IP address.

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

Read-only discovery on 2026-05-29 scanned `192.168.1.0/24`, `192.168.2.0/24`, and later `192.168.9.0/24` for legacy Kasa TCP `9999` responders. The scan sent only `system.get_sysinfo` and `emeter.get_realtime`; it did not send on/off/toggle, dimmer-level changes, schedule, reset, reboot, or configuration commands.

The first whole-estate sanitized scan observed 22 legacy responders on `192.168.1.0/24` and `192.168.2.0/24`. One earlier shorter-timeout scan observed 21 responders, so implementation should tolerate intermittent/offline devices. After hvo.lan router and Tailscale routing were updated for `192.168.9.0/24`, read-only rescans found 17 and then 20 legacy TCP `9999` responders as more devices were added.

Subnet-level result:

| Network | Count | Models observed | Notes |
|---------|------:|-----------------|-------|
| `192.168.1.0/24` | 14 | EP25, HS105, HS300, KP200, KL130 | Likely observatory devices based on network location. |
| `192.168.2.0/24` | 8 | EP25, HS300 | Likely undercounted; about 14 home light switches are expected but did not answer the legacy TCP `9999` read-only scan. |
| `192.168.9.0/24` | 20 | HS105, HS200, HS210, HS220, KL130, LB230 | Home light switch/bulb network reachable after hvo.lan/Tailscale route update. |

Network labels are topology hints, not a device admission policy. Current HVO labels are:

- `192.168.1.0/24`: observatory.
- `192.168.2.0/24`: home.
- `192.168.9.0/24`: home guest/IoT.

Configured devices may have any reachable address, including addresses outside those ranges. The gateway must use configured device identity and validated reachability, not subnet assumptions, to decide what a device is.

| Model | Count | Hardware version | Software version | Device family | Children/outlets observed | Energy fields observed | Notes |
|-------|------:|------------------|------------------|---------------|---------------------------|------------------------|-------|
| EP25(US) | 8 | `1.0` | `1.0.14 Build 240424 Rel.094105` | `IOT.SMARTPLUGSWITCH` | none | `current_ma`, `power_mw`, `total_wh`, `voltage_mv` | Single-outlet plug with top-level `relay_state`. |
| HS300(US) | 4 | `1.0` | `1.0.21 Build 210524 Rel.161309` | `IOT.SMARTPLUGSWITCH` | 6 children | `current_ma`, `power_mw`, `total_wh`, `voltage_mv` | Power strip; per-outlet state observed in `children[].state`. |
| HS300(US) | 2 | `2.0` | `1.0.12 Build 220121 Rel.175814` | `IOT.SMARTPLUGSWITCH` | 6 children | `current_ma`, `power_mw`, `slot_id`, `total_wh`, `voltage_mv` | Power strip; `slot_id` appears in realtime energy response for this hardware/firmware group. |
| KP200(US) | 5 | `1.0` | `1.0.9 Build 200618 Rel.140140` | `IOT.SMARTPLUGSWITCH` | 2 children | none | Dual-outlet wall plug; per-outlet state observed in `children[].state`. |
| HS105(US) | 2 | `1.0` | `1.5.6 Build 191114 Rel.104204` | `IOT.SMARTPLUGSWITCH` via `type` | none | unsupported response with `err_msg` and `err_code` `-1` | Single-outlet plug with top-level `relay_state`. |
| KL130(US) | 3 | `1.0` | `1.8.11 Build 191113 Rel.105336` | `IOT.SMARTBULB` | none | none | Bulb status includes `light_state`; light commands are deferred. |
| HS200(US) | 11 | `1.0` | `1.2.6 Build 200727 Rel.121953` | `IOT.SMARTPLUGSWITCH` | none | none | Light switch with top-level `relay_state`; commands deferred. |
| HS210(US) | 3 | `1.0` | `1.5.8 Build 191118 Rel.135937` | `IOT.SMARTPLUGSWITCH` | none | unsupported response with `err_msg` | 3-way light switch with top-level `relay_state`; commands deferred. |
| HS220(US) | 3 | `1.0` | `1.5.11 Build 200214 Rel.152651` | `IOT.SMARTPLUGSWITCH` | none | unsupported response with `err_msg` | Dimmer switch with top-level `relay_state`; dimmer controls deferred. |
| LB230(E26) | 1 | `1.0` | `1.8.11 Build 191113 Rel.105336` | `IOT.SMARTBULB` | none | none | Bulb status includes `light_state`; light commands are deferred. |

## Alternate Ecosystem / Protocol Flags

The table below tracks non-legacy protocol or ecosystem support separately from the observed legacy TCP `9999` behavior. HVO should not implement HomeKit, Matter, Tapo, or authenticated Kasa protocols unless they provide required extra functionality or are needed for future devices that do not expose legacy Kasa.

| Model | Legacy TCP `9999` observed | HomeKit | Matter | Tapo/new authenticated Kasa | Evidence / notes |
|-------|----------------------------|---------|--------|----------------------------|------------------|
| EP25(US) | Yes | Confirmed | Not confirmed | Not required for observed devices | Official Kasa EP25 page identifies it as HomeKit; legacy TCP `9999` also worked. |
| HS105(US) | Yes | Not confirmed | Not confirmed | Not required for observed devices | Legacy TCP `9999` worked; no alternate protocol evidence captured. |
| HS200(US) | Yes | Not confirmed | Not confirmed | Not required for observed devices | Official page checked for feature context; HomeKit/Matter not confirmed from checked page. |
| HS210(US) | Yes | Not confirmed | Not confirmed | Not required for observed devices | Official page checked for feature context; HomeKit/Matter not confirmed from checked page. |
| HS220(US) | Yes | Not confirmed | Not confirmed | Not required for observed devices | Official page checked for feature context; HomeKit/Matter not confirmed from checked page. |
| HS300(US) | Yes | Not confirmed | Not confirmed | Not required for observed devices | Official page checked for feature context; HomeKit/Matter not confirmed from checked page. |
| KP200(US) | Yes | Not confirmed | Not confirmed | Not required for observed devices | Official page checked for feature context; HomeKit/Matter not confirmed from checked page. |
| KL130(US) | Yes | Not confirmed | Not confirmed | Not required for observed devices | Official page checked for feature context; HomeKit/Matter not confirmed from checked page. |
| LB230(E26) | Yes | Not confirmed | Not confirmed | Not required for observed devices | Official page checked for feature context; HomeKit/Matter not confirmed from checked page. |

Known likely gaps from operator inventory:

- About 17 home light switch devices are now observed on `192.168.9.0/24` after route updates.
- `192.168.2.0/24` may still be undercounted for plugs/strips or other devices.
- Additional devices may already be on `192.168.2.0/24` but should eventually be reset/rejoined to `192.168.9.0/24` for the home Kasa network.
- EP25 is confirmed HomeKit-capable from the official product page, but legacy TCP `9999` currently provides the read-only data HVO needs.
- Other HomeKit-compatible Kasa devices may exist in the broader estate; mark only confirmed model/support combinations in HVO metadata.
- Tapo/Matter devices were not in initial implementation scope and were not confirmed by the legacy TCP `9999` scan.

## Phase 0 Conclusion

The observed legacy Kasa device-type coverage is broad enough to begin implementation design for the local device library, configuration model, parser, local UI, telemetry model, and eventual outbox contracts.

Observed legacy device categories:

- single-outlet plugs: EP25, HS105.
- power strips: HS300.
- dual outlets: KP200.
- light switches: HS200.
- 3-way switches: HS210.
- dimmers: HS220.
- bulbs: KL130, LB230.

Remaining inventory work should not block initial library design, but the gateway must support devices being added later as they are reset onto the home `192.168.9.0/24` network.

## Recommended Initial Scope

Start with a device control/configuration library before outbox work. The first library target should be legacy Kasa LAN `IOT`/XOR devices, because that protocol is the observed installed-device family and can be implemented and simulated deterministically without cloud credentials.

Initial candidate capabilities:

- static host polling for configured devices after identity validation.
- configuration model for known devices, discovered devices, stable device ID, last-known host, optional MAC, expected model/hardware/software, network/subnet, capability flags, and safety classification.
- operator-initiated discovery/add-device workflow for legacy port `9999` devices; no continuous scanning for new devices.
- read-only `system.get_sysinfo`.
- read-only `emeter.get_realtime` when supported.
- read-only metadata probes when explicitly requested for setup/capability validation: schedule, next schedule action, countdown, away, cloud info, cloud firmware list, time, timezone, energy day/month/gain stats where supported, device icon/download-state diagnostics, bulb light details, and HS220 dimmer parameter/details.
- opt-in, shape-only cached Wi-Fi scan probe using `netif.get_scaninfo refresh:0`; raw SSIDs/BSSIDs must not be printed or committed.
- read-only outlet state from top-level `relay_state`, child `children[].state`, and bulb `light_state.on_off` only after parser tests cover the observed shapes.
- model/capability mapping for plugs, strips, dual outlets, switches, 3-way switches, dimmers, and bulbs.
- local status dashboard and gateway health.
- shared edge outbox later, after device discovery/configuration and local status are stable.

Prototype code now exists under `src/HVO.Gateway.TplinkKasa` with tests under `tests/HVO.Gateway.TplinkKasa.Tests`. It includes a read-only legacy TCP client, sanitized fixtures, fake TCP server tests, identity validation, capability detection, sanitized field/type shape summaries, and a redacted CLI probe/scan utility.

Explicitly deferred:

- live on/off/toggle/dimmer commands until an operator explicitly approves a specific test device and command.
- schedule/countdown/away-mode changes.
- factory reset, reboot, firmware, cloud bind/unbind, Wi-Fi provisioning.
- active Wi-Fi scan refresh (`netif.get_scaninfo refresh:1`) unless explicitly approved for a selected device/network.
- Tapo/new Kasa authenticated `SMART`/KLAP/AES implementation.
- Matter integration.

## Open Questions

| Question | Why it matters | Status |
|----------|----------------|--------|
| Which exact TP-Link/Kasa/Tapo models are installed or planned? | Determines protocol, auth, capabilities, and safety. | Partially answered by sanitized live scan; final production list still open |
| Are the devices legacy Kasa LAN devices, newer authenticated Kasa/Tapo devices, HomeKit devices, or Matter devices? | These are materially different protocol families. | Observed responders are legacy TCP `9999`; home `2.x` devices and HomeKit/Tapo/Matter-capable devices need follow-up discovery after Wi-Fi recovery |
| What loads are connected to each outlet/switch? | Determines command safety and whether any commands can be exposed. | Open |
| Is power telemetry needed, or only outlet state/inventory? | Determines central storage/outbox model. | Open; EP25 and HS300 energy fields are available and prototype parser handles milli-units |
| Should HVO ever control these devices, or only monitor them? | Affects local UI, auth, audit, and cloud policy. | Command-capable design accepted, but live commands require explicit per-device/per-command approval |
| Which devices remain on `192.168.2.0/24` and should move to `192.168.9.0/24`? | Determines final home network configuration and production polling list. | Open; update as devices are reset/rejoined |
