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
| HVO prototype read-only scan, 2026-05-29 | 41 total legacy TCP `9999` responders found with prototype utility after adding read-only metadata probes. | High for observed devices at scan time | Sent only read-only sysinfo, realtime/day/month energy, schedule rules, next schedule action, countdown rules, away rules, cloud info, time, timezone, and LED status requests. No switch/dimmer/write commands. Output was summarized/redacted. |
| HVO prototype read-only expansion tests, 2026-05-29 | Additional allowlisted read-only probes implemented and fake-server tested. | High for command safety gate | Added icon/download-state, EMeter gain, cloud firmware list, bulb namespaced reads, HS220 dimmer read probes, and opt-in cached Wi-Fi scan shape probe. Mixed read/write payloads and `netif.get_scaninfo refresh:1` are rejected. |
| HVO expanded privacy-safe live scan, 2026-05-29 | 41 total legacy TCP `9999` responders found with expanded read-only probes. | High for observed devices at scan time | Sent no writes and did not enable Wi-Fi scan. Validated bulb namespaced reads, HS220 dimmer reads, cloud firmware lists, download state, and model-specific icon/gain support. Output was summarized/redacted. |

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
- `emeter.get_daystat` in prototype validation only
- `emeter.get_monthstat` in prototype validation only
- `schedule.get_rules` in prototype validation only
- `schedule.get_next_action` in prototype validation only
- `count_down.get_rules` in prototype validation only
- `anti_theft.get_rules` in prototype validation only
- `cnCloud.get_info` in prototype validation only; field shapes only, raw values not documented
- `time.get_time` in prototype validation only
- `time.get_timezone` in prototype validation only
- `system.get_led_off` in prototype validation only
- `system.get_dev_icon` in expanded prototype validation; model-specific support
- `system.get_download_state` in expanded prototype validation; model-specific support
- `emeter.get_vgain_igain` in expanded prototype validation; model-specific support
- `cnCloud.get_intl_fw_list` in expanded prototype validation; model-specific support
- `smartlife.iot.smartbulb.lightingservice.get_light_state` in expanded prototype validation
- `smartlife.iot.smartbulb.lightingservice.get_light_details` in expanded prototype validation
- bulb namespaced `smartlife.iot.common.cloud`, `timesetting`, `schedule`, and `emeter` reads in expanded prototype validation
- `smartlife.iot.dimmer.get_default_behavior` in expanded prototype validation
- `smartlife.iot.dimmer.get_dimmer_parameters` in expanded prototype validation
- `netif.get_scaninfo` with `refresh:0` in prototype tests only; opt-in, privacy-sensitive, live validation pending

Commands not sent:

- on/off/toggle or `system.set_relay_state`
- dimmer-level changes
- schedule/countdown/away writes
- reset/reboot/factory reset
- Wi-Fi/cloud/account writes
- `netif.get_scaninfo` with `refresh:1`, because it actively refreshes nearby AP scan data and can expose SSIDs
- arbitrary command payloads outside the read-only allowlist above

Prototype scan commands were allowlisted in code and remained read-only. No `set_*`, `add_rule`, `edit_rule`, `delete_rule`, relay, dimmer write, reset, reboot, Wi-Fi connect, cloud bind/unbind, or arbitrary JSON write command was sent. The allowlist now requires every module/operation in a payload to be read-only, so mixed read/write payloads fail closed.

Latest prototype read-only summary:

| Network | Count | Device IDs present | MACs present | Energy supported | Models observed |
|---------|------:|-------------------:|-------------:|-----------------:|-----------------|
| `192.168.1.0/24` | 14 | 14 | 14 | 6 | EP25, HS105, HS300, KP200, KL130 |
| `192.168.2.0/24` | 7 | 7 | 7 | 7 | EP25, HS300 |
| `192.168.9.0/24` | 20 | 20 | 20 | 0 | HS105, HS200, HS210, HS220, KL130, LB230 |

Expanded privacy-safe scan summary:

| Network | Count | Device IDs present | MACs present | Energy supported | Newly validated read-only metadata |
|---------|------:|-------------------:|-------------:|-----------------:|------------------------------------|
| `192.168.1.0/24` | 14 | 14 | 14 | 6 | cloud firmware, download state, EP25/HS300 hw `2.0` EMeter gain, KL130 bulb namespaces. |
| `192.168.2.0/24` | 7 | 7 | 7 | 7 | cloud firmware, download state, EMeter gain on EP25 and HS300 hw `2.0`; HS300 hw `2.0` firmware list included non-empty `fw_list` shape. |
| `192.168.9.0/24` | 20 | 20 | 20 | 0 | HS220 dimmer default behavior/parameters, HS200 icon, bulb namespaces for KL130/LB230, cloud firmware for most non-bulb switches/plugs. |

