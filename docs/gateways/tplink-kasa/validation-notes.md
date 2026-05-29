# TP-Link / Kasa Validation Notes

## Evidence Summary

| Evidence | Result | Confidence | Notes |
|----------|--------|------------|-------|
| `softScheck/tplink-smartplug` README/client | Legacy Smart Home protocol on TCP port `9999`, XOR autokey, JSON commands. | Medium-high for legacy HS100/HS110-era devices | Community reverse-engineering reference, not official docs. |
| `softScheck` command list | Operation groups for system, WLAN, cloud, time, emeter, schedule, countdown, away mode. | Medium | Command inventory only; response fixture coverage still needs implementation tests. |
| `python-kasa` docs | Discovery ports `9999` and `20002`, legacy/new protocol distinction, authentication requirements, protocol/transport overview. | Medium-high | Community library docs with broad device support. |
| `python-kasa` supported devices | Many model/hardware/firmware combinations and auth markers. | Medium | Useful for future model checks; observed HVO responders are legacy TCP `9999`. |
| `plasticrake/tplink-smarthome-simulator` | External simulator exists for legacy Smart Home devices. | Medium | Could be used for comparison; HVO in-process fake preferred for CI. |
| HVO read-only live scan, 2026-05-29 | 42 total legacy TCP `9999` responders found across `192.168.1.0/24`, `192.168.2.0/24`, and `192.168.9.0/24` after routing update and added devices. | High for observed devices; incomplete for any non-legacy devices | Sent only `system.get_sysinfo` and `emeter.get_realtime`; no writes/switch/dimmer commands. Committed docs use aggregate/sanitized findings only. |

## Read-Only Live Discovery Notes

Scan scope: `192.168.1.0/24`, `192.168.2.0/24`, and `192.168.9.0/24`.

Subnet-level result:

| Network | Count | Models observed | Interpretation |
|---------|------:|-----------------|----------------|
| `192.168.1.0/24` | 14 | EP25, HS105, HS300, KP200, KL130 | Likely mostly observatory devices. |
| `192.168.2.0/24` | 8 | EP25, HS300 | Likely undercounted; about 14 home light switches are expected but did not answer the legacy TCP `9999` read-only scan. |
| `192.168.9.0/24` | 20 | HS105, HS200, HS210, HS220, KL130, LB230 | Home switch/bulb network reachable after hvo.lan/Tailscale route update. |

Follow-up read-only rescan of `192.168.2.0/24` still found only 8 legacy TCP `9999` responders: 7 EP25 devices and 1 HS300 device. No light switch, 3-way switch, or dimmer models answered that scan.

Initial read-only scan of `192.168.9.0/24` found 0 legacy TCP `9999` responders before hvo.lan/Tailscale routing was updated. After the route update, the same read-only scan found 17 responders: 11 HS200 switches, 3 HS210 3-way switches, and 3 HS220 dimmers. After adding more devices, a later read-only rescan found 20 responders: the same switch/dimmer counts plus 1 HS105 plug, 1 KL130 bulb, and 1 LB230 bulb.

Commands sent:

- `system.get_sysinfo`
- `emeter.get_realtime`

Commands not sent:

- on/off/toggle or `system.set_relay_state`
- dimmer-level changes
- schedule/countdown/away writes
- reset/reboot/factory reset
- Wi-Fi/cloud/account writes
- arbitrary command payloads outside the two read-only requests above

Sanitized result summary:

| Model | Count | Read-only validation result |
|-------|------:|-----------------------------|
| EP25(US) | 8 | Legacy TCP `9999` sysinfo and realtime energy succeeded; top-level `relay_state` observed. |
| HS300(US) | 6 | Legacy TCP `9999` sysinfo and realtime energy succeeded; six child outlets observed. |
| HS200(US) | 11 | Legacy TCP `9999` sysinfo succeeded; top-level `relay_state` observed. |
| HS210(US) | 3 | Legacy TCP `9999` sysinfo succeeded; top-level `relay_state` observed; `emeter.get_realtime` returned unsupported error shape. |
| HS220(US) | 3 | Legacy TCP `9999` sysinfo succeeded; top-level `relay_state` observed; `emeter.get_realtime` returned unsupported error shape. |
| KP200(US) | 5 | Legacy TCP `9999` sysinfo succeeded; two child outlets observed; no realtime energy fields observed. |
| KL130(US) | 3 | Legacy TCP `9999` sysinfo succeeded; bulb `light_state` observed; commands deferred. |
| HS105(US) | 2 | Legacy TCP `9999` sysinfo succeeded; top-level `relay_state` observed; `emeter.get_realtime` returned unsupported error shape. |
| LB230(E26) | 1 | Legacy TCP `9999` sysinfo succeeded; bulb `light_state` observed; commands deferred. |

Observed energy fields on supported devices:

- `current_ma`
- `power_mw`
- `voltage_mv`
- `total_wh`
- `slot_id` on HS300 hardware `2.0` responses

Observed state field shapes:

- EP25/HS105/HS200/HS210/HS220: top-level `relay_state`, with `on_time` and `next_action` where present.
- HS300/KP200: child outlet `children[].state`, `children[].on_time`, `children[].next_action`.
- KL130/LB230: bulb `light_state.on_off` plus dimming/color capability flags.

Privacy/safety handling:

- Raw aliases, MAC addresses, device IDs, hardware IDs, coordinates, and per-device IPs were not added to committed docs.
- The `python-kasa --redact` raw command was not used as committed evidence because it still printed aliases and identifiers in this environment.
- Two scans differed by one responder because of timeout/discovery timing, so the gateway should not treat a missing device as fatal.

Known likely gaps:

- About 14 home Kasa light switches are expected on `192.168.2.0/24`, but were not observed by the legacy TCP `9999` scan.
- 3-way switches and dimmer switches were observed on `192.168.9.0/24`; dimmer-specific fields/commands still need separate read-only validation.
- Additional home devices on `192.168.2.0/24` may be offline or not rejoined after Wi-Fi changes.
- HomeKit-compatible, Tapo, and Matter-capable devices may require a separate discovery/auth path and should not be assumed to use legacy TCP `9999`.

## Initial Non-Live Validation Plan

1. Unit-test legacy XOR autokey encode/decode with known local round-trip vectors derived from the algorithm.
2. Unit-test TCP frame writer/reader with length-prefix edge cases.
3. Build `FakeKasaLegacyServer` for in-process TCP tests.
4. Fake server should support at least:
   - `system.get_sysinfo`
   - `emeter.get_realtime`
   - single-outlet top-level `relay_state` fixture
   - multi-outlet `children[]` fixture
   - bulb `light_state` fixture
   - unsupported module error fixture
   - malformed JSON response
   - truncated length/payload
   - timeout/no response
5. Integration-test `KasaLegacyClient` against the fake server.
6. Add configuration/registry tests for configured devices, discovery-only devices, expected model mismatch, expected child count mismatch, and per-network responder counts.
7. Worker tests should verify one failing device does not block other devices.
8. Outbox tests should wait until local device configuration and status semantics are stable.

## External Simulator Option

The `plasticrake/tplink-smarthome-simulator` project can simulate legacy devices such as HS100, HS105, HS110, HS200, and bulbs. It is useful for cross-checking HVO behavior, but should not be a required CI dependency unless pinned and containerized.

Recommended use:

- use HVO in-process fake for normal unit/integration tests.
- use the external simulator as an optional compatibility check during protocol development.
- document any mismatch between HVO fake behavior and external simulator behavior.

## Live Validation Plan

Live tests must be opt-in and require explicit device host/configuration.

Suggested environment variables:

| Variable | Purpose |
|----------|---------|
| `KASA_LIVE_HOST` | Device IP/host. |
| `KASA_LIVE_PORT` | Device port, default `9999` for legacy. |
| `KASA_LIVE_NETWORK` | Optional network label such as `observatory` or `home` for discovery reports. |
| `KASA_LIVE_EXPECTED_MODEL` | Optional expected model guard. |
| `KASA_LIVE_ALLOW_COMMANDS` | Must be `true` before any on/off command tests run. Default false. |