Prototype metadata observations:

- schedule, countdown, away, cloud, time, and timezone read operations returned successful responses for switch/plug/strip/dual-outlet legacy devices in the latest scan.
- day/month energy stats returned successful responses for EP25 and HS300 energy-capable groups.
- bulb models returned unsupported/error responses for the probed emeter/schedule/countdown/away/cloud/time/timezone/LED module requests in the latest scan.
- `system.get_led_off` returned unsupported/error responses on all observed latest-scan devices; LED metadata remains modeled but not observed as supported.
- bulb MAC identity uses `mic_mac`; other observed legacy families use `mac`.
- `system.get_download_state` returned success on EP25, HS105, HS210, HS220, HS300, KP200, KL130, and LB230; HS200 returned unsupported/error in the latest expanded scan.
- `system.get_dev_icon` returned success on HS200 and unsupported/error on the other observed model groups.
- `emeter.get_vgain_igain` returned success on EP25 and HS300 hardware `2.0`; HS300 hardware `1.0` and non-energy model groups returned unsupported/error.
- `cnCloud.get_intl_fw_list` returned success on observed non-bulb plug/switch/strip/outlet models through the `cnCloud` namespace and unsupported/error on bulbs through that namespace; some HS200 responders differed on support.
- KL130 and LB230 supported bulb namespaced cloud, time, timezone, schedule, next action, realtime EMeter, light state, and light details reads.
- HS220 supported dimmer default behavior and dimmer parameter reads.

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
- Wi-Fi scan evidence is opt-in and shape-only; raw SSIDs/BSSIDs must not be printed or committed.

Known likely gaps:

- About 14 home Kasa light switches are expected on `192.168.2.0/24`, but were not observed by the legacy TCP `9999` scan.
- 3-way switches and dimmer switches were observed on `192.168.9.0/24`; dimmer-specific fields/commands still need separate read-only validation.
- Additional home devices on `192.168.2.0/24` may be offline or not rejoined after Wi-Fi changes.
- Additional devices can be added later as they are reset/rejoined to the new Wi-Fi, likely moving from `192.168.2.0/24` to `192.168.9.0/24`.
- HomeKit-compatible, Tapo, and Matter-capable devices may require a separate discovery/auth path and should not be assumed to use legacy TCP `9999`.

## Capability Research Plan

The observed device types are enough to start the capability/library design, but the full protocol surface still needs research before classes, interfaces, enums, UI models, telemetry models, and outbox contracts are locked.

Research targets:

- system metadata fields for all observed models.
- switch state and top-level `relay_state` semantics.
- child outlet shape for HS300 and KP200.
- realtime energy shape for EP25 and HS300, including per-outlet `slot_id` behavior.
- unsupported module/error shapes for non-energy devices.
- bulb `light_state`, dimming, color, and variable color temperature fields for KL130 and LB230.
- dimmer-specific read-only fields for HS220.
- remaining live shape gaps for privacy-sensitive Wi-Fi scan and any model/firmware group not currently reachable.
- privacy-safe live Wi-Fi scan shape evidence if needed; raw SSIDs must not be committed.
- schedule/countdown/away metadata read operations, without writes.
- LED/night-mode read operations.
- cloud/account read operations only if safe to sanitize.
- Tapo/Matter/HomeKit discovery boundary, documented separately from legacy Kasa.
- alternate ecosystem metadata per observed model, without assuming HVO needs to implement those protocols.

Design outputs needed before implementation is considered complete:

- protocol client interfaces and transport implementation.
- capability enums/records and device profile detection.
- configuration schema for networks, stable device identity, current locators, capabilities, and safety.
- identity validation path that uses device ID as primary identity and treats IP/MAC as locator hints.
- local snapshot models for switches, outlets, strips, dimmers, bulbs, and energy meters.
- local UI/status view model.
- telemetry/outbox candidate models, after local semantics are stable.
- simulator/fake fixtures for every observed response shape.

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
   - dimmer-capable switch fixture once HS220 read-only fields are researched
   - unsupported module error fixture
   - malformed JSON response
   - truncated length/payload
   - timeout/no response
5. Integration-test `KasaLegacyClient` against the fake server.
6. Add identity tests for configured device ID match, device ID mismatch at reused IP, MAC mismatch, expected model mismatch, and missing identity fields.
7. Add locator tests for configured host success, configured host identity mismatch, MAC/ARP-assisted host recovery, missing-MAC direct-IP-only behavior, and offline rediscovery unsupported when no locator can be validated.
8. Add configuration/registry tests for configured devices, discovery-only setup results, expected child count mismatch, ARP/MAC locator hints, per-network responder counts, and config-record shape that can map to database storage.
9. Add metadata/capability tests for available/unavailable energy, schedules, countdown, away mode, LED state, firmware, and diagnostics without requiring every device to support every category.
10. Worker tests should verify one failing or mismatched device does not block other devices.
11. Command tests should verify dry-run behavior, explicit approval gating, same-session identity validation, and readback handling without live commands in CI.
12. Outbox tests should wait until local device configuration/database mapping, identity validation, metadata, and status semantics are stable.

Current prototype test coverage:

- `KasaXorCipher` encryption/decryption round-trip and TCP length prefix.
- `KasaLegacyClient` fake-server read-only request/response and non-allowlisted command rejection.
- sanitized sysinfo fixture parsing for EP25, HS300, KP200, HS200, HS210, HS220, KL130, and LB230.
- sanitized realtime energy parsing and milli-unit conversion.
- unsupported emeter response handling.
- capability/profile detection for observed model categories.
- identity validation success, wrong device ID fail-closed, and wrong MAC fail-closed.
- locator validation for configured host success, wrong-device fail-closed, and MAC-assisted host recovery after a failed configured host.
- read-only poller success with identity validation and wrong-device-at-IP fail-closed behavior.
- read-only probe metadata support detection through the fake server.
- sanitized field/type shape summarization for API-guide evidence without raw values.
- stricter read-only command allowlist rejection for mixed read/write payloads.
- opt-in cached Wi-Fi scan shape probe rejection for `refresh:1` and acceptance for `refresh:0` only.
- fake-server coverage for additional read-only icon, download-state, EMeter gain, cloud firmware, bulb, and dimmer probes.

Current prototype command:

```bash
dotnet run --project src/HVO.Gateway.TplinkKasa -- scan --cidr 192.168.1.0/24 --summary true
```

Default CLI output redacts device IDs, MAC addresses, and hosts. Explicit `--include-identifiers true` or `--include-locators true` is required to print them locally.

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
| `KASA_LIVE_MAC_ADDRESS` | Optional MAC guard and locator hint. |
| `KASA_LIVE_PORT` | Device port, default `9999` for legacy. |
| `KASA_LIVE_NETWORK` | Optional network label such as `observatory` or `home` for discovery reports. |
| `KASA_LIVE_EXPECTED_MODEL` | Optional expected model guard. |
| `KASA_LIVE_ALLOW_COMMANDS` | Must be `true` before any on/off command tests run. Default false. |
| `KASA_LIVE_APPROVED_DEVICE_ID` | Required before any live command test can target a device. |

Read-only live tests:

- connect to configured host.
- read system info.
- verify configured device ID before accepting any data.
- verify configured MAC address when available, or document that direct-IP polling cannot rediscover an offline/moved device without MAC or discovery input.
- verify expected model when configured.
- parse top-level relay, child outlet, or bulb light state according to observed response shape.
- read realtime energy if the device is expected to support `emeter`.
- treat unsupported `emeter` response as a supported non-energy path, not a device failure.
- verify response times are within configured timeout.
- verify repeated polling does not destabilize the device.

Command live tests are deferred. If ever added, they must:

- require `KASA_LIVE_ALLOW_COMMANDS=true`.
- require the operator to approve the exact device and command.
- require device safety classification.
- require successful same-session identity validation immediately before command execution.
- read current state before command.
- execute command.
- read back state after command.
- restore original state when safe.
- never run in CI by default.

## Open Validation Questions