Read-only live tests:

- connect to configured host.
- read system info.
- verify expected model when configured.
- parse top-level relay, child outlet, or bulb light state according to observed response shape.
- read realtime energy if the device is expected to support `emeter`.
- treat unsupported `emeter` response as a supported non-energy path, not a device failure.
- verify response times are within configured timeout.
- verify repeated polling does not destabilize the device.

Command live tests are deferred. If ever added, they must:

- require `KASA_LIVE_ALLOW_COMMANDS=true`.
- require device safety classification.
- read current state before command.
- execute command.
- read back state after command.
- restore original state when safe.
- never run in CI by default.

## Open Validation Questions

| Question | Why it matters | Validation path |
|----------|----------------|-----------------|
| Which exact model/firmware is installed? | Determines protocol family and auth. | Sanitized scans captured EP25, HS105, HS200, HS210, HS220, HS300, KP200, KL130, and LB230 legacy responders; production subset still needs confirmation. |
| Does the target device respond on port `9999`? | Confirms legacy scope. | Confirmed for 42 observed responders across scanned subnets after route updates and added devices. |
| Does the device require authentication? | Changes protocol implementation. | `python-kasa discover` or HVO discovery. |
| Which devices exist on `192.168.2.0/24` after Wi-Fi recovery? | Current scan likely undercounts home devices. | Reset/rejoin affected home devices, then rerun read-only discovery. |
| Which devices exist on `192.168.9.0/24` after routing is configured? | Confirms home switch inventory. | Route update completed; read-only scan observed 17 legacy responders. Keep monitoring per-network counts for routing regressions. |
| Which 3-way and dimmer switch models are installed? | Switch/dimmer state and command shapes may differ from plugs/strips/bulbs. | Initial read-only scan observed HS210 and HS220; dimmer-specific read-only fields still need validation. |
| Are HomeKit/Tapo/Matter-capable devices present? | They may use non-legacy protocols and auth. | Model inventory and separate non-write discovery. |
| What are exact `system.get_sysinfo` response fields? | Needed for DTO mapping. | Sanitized field names captured; implementation needs committed fake fixtures. |
| What are exact `emeter.get_realtime` response fields and units? | Needed for power telemetry. | Sanitized field names captured for EP25/HS300; implementation needs committed fake fixtures. |
| Does energy total reset on command, power loss, firmware update, or app action? | Historical cloud storage semantics. | Manual/live testing; do not infer. |
| What loads are connected? | Command safety. | Operator inventory. |

## Current Blockers Before Implementation Claims

- Production device subset and connected loads are not confirmed.
- No executable TP-Link/Kasa fixture/test data is captured in repo yet.
- Home `192.168.2.0/24` inventory is likely incomplete until Wi-Fi recovery/rescan.
- Home `192.168.9.0/24` inventory is reachable after hvo.lan/Tailscale subnet routing update, but production subset and load safety still need confirmation.
- Central per-device outlet/power payload contract is not locked.
- Shared `HVO.Edge.Outbox` still needs failure-kind standardization before new gateways should rely on it for production dead-letter classification.
- Command safety classification is not complete.

## Validation Status Register

| Area | Status | Notes |
|------|--------|-------|
| Legacy XOR algorithm | Researched | Needs C# tests. |
| Legacy TCP framing | Live read-only validated | Needs fake server tests. |
| Legacy UDP discovery | Researched at high level | Needs packet/framing validation. |
| Per-network discovery reporting | Live scan manually summarized | Needs configuration/registry implementation. |
| System info parsing | Live shapes captured; not implemented | Needs sanitized fixtures for EP25/HS105, HS300/KP200, and KL130 shapes. |
| Energy parsing | Live fields captured; not implemented | Needs fixtures for EP25/HS300 success and HS105 unsupported response. |
| Outbox forwarding | Deferred | Establish device library/configuration and local status first; then use common outbox standard. |
| Local UI | Not implemented | Status/dashboard only initially. |
| Commands | Deferred | Safety design required. |