| Question | Why it matters | Validation path |
|----------|----------------|-----------------|
| Which exact model/firmware is installed? | Determines protocol family and auth. | Sanitized scans captured EP25, HS105, HS200, HS210, HS220, HS300, KP200, KL130, and LB230 legacy responders; production subset still needs confirmation. |
| Does the target device respond on port `9999`? | Confirms legacy scope. | Confirmed for 42 observed responders in earlier scans and 41 responders in latest prototype scan; counts vary by timeout/device availability. |
| Does the device require authentication? | Changes protocol implementation. | `python-kasa discover` or HVO discovery. |
| Which devices exist on `192.168.2.0/24` after Wi-Fi recovery? | Current scan likely undercounts home devices. | Reset/rejoin affected home devices, then rerun read-only discovery. |
| Which devices exist on `192.168.9.0/24` after routing is configured? | Confirms home switch/bulb inventory. | Route update completed; read-only scan observed 20 legacy responders so far. Keep monitoring per-network counts for routing regressions. |
| Which 3-way and dimmer switch models are installed? | Switch/dimmer state and command shapes may differ from plugs/strips/bulbs. | Initial read-only scan observed HS210 and HS220; dimmer-specific read-only fields still need validation. |
| Are HomeKit/Tapo/Matter-capable devices present? | They may use non-legacy protocols and auth. | Model inventory and separate non-write discovery. |
| Which observed devices have confirmed alternate ecosystems? | Lets HVO document HomeKit/Matter/Tapo options without implementing them unnecessarily. | EP25 HomeKit confirmed from official product page; other observed models are not confirmed from current evidence. |
| Which identity fields are always present per observed model? | Determines safe primary/secondary identity validation. | Latest prototype scan saw device ID on all responders and MAC-equivalent identity on all responders after handling bulb `mic_mac`. |
| Can MAC reliably recover current IP on the target networks? | Determines offline rediscovery support. | Test ARP/table-based lookup and explicit setup scans against configured MACs. |
| What is the complete read-only protocol surface for each observed device category? | Needed for correct classes/interfaces/enums and UI/telemetry models. | Research references plus sanitized live reads before implementation lock. |
| Which read-only metadata categories are available per model? | Energy, schedules, countdown, away mode, LED, firmware, and diagnostics should be represented when available. | Research protocol references and add sanitized fixtures per category before enabling polling/UI fields. |
| What are exact `system.get_sysinfo` response fields? | Needed for DTO mapping. | Sanitized field names captured; implementation needs committed fake fixtures. |
| What are exact `emeter.get_realtime` response fields and units? | Needed for power telemetry. | Sanitized field names captured for EP25/HS300; implementation needs committed fake fixtures. |
| Does energy total reset on command, power loss, firmware update, or app action? | Historical cloud storage semantics. | Manual/live testing; do not infer. |
| What loads are connected? | Command safety. | Operator inventory. |

## Current Blockers Before Implementation Claims

- Production device subset and connected loads are not confirmed.
- Sanitized executable TP-Link/Kasa fixture/test data exists for observed initial shapes, but more fixtures are needed for complete metadata/protocol coverage.
- Home `192.168.2.0/24` inventory is likely incomplete until Wi-Fi recovery/rescan.
- Home `192.168.9.0/24` inventory is reachable after hvo.lan/Tailscale subnet routing update, but production subset and load safety still need confirmation.
- Central per-device outlet/power payload contract is not locked.
- Shared `HVO.Edge.Outbox` still needs failure-kind standardization before new gateways should rely on it for production dead-letter classification.
- Command safety classification is not complete.
- MAC-based rediscovery reliability across target networks is not validated.
- Schedule/countdown/away/LED/diagnostic read-only metadata fields are not fully validated.

## Validation Status Register

| Area | Status | Notes |
|------|--------|-------|
| Legacy XOR algorithm | Implemented/tested | C# round-trip tests pass. |
| Legacy TCP framing | Implemented/tested | Fake server tests cover read-only frame exchange. |
| Legacy UDP discovery | Researched at high level | Needs packet/framing validation. |
| Per-network discovery reporting | Live scan manually summarized | Needs configuration/registry implementation. |
| Identity validation | Prototype implemented/tested | Device ID must be primary identity; IP and MAC are locator/validation hints only. |
| Locator recovery | Prototype implemented/tested | Configured IP is the first locator; MAC can support rediscovery after IP change if network ARP/discovery data is available. |
| Capability model | Prototype implemented/tested | Observed capability flags are derived from sysinfo, energy, read-only metadata probes, and config hints. |
| Metadata model | Prototype implemented/tested | Energy, schedule, countdown, away, LED, firmware, and diagnostics availability are represented where observed/supported. |
| System info parsing | Prototype implemented/tested | Sanitized fixtures cover single relay, multi-outlet, bulb, dimmer, and unsupported-module shapes. |
| Energy parsing | Prototype implemented/tested | Fixtures cover milli-unit success and unsupported response. |
| Outbox forwarding | Deferred | Establish device library/configuration and local status first; then use common outbox standard. |
| Local UI | Not implemented | Status/dashboard only initially. |
| Commands | Deferred | Safety design required. |
