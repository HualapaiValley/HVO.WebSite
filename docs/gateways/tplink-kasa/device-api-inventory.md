# TP-Link / Kasa Device API Inventory

This document is the per-device working inventory for the local HVO Kasa gateway. It combines current live HVO probe summaries, local fixtures/tests, and community references. It is not official TP-Link documentation.

Primary evidence used here:

- HVO read-only live probes saved under `/tmp/hvo-kasa-probes` during gateway validation.
- HVO parser fixtures and tests under `tests/HVO.Gateway.TplinkKasa.Tests`.
- `python-kasa` docs and source, especially `IotDevice`, `IotPlug`, `IotStrip`, `IotStripPlug`, `IotBulb`, `IotDimmer`, `Emeter`, `Schedule`, `Usage`, and `RuleModule`.
- `softScheck/tplink-smartplug` for legacy TCP `9999` XOR JSON framing and command families.
- `plasticrake/tplink-smarthome-api` and legacy `pyHS100` for device-family and command-surface comparison.

## Current Design Conclusion

The Kasa implementation should not be one monolithic card or one monolithic device class. The best fit is:

- a base legacy Kasa device snapshot and protocol client,
- feature/module records for switch state, child outlets, energy, schedules, bulbs, dimmers, diagnostics, and cloud/firmware metadata,
- model/capability descriptors selected by a factory from sysinfo plus probe results,
- shared UI card shell with type-specific bodies and details dialogs.

This mirrors the mature library pattern in `python-kasa`: `IotDevice` is the base, `IotPlug`, `IotStrip`, `IotStripPlug`, `IotBulb`, and `IotDimmer` specialize behavior, and modules/features provide cross-cutting capabilities such as energy, schedule, light, time, cloud, and dimmer metadata.

Recommended HVO shape:

| Concept | Recommended abstraction | Notes |
|---------|-------------------------|-------|
| Base device | `KasaDeviceSnapshot` plus `IKasaDeviceProfileDefinition` | Snapshot holds identity/status; the profile definition describes the model family and its module/control surface. |
| Feature modules | `IKasaSwitchModule`, `IKasaChildOutletModule`, `IKasaEnergyModule`, `IKasaScheduleModule`, `IKasaLightModule`, `IKasaDimmerModule`, `IKasaDiagnosticsModule` | Prefer records/interfaces over deep inheritance for HVO snapshots. Modules are reusable across model families. |
| Factory | `KasaDeviceProfileCatalog.Resolve(systemInfo)` | Maps model/shape evidence to concrete profiles: `EP25`, `HS105`, `HS200`, `HS210`, `HS220`, `HS300`, `KP200`, `KL130`, and `LB230`. |
| UI shell | common device card chrome | Name/status/health/last poll/error/refresh/details. |
| UI body | plug, strip, dual outlet, switch, dimmer, bulb bodies | Keeps cards accurate without condition-heavy markup. |
| Commands | command service behind identity validation and capability/safety checks | Still deferred for live writes. |

## Legacy Protocol Families Observed

All currently configured HVO devices in this inventory are legacy Kasa LAN responders using TCP `9999` with XOR-framed JSON. Current gateway support is read-only.

Common read-only modules:

| Module | Observed request examples | Main use | Notes |
|--------|---------------------------|----------|-------|
| `system` | `get_sysinfo`, `get_dev_icon`, `get_download_state` | Identity, device state, diagnostics | `get_sysinfo` is the controlling status read for all observed models. Legacy plug LED state is exposed as `get_sysinfo.led_off`; `system.get_led_off` is not reliable on HS105. |
| `emeter` | `get_realtime`, `get_daystat`, `get_monthstat`, `get_vgain_igain` | Power and energy | Supported by EP25 and HS300 in current probes; unsupported is normal on many switches/plugs. |
| `schedule` | `get_rules`, `get_next_action` | Schedule metadata | For non-bulb legacy plug/switch/strip families. |
| `count_down` | `get_rules` | Timer metadata | Supported on non-bulb legacy families in current probes. |
| `anti_theft` | `get_rules` | Away mode metadata | Supported on non-bulb legacy families in current probes. |
| `time` | `get_time`, `get_timezone` | Device-local time and timezone | Non-bulb namespace. |
| `cnCloud` | `get_info`, `get_intl_fw_list` | Cloud/firmware diagnostics | Sensitive; do not expose raw account/cloud values by default. |
| `smartlife.iot.common.*` | bulb cloud, timesetting, schedule, emeter | Bulb metadata | Bulbs use different namespaces for the same broad concepts. |
| `smartlife.iot.smartbulb.lightingservice` | `get_light_state`, `get_light_details`, `get_default_behavior` | Bulb light state/details and turn-on behavior | Write command `transition_light_state` remains blocked outside a future guarded bulb lab. |
| `smartlife.iot.dimmer` | `get_default_behavior`, `get_dimmer_parameters` | HS220 dimmer details | Production writes remain blocked; terminal-only lab allows bounded `set_brightness` with identity validation and readback restore. |

## Child Outlet Semantics

HS300 and KP200 are not single relay devices. Their status lives in `system.get_sysinfo.children[]`:

| Field | Meaning | Observed on |
|-------|---------|-------------|
| `children[].id` | vendor child outlet ID | HS300, KP200 |
| `children[].alias` | child outlet display name | HS300, KP200 |
| `children[].state` | child outlet on/off integer, `1` on and `0` off | HS300, KP200 |
| `children[].on_time` | child outlet on duration in seconds | HS300, KP200 |
| `children[].next_action.type` | next scheduled action marker | HS300, KP200 |

`python-kasa` confirms the best-practice semantics for strips:

- `IotStrip` represents the parent strip.
- `IotStripPlug` represents an individual socket.
- Parent strip on/off applies across child sockets.
- Child operations and child reads use request `context.child_ids`.
- Parent strip energy is an aggregate of child socket energy modules.

Important HVO gap: `KasaCommands.IsKnownReadOnly` currently rejects top-level `context`, so the gateway cannot yet send child-scoped read-only requests. The current gateway can read child state from sysinfo. Per-child energy and per-child schedule details need an allowlisted child-context read path before they can be verified live and shown accurately.

## Per-Device Inventory

### EP25(US) Plug

Observed live device: `Living Room TV`, model `EP25(US)`, hardware `1.0`, firmware `1.0.14 Build 240424 Rel.094105`.

Observed kind: `Plug`.

Current live capability summary: `Diagnostics`, `EnergyRealtime`, `LedState`, `ScheduleMetadata`, `SwitchState`; metadata includes `AwayModeRead`, `CountdownRead`, `Diagnostics`, `EnergyRealtime`, `EnergyTotal`, `FirmwareInfo`, `LedRead`, `ScheduleRead`.

Confirmed status/API shape:

| Area | Evidence | Card implication |
|------|----------|------------------|
| State | top-level `system.get_sysinfo.relay_state: 1`, `on_time`, `active_mode: none`, `next_action.type: -1` | Show one device-level on/off state and on-time. Do not show a fake numbered port unless the UI intentionally uses a generic outlet body for all relay devices. |
| Energy | `emeter.get_realtime` returns `power_mw`, `current_ma`, `voltage_mv`, and `total_wh`; live read was roughly 70-90 W, 122 V, 0.6-0.8 A, 468 kWh total | Show realtime W, V, A, and total kWh/Wh. This is a full plug meter, unlike HS105. |
| Energy history/calibration | `emeter.get_daystat`, `get_monthstat`, and `get_vgain_igain` supported | Details dialog can show history availability and calibration/gain diagnostics; do not expose reset/erase actions by default. |
| Schedule | `schedule.get_rules`, `schedule.get_next_action`, `count_down.get_rules`, and `anti_theft.get_rules` supported; live rule counts empty/off and next action `type: -1` | Show rule counts/availability in details, native app remains primary for edits. |
| Time/cloud/firmware | `time`, `timezone`, `cnCloud`, and firmware reads supported; `system.get_dev_icon` returned unsupported on the safe unit | Diagnostics only; avoid raw cloud/account values. Icon is optional. |
| Unsupported namespaces | bulb and dimmer namespaces returned module-not-supported errors | Do not treat EP25 as a bulb/dimmer or show light/color controls. |

Recommended card: plug card with state, energy summary, on-time, schedule availability, and diagnostics. No child outlet table and no bulb/dimmer controls.

### HS105(US) Plug

Observed kind: `Plug`.

Current live capability summary: `Diagnostics`, `LedState`, `ScheduleMetadata`, `SwitchState`; metadata includes `AwayModeRead`, `CountdownRead`, `Diagnostics`, `FirmwareInfo`, `LedRead`, `ScheduleRead`.

Confirmed status/API shape:

| Area | Evidence | Card implication |
|------|----------|------------------|
| State | top-level `relay_state` | Show one device-level on/off state. |
| Energy | `emeter` unsupported in current probes | Do not show energy gauges. Unsupported energy is not degraded. |
| Schedule | schedule/countdown/away reads supported | Show schedule metadata in details; write/edit behavior below is HS105 firmware 1.5.6-specific until each model and child outlet is validated. |
| LED status | `system.get_sysinfo.led_off`, `system.set_led_off` | Show LED support when `led_off` exists in sysinfo. |
| Time/cloud/firmware | supported diagnostics | Details only. |

Recommended card: compact plug card. No energy, no child outlet list.

Live write validation, Desk Lamp HS105 plug, 2026-06-06:

| Step | Command shape | Observed result |
|------|---------------|-----------------|
| Baseline read | `system.get_sysinfo` | Model validated as `HS105(US)` with top-level `relay_state`. Starting state was On. |
| Turn off | `system.set_relay_state` with `{ "state": 0 }` | Write round trip about 552 ms; readback reached Off in about 338 ms. |
| Turn on | `system.set_relay_state` with `{ "state": 1 }` | Write round trip about 656 ms; readback reached On in about 274 ms. |
| Gateway verification | authenticated `/devices/{sourceId}` | Device was online, not degraded, and back On after the cycle. |
| Alias | `system.set_dev_alias` | Temporary alias `Desk Lamp Lab` set with `err_code: 0`; readback confirmed it, then alias restored to `Desk Lamp` with `err_code: 0`. |
| LED read/write | Read `system.get_sysinfo.led_off`; write `system.set_led_off` with `{ "off": 0|1 }` | `system.get_led_off` returned `err_code: -2`, but it is the wrong read path for this firmware. Live LED-only test started with `led_off=0`, wrote `off=1` with `err_code: 0`, read back `led_off=1`, restored `off=0` with `err_code: 0`, and read back `led_off=0`. This matches the Kasa app LED Status toggle and `python-kasa`'s `Led` module behavior. |
| Time | `time.get_time`, `time.get_timezone`, `time.set_time`, `time.set_timezone` | Device reported timezone index `7`; setting current device time and setting the same timezone index both returned `err_code: 0`. |
| Meters | `emeter.get_realtime`, `emeter.erase_emeter_stat`, `emeter.erase_runtime_stat` | Realtime energy returned `err_code: -1`; energy erase returned `err_code: -1`; runtime erase returned `err_code: -2`. Meter/reset commands are not supported on this non-energy HS105. |
| Rules read | `schedule.get_rules`, `schedule.get_next_action`, `count_down.get_rules`, `anti_theft.get_rules` | Schedule, countdown, and away rule reads returned `err_code: 0` with zero rules; countdown and away `get_next_action` returned `err_code: -2`. |
| Absolute schedule attempt | `schedule.add_rule` with observed legacy plug rule shape | Returned `err_code: -3`; no rule was created, and the relay stayed On through the 95 second watch. |
| Countdown timer | `count_down.add_rule` with `{ "name": "HVO Lab", "enable": 1, "delay": 30, "act": 0 }` | Returned `err_code: 0`; rule readback showed one generated rule ID; relay turned Off after about 30.5 seconds; `count_down.delete_rule` returned `err_code: 0`; relay was restored On. |
| Reboot | `system.reboot` with `{ "delay": 1 }` | Returned `err_code: 0`; TCP connectivity dropped after about 6.5 seconds and recovered after about 16.9 seconds. Relay stayed On across reboot and RSSI returned around `-43`. |
| Final verification | `system.get_sysinfo` | Alias was restored to `Desk Lamp`, relay was On, and RSSI was about `-43`. |

Follow-up inspection after a Kasa-app-created one-time schedule:

| Area | Command shape | Observed result |
|------|---------------|-----------------|
| Kasa app one-time schedule | `schedule.get_rules` | One rule existed with generated ID, `name: "name"`, `enable: 1`, `repeat: 0`, `wday: [0,0,0,0,0,0,1]`, `sact: 1`, `stime_opt: 0`, `smin: 1095`, `soffset: 0`, `eact: -1`, `etime_opt: -1`, `emin: 0`; `schedule.get_next_action` returned `err_code: 0`, `type: 1`. |
| Runtime metrics | `schedule.get_daystat`, `schedule.get_monthstat` | Runtime is available through the `schedule` module, not `emeter`. Current read returned `day_list` and `month_list` minute totals; today's usage was about 910 minutes, current-month usage about 8111 minutes. This matches the Kasa app runtime panels conceptually: today, recent days, and month totals can be computed from these lists. |
| Pre-armed countdown On recovery | `count_down.add_rule` with `{ "delay": 30, "act": 1 }`, then `system.set_relay_state` Off | Did not restore the relay within 50 seconds. The plug stayed reachable over Wi-Fi, but relay stayed Off until manually restored. |
| Pre-armed one-time schedule On recovery | `schedule.add_rule` using the observed one-time fields with `sact: 1`, then `system.set_relay_state` Off | Did not restore the relay within 95 seconds. The plug stayed reachable over Wi-Fi, but relay stayed Off until manually restored. |
| Scheduled Off/On pair | Two `schedule.add_rule` requests using the observed one-time fields, Off at +2 minutes and On at +3 minutes | Both adds returned `err_code: -3`; no rule IDs were created, `schedule.get_rules` stayed empty, and the relay stayed On. This did not exercise the scheduler because the device rejected the local schedule writes. |
| App schedule round-trip | App-created OFF 3:50 PM and ON 3:55 PM rules on W/T/F/S, then local `schedule.delete_rule` and local `schedule.add_rule` with the exact app shape | Initial read showed two rules: `name: "name"`, `repeat: 1`, `wday: [0,0,0,1,1,1,1]`, OFF `sact: 0`, `smin: 950`; ON `sact: 1`, `smin: 955`; both had `stime_opt: 0`, `soffset: 0`, `eact: -1`, `etime_opt: -1`, `emin: 0`. Deleting both matched IDs returned `err_code: 0`; rule count went to `0`. Re-adding both with `name: "name"` and `repeat: 1` returned `err_code: 0`; rule count returned to `2`. |
| App schedule execution | Recreated W/T/F/S OFF 3:50 PM and ON 3:55 PM schedules | The 3:50 PM rule turned the relay Off. A read-only watch observed the 3:55 PM rule turn the relay back On after about 29.4 seconds of polling. No relay write was sent by the watch. |
| App one-time schedule, enabled | OFF at 4:30 PM, one time only | Device rule shape was `name: "name"`, `enable: 1`, `repeat: 0`, `wday: [0,0,0,0,0,0,1]`, `sact: 0`, `stime_opt: 0`, `smin: 990`, `soffset: 0`, `eact: -1`, `etime_opt: -1`, `emin: 0`. |
| App one-time schedule, disabled | OFF at 5:40 PM, one time only, disabled | Device rule shape was the same one-time shape except `enable: 0` and `smin: 1060`. Disabled rules remain in `schedule.get_rules` but should not become the next active action. |
| Schedule delete all | Local `schedule.delete_rule` for the two repeating schedules and two one-time schedules | Four deletes returned `err_code: 0`; `schedule.get_rules` returned `count=0`; `schedule.get_next_action` returned `err_code: 0`, `type: -1`. Relay stayed On. |
| Kasa app timer object, inactive | `count_down.get_rules` after app timer use | A device-resident rule remained: name `Timer AddTimerObject`, `enable: 0`, `delay: 0`, `act: 1`; `count_down.get_next_action` returned `err_code: -2` once inactive. This proves the timer is not purely application-side after creation; the app writes countdown state to the device. |
| Kasa app timer object, active | App-created timer to turn the plug Off in 1 hour 30 minutes | `count_down.get_rules` returned one rule with `name: "Timer AddTimerObject"`, generated `id`, `enable: 1`, `delay: 5400`, `remain: 5324`, and `act: 0`. No `repeat`, `wday`, `sact`, or schedule start/end fields were present. `schedule.get_next_action` simultaneously returned `err_code: 0`, `type: 2`, `action: 0`, `schd_time: 63657`, so this firmware exposes the active timer's pending action through the schedule next-action API, not through `count_down.get_next_action`. |
| App-shaped countdown On add | `count_down.add_rule` with `{ "name": "Timer AddTimerObject", "enable": 1, "delay": 60, "act": 1 }`, then `system.set_relay_state` Off | Returned `err_code: -10` while one disabled app timer object already existed. Rule count stayed `1`, no new rule ID appeared, `get_next_action` returned `err_code: -2`, and the relay did not turn On within 90 seconds. The existing app timer object was not deleted by the lab cleanup. |
| App-shaped countdown Off edit | `count_down.edit_rule` on existing `Timer AddTimerObject` ID with `{ "name": "Timer AddTimerObject", "enable": 1, "delay": 120, "act": 0 }` | Returned `err_code: 0`; readback showed the same single timer slot with `enable: 1`, `delay: 120`, `remain: 119`, `act: 0`; `schedule.get_next_action` returned `err_code: 0`, `type: 2`, `action: 0`, `schd_time: 59060`. The relay turned Off after about 122.9 seconds, then the lab restored it On. |
| App-shaped countdown On edit | `count_down.edit_rule` on existing `Timer AddTimerObject` ID with `{ "name": "Timer AddTimerObject", "enable": 1, "delay": 120, "act": 1 }`, then `system.set_relay_state` Off | Edit returned `err_code: 0`; immediate readback showed `enable: 1`, `delay: 120`, `remain: 119`, `act: 1`, and `schedule.get_next_action` returned `type: 2`, `action: 1`. After the manual Off write, independent snapshots showed `active_mode: "none"`, `enable: 0`, `delay: 0`, `remain: 0`, `act: 1`, `schedule.get_next_action.type: -1`, and relay still Off. The timer did not restore relay On within 150 seconds; the lab restored it On. |
| App-shaped countdown On edit, Off first | `system.set_relay_state` Off, then `count_down.edit_rule` on existing `Timer AddTimerObject` ID with `{ "name": "Timer AddTimerObject", "enable": 1, "delay": 120, "act": 1 }` | Returned `err_code: 0`; readback showed the same single timer slot with `enable: 1`, `delay: 120`, `remain: 119`, `act: 1`; `schedule.get_next_action` returned `err_code: 0`, `type: 2`, `action: 1`, `schd_time: 59558`. The relay restored On after about 122.0 seconds. This validates that the previous On failure was caused by turning the relay Off after arming the timer, which cancels or consumes the active timer on this firmware. |
| Diagnostic reads | `system.get_dev_icon`, `system.get_download_state`, `cnCloud.get_info`, `cnCloud.get_intl_fw_list` | `get_dev_icon` returned `err_code: -2`; download state returned `err_code: 0` with keys `flash_time`, `ratio`, `reboot_time`, and `status`; cloud info returned `err_code: 0` with cloud/account/status keys; firmware list returned `err_code: 0` with `fw_list`. The dashboard should summarize presence/status and avoid raw cloud/account values. |
| Away mode write attempt | `anti_theft.add_rule` with the schedule-like app legacy fields | Returned `err_code: -3`; rule count stayed `0`; `anti_theft.get_next_action` returned `err_code: -2`. Away mode should remain read-only/unsupported for HS105 local writes until a real Kasa-app away rule is captured. |
| Local one-time schedule add | `schedule.add_rule` with app-observed one-time shape, `repeat: 0` | Returned `err_code: -3`; no rule ID was created; `schedule.get_next_action` stayed `type: -1`; no relay change occurred. This firmware accepts the app's one-time rules when the app creates them, but rejects the same local one-time add shape through the legacy API path used by the lab. |
| App-slot countdown reboot persistence | Existing `Timer AddTimerObject` edited with `{ "enable": 1, "delay": 120, "act": 0 }`, then `system.reboot` | Naive `count_down.add_rule` still returned `err_code: -10` because the app timer slot already existed. Editing the existing slot returned `err_code: 0`; independent snapshots showed `active_mode: "count_down"`, `schedule.get_next_action.type: 2`, and remaining time decreasing after reboot. The timer survived reboot and turned the relay Off after about 168.8 seconds; the lab restored it On. |
| Repeated schedule reboot persistence | Two local `schedule.add_rule` requests using the exact repeated app shape, Off at +3 minutes and On at +4 minutes, then `system.reboot` | Adds returned `err_code: 0` in the corrected lab flow. After reboot, read-only snapshots showed `active_mode: "schedule"`, both repeated rules still present, relay Off after the Off rule, and `schedule.get_next_action` pointing to the On rule. A later cleanup baseline observed the relay back On with both rules still present, then `schedule.delete_rule` removed both rules with `err_code: 0`. The schedule behavior survived reboot, but the current CLI target did not return a final summary and had to be stopped manually; treat that as a lab-tool cleanup issue, not a device scheduling failure. |

Validation constraints:

- The lab command validated configured device ID, expected model, and MAC before sending writes.
- No network, MAC, cloud bind/unbind, Wi-Fi, factory reset, or raw arbitrary commands were sent.
- The live test used a CLI-only guarded lab path, not a public web write endpoint.
- A simple relay plug can be controlled with one JSON command and then verified by a fresh `system.get_sysinfo` read. A readback window around 1-3 seconds is conservative for this local device; this run observed sub-second write and readback timings.
- Reboot is an operating-system/device reboot, not a relay power cycle. Local TCP connectivity drops and reconnects; the load relay stayed On during the observed reboot.
- Schedule, runtime, and timer write shapes in this section are validated only on the Desk Lamp HS105(US), hardware 1.0, firmware 1.5.6. They are likely related across legacy non-bulb devices, but HVO should not assume the same fields, slot rules, or per-child scope for HS200/HS220/KP200/HS300 without per-model and child-context validation.
- For this HS105 firmware, countdown Off through `count_down.add_rule` and app-slot countdown Off through `count_down.edit_rule` are confirmed HVO-created one-shot automation paths.
- Scheduled events themselves work when created by the Kasa app and are visible through `schedule.get_rules` / `schedule.get_next_action`. Local schedule writes also work when using the app's literal legacy shape for this repeated weekday schedule: `name: "name"`, `repeat: 1`, seven-value `wday`, `stime_opt: 0`, `soffset: 0`, `eact: -1`, `etime_opt: -1`, and `emin: 0`. Earlier local writes failed because they used non-app names and/or a one-time shape that this firmware rejected.
- The Kasa app timer is device-resident, not purely application-side: it uses a single `count_down` rule named `Timer AddTimerObject`. Inactive rules may stay as `enable: 0`, `delay: 0` or preserve the previous delay after firing; active rules use the same name with `enable: 1`, original `delay`, current `remain`, and `act`. Starting the existing app timer slot works with `count_down.edit_rule`; naive `count_down.add_rule` returned `err_code: -10` while the disabled timer object existed.
- Both the repeated schedule path and the app-slot countdown path are device-resident enough to survive `system.reboot` on this HS105 firmware. The repeated schedule path is suitable for modeled recurring schedules only after per-model validation; the app-slot countdown path is suitable for one active app-style timer slot, not multiple independent timers.
- Pre-arming an On timer before turning the plug Off is not a safe recovery pattern on this HS105. Off countdown worked, app-slot Off edit worked, and app-slot On edit also worked when the relay was already Off before arming the timer. By contrast, On countdown, app-shaped On add, app-shaped On edit followed by manual Off, and HVO-created one-time On schedule did not restore the relay in live tests. The app-slot On edit appears to be cancelled or consumed when the relay is manually turned Off after arming: the device immediately returns to `enable: 0`, `remain: 0`, `active_mode: "none"`, and no next action.

### HS200(US) Switch

Observed kind: `Switch`.

Current live capability summary: `Diagnostics`, `ScheduleMetadata`, `SwitchState`; metadata includes `AwayModeRead`, `CountdownRead`, `Diagnostics`, `FirmwareInfo`, `ScheduleRead`.

Live device validated: Front Door Outside Light, HS200(US), hardware 1.0, firmware 1.2.6 Build 200727 Rel.121953.

Confirmed status/API shape:

| Area | Evidence | Card implication |
|------|----------|------------------|
| State | top-level `relay_state` | Show switch on/off state, not an outlet row. |
| Energy | no supported realtime energy in current probes | Hide energy. |
| Schedule | schedule/countdown/away reads supported; existing user schedules were preserved during the live lab | Details dialog should show schedule/countdown availability and rule counts without presenting destructive editing by default. |
| Runtime | `schedule.get_daystat` and `schedule.get_monthstat` supported | Runtime belongs in switch details, not in an energy panel. |
| LED status | `system.get_sysinfo.led_off`, `system.set_led_off` | Show LED support when `led_off` exists in sysinfo. |
| Icon | `system.get_dev_icon` returned `err_code: 0` with `hash` and `icon` | Diagnostic detail only. |
| Cloud/firmware | `cnCloud.get_info` and `cnCloud.get_intl_fw_list` supported; download state returned `err_code: -7` | Summarize diagnostics only; avoid raw cloud/account values. |

Recommended card: wall-switch card with state and schedule metadata. Avoid plug/outlet language.

Live write validation, Front Door Outside Light HS200 switch, 2026-06-07:

| Step | Command shape | Observed result |
|------|---------------|-----------------|
| Baseline read | `system.get_sysinfo` | Model validated as `HS200(US)` with top-level `relay_state`; alias was `Front Door Outside Light`; starting state was On; RSSI was around `-42` to `-45`. |
| Existing schedules | `schedule.get_rules`, `schedule.get_next_action` | Two user schedules existed and were preserved: all-week On at `smin: 1020` (17:00) and all-week Off at `smin: 360` (06:00), both `name: "name"`, `repeat: 1`, `wday: [1,1,1,1,1,1,1]`, `eact: -1`, `etime_opt: -1`, and `day/month/year: 0`. Current `get_next_action` returned `type: -1` outside an active pending event window. |
| Runtime metrics | `schedule.get_daystat`, `schedule.get_monthstat` | Runtime stats are available through `schedule`; observed recent totals included `last7_min=4282` and `this_month_min=4282`. |
| Diagnostics | `system.get_dev_icon`, `system.get_download_state`, `cnCloud.get_info`, `cnCloud.get_intl_fw_list` | Icon read returned `err_code: 0` with `hash` and `icon`; download state returned `err_code: -7`; cloud and firmware reads returned `err_code: 0`. This differs from the HS105, where icon returned `-2` and download returned `0`. |
| LED read/write | Read `system.get_sysinfo.led_off`; write `system.set_led_off` | Started `led_off=0`; setting `off=1` returned `err_code: 0` and read back `led_off=1`; restoring `off=0` returned `err_code: 0` and read back `led_off=0`. |
| Alias | `system.set_dev_alias` | Temporary alias set and restore both returned `err_code: 0`; final alias was restored to `Front Door Outside Light`. |
| Time | `time.get_time`, `time.get_timezone`, `time.set_time`, `time.set_timezone` | Device reported timezone index `7`; `time.set_time` returned `err_code: -2`; setting the same timezone returned `err_code: 0`. This differs from HS105 firmware 1.5.6, where both writes returned `0`. |
| Meters | `emeter` reads and reset attempts | Energy/meter support was absent or unsupported for this switch; do not show energy fields. |
| Schedule write attempt | `schedule.add_rule` with an HVO-named near-future rule | Returned `err_code: -3`; no rule ID was created; no relay fire occurred; existing user schedules stayed intact. Destructive schedule-clear and round-trip targets were intentionally avoided on this real front-door schedule. |
| Countdown Off | `count_down.add_rule` with `{ "name": "HVO Lab", "enable": 1, "delay": 30, "act": 0 }` | Returned `err_code: 0`; a generated rule ID appeared; relay turned Off after about 31.1 seconds; `count_down.delete_rule` returned `err_code: 0`; relay was restored On. |
| Reboot | `system.reboot` with `{ "delay": 1 }` | Returned `err_code: 0`; TCP connectivity dropped after about 6.2 seconds and recovered after about 25.8 seconds; relay was On after reboot. |
| Plain countdown On after manual Off | `count_down.add_rule` with `{ "delay": 60, "act": 1 }`, then `system.set_relay_state` Off | Add returned `err_code: 0` and a new rule ID appeared, but `count_down.get_next_action` returned `err_code: -2`; it did not restore On within 90 seconds after manual Off. The rule was deleted and the relay was restored On. |
| App-shaped countdown Off | `count_down.add_rule` for `Timer AddTimerObject`, delay 120, act 0 | With no existing app timer slot, add returned `err_code: 0`; readback showed one slot with `enable: 1`, `delay: 120`, `remain: 119`, `act: 0`; `schedule.get_next_action` returned `type: 2`, `action: 0`; relay turned Off after about 121.4 seconds; final state was restored On. |
| App-shaped countdown On after manual Off | `count_down.edit_rule` for existing `Timer AddTimerObject`, delay 120, act 1, then manual Off | Existing slot edit returned `err_code: 0`; readback showed `enable: 1`, `delay: 120`, `remain: 119`, `act: 1`; `schedule.get_next_action` returned `type: 2`, `action: 1`; after manual Off, relay restored On after about 17.1 seconds. This differs from the HS105, where the same sequence appeared to cancel or consume the timer. |
| App-shaped countdown On while already Off | `system.set_relay_state` Off, then `count_down.edit_rule` for existing `Timer AddTimerObject`, delay 120, act 1 | Initial Off write/readback succeeded; edit returned `err_code: 0`; `schedule.get_next_action` returned `type: 2`, `action: 1`; relay restored On after about 119.9 seconds. |
| Countdown reboot persistence | HVO-named countdown and app-slot countdown with `system.reboot` | The HVO-named reboot path was not reliable while the app timer slot existed and the CLI reached the app-slot phase with the relay still On and only an inactive `Timer AddTimerObject` slot. The app-slot countdown Off survived reboot: an independent snapshot after reboot showed `active_mode: "count_down"`, `Timer AddTimerObject` with `enable: 1`, `delay: 180`, `remain: 134`, `act: 0`, and `schedule.get_next_action.type: 2`. The timer later fired Off and reset to inactive `enable: 0`, `delay: 0`. The CLI target hit its overall timeout before cleanup, so the guarded `on` target restored the relay; final snapshot confirmed relay On, two user schedules preserved, and only the inactive app timer slot remained. |

HS200-specific constraints and design conclusions:

- Treat HS200 as a wall switch, not a plug. The dashboard should avoid outlet language and should not show energy.
- The LED toggle is available through sysinfo `led_off`, same broad UI affordance as HS105 but validated separately.
- Runtime is available through the `schedule` module and belongs in details alongside schedule/timer status.
- Existing user schedules must be treated as first-class device data. The live lab preserved both real all-week schedules and avoided destructive schedule-clear/round-trip tests.
- The Kasa app timer slot is device-resident on HS200. Active app-slot timers appear through `count_down.get_rules` and simultaneously surface as `schedule.get_next_action` with `type: 2`.
- Countdown Off and app-slot countdown Off are confirmed automation paths. Plain countdown On after manual Off is not a safe recovery pattern. App-slot countdown On worked both from an already-Off state and, unlike HS105, after arming while On and then manually turning Off.
- App-slot countdown Off survived `system.reboot`, but the current lab target needs a timeout budget longer than two 180-second phases plus reboot overhead. The lab cleanup path was updated to restore relay On with an uncanceled short cleanup token if the overall lab token has expired.

### HS210(US) Three-Way Switch

Observed kind: `ThreeWaySwitch`.

Current live capability summary: `Diagnostics`, `ScheduleMetadata`, `SwitchState`; metadata includes `AwayModeRead`, `CountdownRead`, `Diagnostics`, `FirmwareInfo`, `ScheduleRead`.

Live device validated: Stairway Light, HS210(US), hardware 1.0, firmware 1.5.8 Build 191118 Rel.135937.

Confirmed status/API shape:

| Area | Evidence | Card implication |
|------|----------|------------------|
| Identity | `system.get_sysinfo.dev_name` is `Smart Wi-Fi 3-Way Light Switch`; `model` is `HS210(US)`; `feature` is `TIM`; `mic_type` is `IOT.SMARTPLUGSWITCH` | Label as a three-way switch from model/dev name, but do not infer paired-device topology from local API fields. |
| State | top-level `relay_state`, `on_time`, and `next_action`; no `children[]`, traveler, peer, or paired-switch fields observed in sysinfo | Show switch state. Treat local control as a single relay until more three-way installations can be compared. |
| Energy | `emeter` unsupported in live exercise | Hide energy. |
| Schedule | schedule/countdown/away reads supported; no user rules were present on this device | Details dialog can show schedule/timer availability and counts. |
| Runtime | `schedule.get_daystat` and `schedule.get_monthstat` supported, all observed totals zero | Runtime belongs in switch details, not in an energy panel. |
| LED status | `system.get_sysinfo.led_off`, `system.set_led_off` | Show LED support when `led_off` exists in sysinfo. |
| Diagnostics | `system.get_dev_icon` returned `err_code: -2`; `system.get_download_state`, `cnCloud.get_info`, and `cnCloud.get_intl_fw_list` returned `err_code: 0` | Diagnostic detail only; behavior matches HS105 more closely than HS200 for icon/download reads. |

Recommended card: three-way wall-switch card with state, LED status, schedule/timer metadata, and a small three-way badge or label. Avoid energy and outlet language.

Live write validation, Stairway Light HS210 three-way switch, 2026-06-07:

| Step | Command shape | Observed result |
|------|---------------|-----------------|
| Baseline read | `system.get_sysinfo` | Model validated as `HS210(US)` with top-level `relay_state`; alias was `Stairway Light`; starting state was Off; RSSI around `-40` to `-42`. |
| Three-way identity scan | `system.get_sysinfo` raw key inspection | Keys were the normal legacy single-relay shape: `relay_state`, `on_time`, `next_action`, `led_off`, `active_mode`, and diagnostics fields. No local field exposed traveler state, companion switch identity, paired endpoint, or physical load truth separate from `relay_state`. |
| Rule inspection | `schedule.get_rules`, `schedule.get_next_action`, `count_down.get_rules`, `anti_theft.get_rules` | Schedule returned `err_code: 0`, `enable: 1`, `version: 2`, and zero rules; schedule next returned `type: -1`. Countdown and away rule reads returned zero rules; countdown/away next-action returned `err_code: -2`. |
| Runtime metrics | `schedule.get_daystat`, `schedule.get_monthstat` | Runtime stats are available through `schedule`; observed six day/month entries with `last7_min=0` and `this_month_min=0`. |
| Diagnostics | `system.get_dev_icon`, `system.get_download_state`, `cnCloud.get_info`, `cnCloud.get_intl_fw_list` | Icon read returned `err_code: -2`; download state returned `err_code: 0` with flash/ratio/reboot/status keys; cloud and firmware reads returned `err_code: 0`. |
| LED read/write | Read `system.get_sysinfo.led_off`; write `system.set_led_off` | Started `led_off=0`; setting `off=1` returned `err_code: 0` and read back `led_off=1`; restoring `off=0` returned `err_code: 0` and read back `led_off=0`. |
| Alias | `system.set_dev_alias` | Temporary alias `Stairway Light Lab` set and restored with `err_code: 0`; final alias was `Stairway Light`. |
| Time | `time.get_time`, `time.get_timezone`, `time.set_time`, `time.set_timezone` | Device reported timezone index `7`; setting current device time and setting the same timezone both returned `err_code: 0`. |
| Meters | `emeter.get_realtime`, `emeter.erase_emeter_stat`, `emeter.erase_runtime_stat` | Realtime energy returned `err_code: -1`; energy erase returned `err_code: -1`; runtime erase returned `err_code: -2`. Meter/reset commands are unsupported on this non-energy switch. |
| Schedule write attempt | `schedule.add_rule` with an HVO-named near-future rule | Returned `err_code: -3`; no rule ID was created; no relay fire occurred; schedule rule count stayed zero. |
| Countdown Off | `count_down.add_rule` with `{ "name": "HVO Lab", "enable": 1, "delay": 30, "act": 0 }` | Returned `err_code: 0`; a generated rule ID appeared; `schedule.get_next_action` surfaced the pending action as `type: 2`, `action: 0`; relay turned Off after about 30.7 seconds; `count_down.delete_rule` returned `err_code: 0`; relay was restored On by the lab. |
| Reboot | `system.reboot` with `{ "delay": 1 }` | Returned `err_code: 0`; TCP connectivity dropped after about 6.6 seconds and recovered after about 9.9 seconds; relay was On after reboot. |
| Plain countdown On after manual Off | `count_down.add_rule` with `{ "delay": 60, "act": 1 }`, then `system.set_relay_state` Off | Add returned `err_code: 0` and a new rule ID appeared, but `count_down.get_next_action` returned `err_code: -2`; it did not restore On within 90 seconds after manual Off. The rule was deleted and the relay was restored On. |
| App-shaped countdown Off | `count_down.add_rule` for `Timer AddTimerObject`, delay 120, act 0 | With no existing app timer slot, add returned `err_code: 0`; readback showed one slot with `enable: 1`, `delay: 120`, `remain: 119`, `act: 0`; `schedule.get_next_action` returned `type: 2`, `action: 0`; relay turned Off after about 119.9 seconds; final state was restored On. |
| App-shaped countdown On after manual Off | `count_down.edit_rule` for existing `Timer AddTimerObject`, delay 120, act 1, then manual Off | Existing slot edit returned `err_code: 0`; readback showed `enable: 1`, `delay: 120`, `remain: 119`, `act: 1`; `schedule.get_next_action` returned `type: 2`, `action: 1`. After manual Off, the slot collapsed to inactive `enable: 0`, `delay: 0`, `remain: 0`; relay did not restore On within 150 seconds. This matches HS105 behavior and differs from the observed HS200 run. |
| App-shaped countdown On while already Off | `system.set_relay_state` Off, then `count_down.edit_rule` for existing `Timer AddTimerObject`, delay 120, act 1 | Initial Off write/readback succeeded; edit returned `err_code: 0`; `schedule.get_next_action` returned `type: 2`, `action: 1`; relay restored On after about 122.6 seconds. |
| Final verification | Guarded Off restore plus read-only snapshot | Relay was restored to original Off state; alias was `Stairway Light`; schedule rule count was `0`; `schedule.get_next_action` returned `type: -1`; one inactive `Timer AddTimerObject` slot remained with `enable: 0`, `delay: 0`, `remain: 0`, `act: 1`. |

HS210-specific constraints and design conclusions:

- The local legacy API exposes HS210 as a top-level single-relay switch with a three-way model/dev-name identity. This run did not expose traveler state, companion switch identity, or a separate physical load state.
- Revisit HS210 when more three-way installations are available. The open question is whether `relay_state` always maps to load truth in different wiring/pairing scenarios or whether it can diverge from physical light state when a companion/manual switch changes the circuit.
- For HVO card design today, use a three-way wall-switch profile: switch state, LED status, schedule/timer status, runtime details, diagnostics, and no energy/outlet UI.
- The Kasa app timer slot is device-resident on HS210. Active app-slot timers appear through `count_down.get_rules` and simultaneously surface through `schedule.get_next_action` with `type: 2`.
- Countdown Off and app-slot countdown Off are confirmed automation paths. Plain countdown On after manual Off is not a safe recovery pattern. App-slot countdown On works when the relay is already Off before arming the timer; arming while On and then manually turning Off appears to cancel or consume the active timer, matching HS105 rather than HS200.

### HS220(US) Dimmer

Observed live device: ` Bedroom Light`, model `HS220(US)`, hardware `1.0`, firmware `1.5.11 Build 200214 Rel.152651`, MAC `D8:0D:17:2B:79:A0`.

Observed kind: `Dimmer`.

Current live capability summary: `Diagnostics`, `Dimming`, `ScheduleMetadata`, `SwitchState`; metadata includes `AwayModeRead`, `CountdownRead`, `Diagnostics`, `DimmerRead`, `FirmwareInfo`, `ScheduleRead`.

Live read-only status/API shape:

| Area | Live evidence | Card implication |
|------|---------------|------------------|
| State | `system.get_sysinfo.relay_state: 0`, `on_time: 0` | Show wall-switch on/off state. |
| Brightness | `system.get_sysinfo.brightness: 100`; guarded `set_brightness` test changed it to `40` and restored `100` while relay stayed Off | Show brightness percentage as primary dimmer state. Future controls must restore/read back by `sysinfo.brightness`. |
| Presets | `system.get_sysinfo.preferred_state`: index `0=100`, `1=75`, `2=50`, `3=25` | Show presets in details; do not assume these are schedules or child outlets. |
| Dimmer behavior | `smartlife.iot.dimmer.get_default_behavior`: soft/hard `last_status`, double-click `gentle_on_off`, long-press `instant_on_off` | Details should summarize physical button behavior separately from automation schedules. |
| Dimmer parameters/calibration | `smartlife.iot.dimmer.get_dimmer_parameters`: `bulb_type: 1`, `fadeOnTime: 1000`, `fadeOffTime: 1000`, `gentleOnTime: 3000`, `gentleOffTime: 10000`, `minThreshold: 23`, `rampRate: 30` | Details should show bulb type, fade/gentle timings, minimum threshold, and ramp rate. Treat threshold/ramp as calibration/configuration, not routine telemetry controls. |
| Schedule | `schedule.get_rules`: `err=0`, `enable=1`, `version=2`, count `0`; `schedule.get_next_action`: `err=0`, `type=-1` | Details can show schedule module enabled with no active rules. No card schedule badge unless rules/next action exist. |
| Countdown | `count_down.get_rules`: `err=0`, count `0`; `count_down.get_next_action`: `err=-2` | Hide active countdown unless rules exist. |
| Away mode | `anti_theft.get_rules`: `err=0`, `enable=0`, `version=2`, count `0`; `get_next_action`: `err=-2` | Hide away mode unless enabled/rules exist. |
| Runtime | `schedule.get_daystat`/`get_monthstat`: `err=0`, count `6`; live run showed last-7/current-month minutes | Details can include runtime stats, but do not label as energy. |
| Energy | `emeter` unsupported | Hide energy/current/voltage/cost UI. |
| Diagnostics | `system.get_dev_icon` unsupported with `err=-2`; `system.get_download_state`, `cnCloud.get_info`, and `cnCloud.get_intl_fw_list` supported | Diagnostics panel only; icon should be optional. |

Additional probes against likely dimmer/light names returned `err_code: -2`: `get_calibration`, `get_dimmer_transition`, `get_led_params`, `get_preset_rules`, and `get_light_state`. The live local legacy surface for this firmware appears to expose presets through `system.get_sysinfo.preferred_state`, behavior through `get_default_behavior`, and calibration-like values through `get_dimmer_parameters`.

Recommended card: dimmer card with state, brightness, and last poll/health as primary data. Details can include preset percentages, default behavior, fade/gentle timings, minimum threshold, ramp rate, schedule/countdown/away diagnostics, runtime minutes, and firmware/cloud diagnostics. Do not treat HS220 as a bulb, outlet, or energy meter: it controls a wall load, has no hue/color-temperature surface, and has no per-port/child state.

### KP200(US) Dual Outlet

Observed live device: `N Roof AllSkyCamera`, model `KP200(US)`, hardware `1.0`, firmware `1.0.9 Build 200618 Rel.140140`, two child sockets.

Observed kind: `DualOutlet`.

Current live capability summary: `ChildOutlets`, `Diagnostics`, `ScheduleMetadata`; metadata includes `AwayModeRead`, `CountdownRead`, `Diagnostics`, `FirmwareInfo`, `ScheduleRead`.

Confirmed status/API shape:

| Area | Evidence | Card implication |
|------|----------|------------------|
| Parent state | no top-level `relay_state`; derive from child states | Parent card state is on when any child outlet is on, off when all known child outlets are off. |
| Child state | `children[]` with `id`, `alias`, `state`, `on_time`, `next_action.type` | Show two named outlet rows with individual state and on-time. |
| Energy | parent and child-context `emeter.get_realtime`, `get_daystat`, and `get_monthstat` returned `err_code: -1` | Hide energy/current/voltage/cost UI for KP200. Do not inherit HS300 per-child energy behavior. |
| Schedule | parent namespace `schedule.get_rules` and `schedule.get_next_action` supported; live rules empty and next action `type: -1` | Details can show parent-level schedule availability without implying active rules. |
| Per-child schedule | child-context `schedule.get_rules`, `schedule.get_next_action`, `count_down.get_rules`, and `anti_theft.get_rules` succeeded for both child sockets | Treat schedule/countdown/away metadata as per-socket capable. UI should keep parent and child automation scopes separate. |
| Diagnostics | time, timezone, firmware download, cloud info, cloud firmware list, and LED state are supported or structured; device icon returned unsupported | Keep diagnostics in details; do not surface private cloud identity values. |

Answer to the KP200 scope question: KP200 is child-socket scoped for schedule/countdown/away reads, but not for energy on the live unit. The parent card state must be derived from the two children, and the details view should not merge parent and child automation into one count.

Recommended card: dual-outlet card with parent health/state, two outlet rows, individual outlet state/on-time, and no energy section. Details should separate parent schedule/countdown/away metadata from per-outlet schedule/countdown/away metadata once the UI has a scoped automation view.

### HS300(US) Power Strip

Observed live device: model `HS300(US)`, hardware `1.0`, firmware `1.0.21 Build 210524 Rel.161309`, six child sockets.

Observed kind: `PowerStrip`.

Current live capability summary: `ChildOutlets`, `Diagnostics`, `EnergyRealtime`, `ScheduleMetadata`; metadata includes `AwayModeRead`, `CountdownRead`, `Diagnostics`, `EnergyRealtime`, `EnergyTotal`, `FirmwareInfo`, `LedRead`, `ScheduleRead`.

Confirmed status/API shape:

| Area | Evidence | Card implication |
|------|----------|------------------|
| Parent state | no top-level `relay_state`; derive from child states | Parent card state is on when any child outlet is on, off when all known children are off. |
| Child state | six `children[]` entries with `id`, `alias`, `state`, `on_time`, `next_action.type` | Show six named outlet rows with individual state/on-time. |
| Parent energy | parent `emeter.get_realtime` succeeds; live read showed aggregate-style values including power, voltage, current, and total energy | Show aggregate strip energy separately from socket rows. |
| Per-child energy | child-context `emeter.get_realtime`, `get_daystat`, and `get_monthstat` succeeded for all six child sockets | Show per-outlet power/energy when present; do not derive it from parent aggregate. |
| Schedule | parent namespace schedule reads supported; live `get_next_action.type` was `-1` when no next action existed | Details can show parent schedule metadata when useful. |
| Per-child schedule | child-context `schedule.get_rules`, `schedule.get_next_action`, `count_down.get_rules`, and `anti_theft.get_rules` succeeded for all six child sockets; current live rules were empty/off | Treat schedule/countdown/away metadata as per-socket capable. UI should not claim a parent rule count covers every socket unless parent and child scopes are displayed separately. |
| Diagnostics | time, timezone, firmware download, cloud info, cloud firmware list, LED state, and device icon probes succeeded or returned structured unsupported/error responses | Keep diagnostics in details; do not surface private cloud identity values. |

Answer to the current HS300 question: the strip has both parent-level behavior and child-socket behavior. `system.get_sysinfo.children[]` carries per-port state. Parent `emeter.get_realtime` is not enough for the card because each socket also supports child-context energy reads. Parent and child schedule surfaces both exist; the live device currently has empty child schedules/countdowns/away rules, but HVO must keep those scopes separate when presenting or later editing them.

Recommended card: power strip card with aggregate energy summary plus a six-row outlet table. Each outlet row can show its own state, on-time, and realtime power when available. Details should separate aggregate strip energy, per-outlet energy, parent schedule metadata, and per-outlet schedule metadata. Do not show one schedule count as if it definitively covers every outlet.

### LB230(E26) Bulb

Observed live device: `Kitchen Sink Light`, model `LB230(E26)`, hardware `1.0`, firmware `1.8.11 Build 191113 Rel.105336`, MAC `AC:84:C6:02:C6:06`.

Observed kind: `Bulb`.

Current live capability summary: `Color`, `Diagnostics`, `Dimming`, `LightState`, `ScheduleMetadata`, `VariableColorTemperature`; metadata includes `BulbLightRead`, `Diagnostics`, `EnergyRealtime`, `FirmwareInfo`, `ScheduleRead`.

Live read-only status/API shape:

| Area | Live evidence | Card implication |
|------|---------------|------------------|
| State | `system.get_sysinfo.light_state.on_off: 1`; `get_light_state.on_off: 1` | Show bulb on/off state from light state, not relay state. |
| Brightness/color | `mode: circadian`, `brightness: 5`, `hue: 0`, `saturation: 0`, `color_temp: 2700`; flags `is_dimmable: 1`, `is_color: 1`, `is_variable_color_temp: 1` | Show brightness, mode, color temperature, and HSV/color indicator. A zero saturation with nonzero color temp is white/CT mode, not an RGB color swatch. |
| Supported color temperature | External `python-kasa` model table lists `LB230` range `2500-9000 K` | Use `2500-9000 K` for future validation/controls; display current CT as a value, not a slider until controls are approved. |
| Preferred states | `preferred_state[]`: `0` warm white `2700 K` at `50%`; `1` hue `0` sat `75` at `100%`; `2` hue `120` sat `75` at `100%`; `3` hue `240` sat `75` at `100%` | Details should show preset rows/swatches with brightness, CT, hue, and saturation. |
| Preferred-state read command | `smartlife.iot.smartbulb.lightingservice.get_preferred_state` returned the same four states under `states[]` | Sysinfo is sufficient for normal snapshots; command is a confirmed alternate read path. |
| Turn-on behavior | `get_default_behavior`: `soft_on.mode: last_status`, `hard_on.mode: last_status` | Details should show soft/hard turn-on behavior; HVO now reads this as bulb metadata. |
| Light details | `get_light_details`: beam `120`, voltage `110-120`, wattage `10`, incandescent equivalent `75`, max lumens `1000`, CRI `80` | Details should show bulb technical specs. |
| Energy | legacy `emeter` rejects with `-2001`; bulb namespace `smartlife.iot.common.emeter.get_realtime` returns `power_mw: 1310` | Show realtime power only when present; do not invent voltage/current/total energy for LB230. |
| Schedule | legacy `schedule` rejects with `-2001`; bulb namespace `smartlife.iot.common.schedule.get_rules`: `enable: 0`, `version: 2`, count `0`; `get_next_action.type: -1` | Use bulb schedule namespace only. Show no active schedule badge when disabled and empty. |
| Away/countdown | `smartlife.iot.common.anti_theft.get_rules`: `enable: 0`, count `0`; legacy `count_down.get_rules` rejects with `-2001` | Hide away/countdown state unless bulb-specific rules are enabled. |
| Time/timezone | legacy `time` rejects with `-2001`; bulb `smartlife.iot.common.timesetting` returns local time and timezone `index: 7` | Device time diagnostics must use bulb namespace. |
| Cloud/firmware | legacy `cnCloud` rejects with `-2001`; bulb `smartlife.iot.common.cloud.get_info` succeeds; `system.get_download_state` succeeds; `system.get_dev_icon` rejects with `-2000` | Diagnostics panel only; do not expose raw cloud account/server fields by default. Icon is optional. |
| Dimmer module | `smartlife.iot.dimmer` commands timed out/unsupported on this bulb | Do not reuse HS220 dimmer module UI or commands for LB230. |

Recommended card: bulb card with on/off, brightness, mode, current color temperature, HSV/color indicator, realtime watts when present, and last poll/health. Details should include preset rows/swatches, soft/hard turn-on behavior, bulb technical specs, schedule state, bulb time/timezone, cloud/firmware diagnostics, and power-only emeter data. Do not use outlet, switch, strip, or HS220 dimmer language.

### KL130(US) Bulb

Observed live device: `Living Room Lamp`, model `KL130(US)`, hardware `1.0`, firmware `1.8.11 Build 191113 Rel.105336`.

Observed kind: `Bulb`.

Current live capability summary: `Color`, `Diagnostics`, `Dimming`, `LightState`, `ScheduleMetadata`, `VariableColorTemperature`; metadata includes `BulbLightRead`, `Diagnostics`, `EnergyRealtime`, `FirmwareInfo`, `ScheduleRead`.

Live read-only status/API shape:

| Area | Evidence | Card implication |
|------|----------|------------------|
| State | `system.get_sysinfo.light_state.on_off: 1`; `get_light_state.on_off: 1` | Show bulb state from light state, not relay state or outlet rows. |
| Brightness/color | `mode: normal`, `brightness: 100`, `hue: 60`, `saturation: 0`, `color_temp: 3920`; flags `is_dimmable: 1`, `is_color: 1`, `is_variable_color_temp: 1` | Same bulb UI pattern as LB230. A zero saturation with nonzero color temperature is white/CT mode, not a saturated RGB color. |
| Preferred states | four `preferred_state[]` entries: warm white slot plus red/green/blue HSV slots with `color_temp: 0` | Details should show preset rows/swatches with brightness, CT, hue, and saturation. |
| Turn-on behavior | `get_default_behavior`: `soft_on.mode: last_status`, `hard_on.mode: last_status` | Details should show soft/hard turn-on behavior. |
| Light details | live `get_light_details` timed out on the safe lamp during the redacted sweep; repo probe may mark the module supported when it responds | Treat bulb light details as optional diagnostics. Card must not require wattage/lumen details to render a KL130. |
| Energy | legacy `emeter` rejects with `-2001`; bulb namespace `smartlife.iot.common.emeter.get_realtime` returned power-only `power_mw` | Show realtime watts when present; do not invent voltage/current/total energy for KL130. |
| Schedule | legacy `schedule` rejects with `-2001`; bulb namespace `smartlife.iot.common.schedule.get_rules`: `enable: 0`, `version: 2`, count `0`; `get_next_action.type: -1` | Use bulb schedule namespace only. Show no active schedule badge when disabled and empty. |
| Time/timezone | legacy `time` rejects with `-2001`; bulb `smartlife.iot.common.timesetting` returns local time and timezone `index: 7` | Device time diagnostics must use bulb namespace. |
| Cloud/firmware | legacy `cnCloud` rejects with `-2001`; bulb `smartlife.iot.common.cloud.get_info` succeeds; `system.get_download_state` succeeds; `system.get_dev_icon` rejects with `-2000` | Diagnostics panel only; do not expose raw cloud account/server fields by default. Icon is optional. |
| Dimmer module | `smartlife.iot.dimmer` commands reject with `-2001` | Do not reuse HS220 dimmer module UI or commands for KL130. |

Recommended card: bulb card with on/off, brightness, mode, current color temperature, HSV/color indicator, realtime watts when present, and last poll/health. Details should include preset rows/swatches, optional light details, soft/hard turn-on behavior, schedule state, bulb time/timezone, cloud/firmware diagnostics, and power-only emeter data. Do not use outlet, switch, strip, or HS220 dimmer language.

## Current Card Accuracy Rules

## Enum And Mode Values

HVO-owned enum values must be documented and covered by tests when added. These are normalized application values, not raw vendor strings.

| Enum | Values | Notes |
|------|--------|-------|
| `KasaDeviceKind` | `Auto`, `Plug`, `PowerStrip`, `DualOutlet`, `Switch`, `ThreeWaySwitch`, `Dimmer`, `Bulb`, `Unknown` | `Auto` is configuration input; snapshots should resolve to a concrete kind when possible. |
| `KasaCapability` | `SwitchState`, `ChildOutlets`, `EnergyRealtime`, `LightState`, `Dimming`, `Color`, `VariableColorTemperature`, `ScheduleMetadata`, `LedState`, `Diagnostics` | Capability flags drive card/profile selection. |
| `KasaMetadataCapability` | `EnergyRealtime`, `EnergyTotal`, `ScheduleRead`, `CountdownRead`, `AwayModeRead`, `LedRead`, `BulbLightRead`, `DimmerRead`, `WifiScanRead`, `FirmwareInfo`, `SignalInfo`, `Diagnostics` | Metadata flags describe reads that succeeded or are known profile metadata. |
| `KasaCommandCapability` | `SwitchPower`, `DimLevel`, `LightColor`, `LightColorTemperature`, `ScheduleWrite`, `EnergyReset`, `DeviceReset`, `Reboot` | Command capability does not imply a command is enabled in UI/API. Safety policy still applies. |
| `KasaSafetyClass` | `TelemetryOnly`, `LowRiskCommand`, `HighRiskCommand`, `SafetyCritical` | Safety classification for future command surfaces. |

Vendor string mode values are preserved as strings because TP-Link firmware may add values. Known observed or source-confirmed values:

| Surface | Values | Test status |
|---------|--------|-------------|
| Bulb current light state `light_state.mode` | `normal`, `circadian` | `normal` is covered by local LB230/KL130 fixtures; `circadian` was live-read from Kitchen Sink Light and is covered by parser tests. |
| Bulb default turn-on behavior `get_default_behavior.*.mode` | `last_status`, `customize_preset`, `circadian` | `last_status` was live-read from Kitchen Sink Light and covered by poller/parser tests; `customize_preset` is parser-tested from external fixture semantics; `circadian` is documented from `python-kasa` behavior enum but was not live-observed on the LB230. |
| Dimmer default behavior `smartlife.iot.dimmer.get_default_behavior.*.mode` | `last_status`, `customize_preset`, `gentle_on_off`, `instant_on_off`, `gentle_on`, `none`, `unknown` | HS220 live-read covered `last_status`, `gentle_on_off`, and `instant_on_off`; parser tests cover `customize_preset`, `gentle_on`, and `instant_on_off`; `none`/`unknown` are external-fixture values and must be preserved if seen. |
| Schedule next action `type` | `-1`, `2` observed | `-1` means no next action in current probes. `2` was observed for active countdown/app timer. Keep numeric until the rule taxonomy is fully validated. |
| Relay/light booleans | `relay_state`, `children[].state`, `light_state.on_off` use `0`/`1` | Normalize to nullable bool in snapshots; preserve raw JSON for diagnostics. |

Not all vendor modes are live-tested on every device. The current rule is: live-test the active device's current values, parser-test known alternate values, document source/fixture-only values, and preserve unknown strings rather than rejecting the device.

## Parameter Value Domains

Any raw TP-Link value that becomes an HVO API parameter should be wrapped in a named request model, enum, or bounded value object before it is exposed. Values that are only read for diagnostics can remain raw vendor values, but the known domains below must stay documented and test-covered so UI labels and future command models do not guess.

| Domain | Raw field(s) | Known values or range | HVO exposure rule |
|--------|--------------|-----------------------|-------------------|
| Binary enabled flag | `enable`, `set_overall_enable.enable` | `0` disabled, `1` enabled | Expose as `bool`, not int. |
| Relay/socket/LED state | `relay_state`, `children[].state`, `light_state.on_off`, `set_relay_state.state`, `set_led_off.off` | `0` off/false, `1` on/true; `set_led_off.off` is inverted UI language: `1` means LED disabled | Expose as named booleans so LED disabled is not confused with relay off. |
| Schedule weekday mask | `wday[0..6]` | Seven integers, each `0` or `1`; observed ordering is Sunday, Monday, Tuesday, Wednesday, Thursday, Friday, Saturday. Example W/T/F/S: `[0,0,0,1,1,1,1]`; all week: `[1,1,1,1,1,1,1]` | Expose as a weekday set, not a raw int array. |
| Schedule repeat | `repeat` | `0` one-time, `1` repeating | Expose as a schedule recurrence value. |
| Schedule action | `sact`, `eact`, next-action `action` | `0` off, `1` on, `-1` no end action/disabled end action where observed in `eact` | Expose as `Off`, `On`, or `None` depending field context. |
| Schedule time option | `stime_opt`, `etime_opt` | `0` fixed minute-of-day; `-1` disabled/not used for observed end time. Sunrise/sunset variants are expected in vendor families but not live-validated here. | Expose fixed-time first; do not expose sunrise/sunset until captured from a real rule. |
| Schedule minute | `smin`, `emin` | `0-1439` minute of day; `emin: 0` paired with disabled `etime_opt: -1` in observed rules | Expose as local time-of-day. |
| Schedule offset | `soffset` | `0` observed; likely minute offset for sun-relative rules | Keep docs-only until sunrise/sunset rules are captured. |
| Schedule next action type | `get_next_action.type`, `children[].next_action.type` | `-1` none/no next action, `1` scheduled rule observed on HS105 app one-time schedule, `2` active countdown/app timer observed on HS105/HS200/HS210 | Preserve numeric plus label until per-device taxonomy is complete. |
| Countdown action | `count_down.*.act` | `0` turn off, `1` turn on | Expose as a timer target state. |
| Countdown delay/remain | `delay`, `remain` | Live lab guard allows `5-300` seconds; app reads showed larger values such as `5400` seconds | Separate lab safety range from future product range. |
| Active mode | `system.get_sysinfo.active_mode` | `none`, `schedule`, `count_down` observed | Preserve as vendor string and use for diagnostics/status badges only. |
| Timezone index | `time.get_timezone.index`, `time.set_timezone.index`; bulb timesetting equivalent | `0-200` guarded lab range; live devices reported `7` | Expose as vendor timezone index only after mapping to a human timezone label. |
| Dimmer brightness | `system.get_sysinfo.brightness`, `set_brightness.brightness` | `1-100` for guarded HS220 `set_brightness`; transition APIs may accept `0` for off | Expose dimmer brightness as percent with a separate off command. |
| Dimmer timings | `fadeOnTime`, `fadeOffTime`, `gentleOnTime`, `gentleOffTime`, transition `duration` | Milliseconds; live HS220 read `1000`, `3000`, and `10000` examples | Keep config writes native-app-only until a typed config model exists. |
| Dimmer calibration | `bulb_type`, `minThreshold`, `rampRate` | HS220 live read `bulb_type: 1`, `minThreshold: 23`, `rampRate: 30`; external calibration range for threshold is `0-51` | Diagnostics only; calibration is maintenance, not routine control. |
| Bulb light state | `brightness`, `hue`, `saturation`, `color_temp`, `transition_period` | Brightness `0-100`; hue `0-360`; saturation `0-100`; LB230 color temperature `2500-9000 K`; `color_temp: 0` indicates HSV color mode in observed presets | Expose through bulb-specific controls, never plug/switch controls. |
| Preset index | `preferred_state[].index`, `set_preferred_state.index` | LB230 live presets `0-3` | Expose as profile-derived preset slots, not a global fixed count. |
| Firmware download status | `system.get_download_state.status`, `ratio`, `flash_time`, `reboot_time` | Numeric vendor diagnostics; HVO currently reads but does not interpret full taxonomy | Keep raw diagnostics until a source-confirmed status enum exists. |
| Cloud diagnostic codes | `fwNotifyType`, `illegalType`, `tcspStatus`, plus booleans `binded`, `cld_connection`, `stopConnect` | Numeric/vendor booleans observed; no complete taxonomy yet | Keep raw diagnostics; do not expose as operator choices. |

| Device kind | Primary card fields | Details fields | Avoid |
|-------------|---------------------|----------------|-------|
| Plug | device state, energy if supported, last poll/health | schedule/time/cloud/firmware diagnostics | fake child port rows for simple plugs. |
| Switch | switch state, last poll/health | schedule/time/cloud/firmware diagnostics | plug/outlet wording. |
| Three-way switch | relay/device state, last poll/health | schedule/time/cloud/firmware diagnostics | implying complete circuit truth without validation. |
| Dimmer | state, brightness, last poll/health | dimmer behavior/parameters, schedule diagnostics | bulb hue/color controls. |
| Dual outlet | parent derived state, two child outlet rows | per-child metadata once validated; parent diagnostics | aggregate-only state as the whole story. |
| Power strip | parent derived state, six child outlet rows, aggregate energy, per-child realtime energy when available | per-child schedule/countdown/away diagnostics once UI has a scoped detail view | claiming aggregate energy is per-port or merging parent/child schedules. |
| Bulb | light state, brightness, color temperature/HSV, mode | preferred states, light details, bulb schedule/energy | outlet rows and plug-only modules. |

## Implementation Gaps To Close Next

1. Add scoped child schedule/countdown/away models if the UI needs to display per-outlet automation metadata beyond probe diagnostics.
2. Split the Blazor card into a common shell plus kind-specific bodies/details so the UI cannot accidentally show plug fields on bulbs or merge aggregate strip energy with per-port energy.
3. Stop projecting simple relay devices as a generic `Port 1` in the public UI unless a generic outlet table is explicitly chosen for all relay devices.
4. Preserve raw vendor JSON locally for diagnostics, but keep public APIs redacted and normalized.

## Write And Control Inventory

This section inventories writable properties and commands that matter for the currently configured device set. It is intentionally broader than the commands HVO should expose first. Live write execution remains blocked until a command is explicitly designed, identity-validated, authorized, audited, and approved against the connected load.

Current HVO code status: `KasaLegacyClient.SendReadOnlyAsync` rejects every command listed below because `KasaCommands.IsKnownReadOnly` only allowlists read operations. That is correct for the current gateway state.

### Write Command Risk Classes

| Risk class | Meaning | Examples | Default HVO stance |
|------------|---------|----------|--------------------|
| Low operational risk | Changes local presentation or non-load config. | Alias, LED/night mode. | Candidate after identity validation and audit. |
| Load-affecting | Changes power, brightness, color, or circuit/load state. | Relay on/off, dimmer brightness, bulb transitions, strip child socket state. | Needs connected-load safety classification and explicit operator approval. |
| Schedule/automation | Changes future behavior without immediate visible state. | Add/edit/delete/enable schedules, countdown, away mode, default bulb/dimmer behavior. | Keep native app primary until HVO has schedule UX, audit, and rollback semantics. |
| Counter/history destructive | Deletes device counters. | Energy/runtime statistic erase. | Block unless an operator requests a specific maintenance action. |
| Network/cloud/system destructive | Can disconnect, reset, reboot, bind/unbind, or change identity. | Wi-Fi join, cloud bind/unbind, MAC write, reboot, factory reset. | Block from normal UI/API. Keep as break-glass/admin tooling only, if ever. |

### Common Write Surface

| Command/property | Module/operation | Parameters seen in references | Applies to current device types | HVO need | Initial HVO policy |
|------------------|------------------|-------------------------------|---------------------------------|----------|--------------------|
| Set alias | `system.set_dev_alias`; bulbs may use `smartlife.iot.common.system.set_dev_alias` | `alias` string | Plug, switch, dimmer, strip parent, bulb; child aliases via child context likely for strip sockets | Useful for local naming sync, but HVO can keep its own friendly name instead | Defer; prefer HVO-local display name first. |
| Set MAC | `system.set_mac_addr` | `mac` string | Legacy base device API | No normal HVO need | Block. Changing vendor MAC/identity is unsafe. |
| Reboot | `system.reboot` | `delay` seconds | Legacy base device API | Rare troubleshooting | Block from normal UI; possible admin-only maintenance command later. |
| Factory reset | `system.reset` | none/empty | Legacy base device API | No normal HVO need | Block. |
| Join Wi-Fi | `netif.set_stainfo` or `smartlife.iot.common.softaponboarding.set_stainfo` | `ssid`, `password`, `key_type` | Legacy/new setup contexts | No normal gateway need after device is configured | Block; native app/setup flow remains primary. |
| Active Wi-Fi scan | `netif.get_scaninfo` with `refresh:1` | `refresh:1` | Devices with Wi-Fi scan support | Possibly diagnostics | Block by default due privacy and side effects; HVO only has cached `refresh:0` shape probe. |
| Cloud bind | `cnCloud.bind` | `username`, `password` | Plug/switch/strip legacy cloud module | No HVO need | Block. |
| Cloud unbind | `cnCloud.unbind` | none/empty | Plug/switch/strip legacy cloud module | No HVO need | Block. |
| Cloud firmware/update server | `cnCloud.set_server_url` | `server` URL | Plug/switch/strip legacy cloud module | No HVO need | Block. |
| Set device time/timezone | `time.set_time`, `time.set_timezone`; bulb namespace equivalent may exist | date/time fields and optional timezone `index` | Plug/switch/strip/dimmer; bulbs via bulb timesetting namespace | Not needed if native app/cloud manages time | Defer; read-only timezone display is enough now. |
| LED/night mode | `system.set_led_off` | `off` integer, usually `1` to disable LED and `0` to enable | Plug/switch/strip families when `led_off` exists | Low-value convenience | Candidate later; currently blocked because standalone LED getter was not broadly supported in HVO probes. |
| Erase runtime usage | `schedule.erase_runtime_stat` through Usage module | none/empty | Plug/switch/strip child usage modules | No normal HVO need | Block. |
| Erase energy stats | `emeter.erase_emeter_stat` | none/empty | EP25, HS300/child sockets where energy is supported | No normal HVO need | Block; destructive to historical counters. |

### Plug And Switch Writes

Applies to EP25, HS105, HS200, and HS210. HS220 also inherits the relay surface but has dimmer-specific commands listed separately.

| Capability | Module/operation | Parameters | Applies to | HVO need | Initial policy |
|------------|------------------|------------|------------|----------|----------------|
| Turn on | `system.set_relay_state` | `{ "state": 1 }` | EP25, HS105, HS200, HS210, HS220; child-scoped sockets via `context.child_ids` on strips/outlets | Core control capability if HVO becomes controller | Block until load safety is recorded per device. |
| Turn off | `system.set_relay_state` | `{ "state": 0 }` | Same | Core control capability if HVO becomes controller | Block until load safety is recorded per device. |
| Toggle | no distinct legacy command required; read current state then send `set_relay_state` | derived command | Same | Convenience only | Avoid as first API; explicit on/off is safer and more auditable. |

Operational notes:

- Commands must target configured HVO identity, not host/IP alone.
- The gateway must read `system.get_sysinfo` immediately before command execution and verify device ID, model, MAC when configured, and expected child count.
- For HS200/HS210 wall circuits, load safety and physical circuit semantics matter. A three-way switch relay state may not be the complete truth of the lighting circuit.

### HS300 And KP200 Child Outlet Writes

Applies to HS300 power strips and KP200 dual outlets.

| Capability | Module/operation | Parameters | Scope | HVO need | Initial policy |
|------------|------------------|------------|-------|----------|----------------|
| Parent turn on | repeated child `system.set_relay_state` via `context.child_ids`, based on `python-kasa` parent behavior | `{ "state": 1 }` per child | All child sockets | Useful if HVO controls whole strip | Defer; prefer explicit per-socket commands first. |
| Parent turn off | repeated child `system.set_relay_state` via `context.child_ids` | `{ "state": 0 }` per child | All child sockets | Useful but high blast radius | Defer; require stronger confirmation than a single-socket command. |
| Child socket on | `context.child_ids` + `system.set_relay_state` | top-level `context: { child_ids: [id] }`, command `{ "state": 1 }` | One socket | Important for accurate strip/dual-outlet control | Block until child-context reads/writes are implemented and validated. |
| Child socket off | same | `{ "state": 0 }` | One socket | Important for accurate strip/dual-outlet control | Block until validated and load safety recorded per socket. |
| Child alias | likely child-context alias write through system module | `alias` string | One socket | Nice-to-have only | Defer; HVO-local child display name is safer. |
| Child energy reset | child-context `emeter.erase_emeter_stat` | none/empty | One socket | No normal HVO need | Block. |
| Child runtime reset | child-context `schedule.erase_runtime_stat` | none/empty | One socket | No normal HVO need | Block. |

Open validation requirement: HVO can now send allowlisted read-only `context.child_ids` commands, and HS300/KP200 per-child read surfaces are live-validated. Writes remain blocked; before any child socket write is designed, validate the write shape separately against a safe connected load.

### HS220 Dimmer Writes

Applies to HS220.

| Capability | Module/operation | Parameters | HVO need | Initial policy |
|------------|------------------|------------|----------|----------------|
| Relay on/off | `system.set_relay_state` | `{ "state": 1 }` / `{ "state": 0 }` | Basic switch control | Block until load safety approved. |
| Set brightness | `smartlife.iot.dimmer.set_brightness` | `brightness` integer, live-validated `1-100`; `40` changed Bedroom Light and restore to `100` succeeded | Core dimmer control | Allowed only in terminal lab with identity validation/readback restore; defer production UI until dimmer-specific controls and load safety are approved. |
| Transition to brightness/off | `smartlife.iot.dimmer.set_dimmer_transition` | `brightness` 0-100, `duration` milliseconds | Better dimmer UX than abrupt relay/brightness changes | Defer; requires guardrails for duration and brightness. |
| Set double-click action | `smartlife.iot.dimmer.set_double_click_action` | `mode`; optional preset `index` | Configuration, not telemetry | Defer to native app. |
| Set long-press action | `smartlife.iot.dimmer.set_long_press_action` | `mode`; optional preset `index` | Configuration | Defer to native app. |
| Set fade-on time | `smartlife.iot.dimmer.set_fade_on_time` | `fadeTime` milliseconds | Configuration | Defer. |
| Set fade-off time | `smartlife.iot.dimmer.set_fade_off_time` | `fadeTime` milliseconds | Configuration | Defer. |
| Set gentle-on time | `smartlife.iot.dimmer.set_gentle_on_time` | `duration` milliseconds | Configuration | Defer. |
| Set gentle-off time | `smartlife.iot.dimmer.set_gentle_off_time` | `duration` milliseconds | Configuration | Defer. |
| Calibrate minimum threshold | `smartlife.iot.dimmer.calibrate_brightness` | `minThreshold`, external range 0-51 | Device/load calibration | Block except explicit maintenance workflow. |
| Set button ramp rate | `smartlife.iot.dimmer.set_button_ramp_rate` | `rampRate` | Physical button behavior | Defer to native app. |

Dimmer command design notes:

- Treat brightness `0` carefully. External code uses `set_dimmer_transition` with brightness `0` for off, but normal `set_brightness` coerces `0` to `1`.
- Read back `get_dimmer_parameters`, `get_default_behavior`, and `system.get_sysinfo` after any future write.
- Do not expose dimmer writes through a generic plug/switch control surface.

### LB230/KL130 Bulb Writes

Applies to LB230 and KL130-style bulbs.

| Capability | Module/operation | Parameters | HVO need | Initial policy |
|------------|------------------|------------|----------|----------------|
| Turn on | `smartlife.iot.smartbulb.lightingservice.transition_light_state` | `on_off: 1`, optional `transition_period`; external code may include `ignore_default` | Core light control | Defer until bulb-specific UI and safety/audit exist. |
| Turn off | same | `on_off: 0`, optional `transition_period` | Core light control | Defer. |
| Set brightness | same | `brightness` 0-100, optional transition | Core bulb control | Defer until UI validates ranges. |
| Set color temperature | same | `color_temp` Kelvin, optional `brightness`, optional transition | Core bulb control | Defer; validate model-specific Kelvin range. |
| Set HSV color | same | `hue` 0-360, `saturation` 0-100, optional `brightness`, `color_temp: 0`, optional transition | Core color bulb control | Defer; bulb profile only. |
| Set full light state | same | combined state fields, with `ignore_default` semantics | Advanced control | Defer until typed request model exists. |
| Set default turn-on behavior | `smartlife.iot.smartbulb.lightingservice.set_default_behavior` | `soft_on`, `hard_on`, mode/index/brightness/color fields | Configuration | Defer to native app. |
| Apply preset | uses light state from `preferred_state` | preset brightness/color/temp fields | Convenience | Defer until preset UI exists. |
| Save preset | `smartlife.iot.smartbulb.lightingservice.set_preferred_state` | `index`, brightness, hue, saturation, color_temp | Configuration | Defer to native app. |
| Set alias | `smartlife.iot.common.system.set_dev_alias` | `alias` string | Low-value convenience | Prefer HVO-local display name. |

Bulb command design notes:

- Do not represent bulbs as outlets. The command payload is light-state based, not relay-state based.
- Preserve and validate model-specific color temperature ranges before exposing Kelvin controls.
- Transition values are milliseconds. A zero transition means immediate for bulbs in external docs, while HS220 dimmer transition code treats zero specially.
- Read back `get_light_state`, `get_light_details`, and sysinfo after any future write.

### Schedule, Countdown, And Away Writes

Applies to plug/switch/dimmer/strip/dual-outlet families through `schedule`, `count_down`, and `anti_theft`; applies to bulbs through `smartlife.iot.common.schedule` for schedules. Rule writes are known command families but are not yet HVO-designed.

| Capability | Module/operation | Parameters observed/inferred from rule reads | Applies to | HVO need | Initial policy |
|------------|------------------|----------------------------------------------|------------|----------|----------------|
| Enable/disable whole module | `set_overall_enable` | `enable` boolean/int | Schedule/countdown/away modules | Low priority | Defer; native app primary. |
| Delete one rule | `delete_rule` | `id` | Rule modules | Maintenance only | Defer. |
| Delete all rules | `delete_all_rules` | none/empty | Rule modules | Destructive | Block except explicit maintenance workflow. |
| Add rule | likely `add_rule` in vendor command family | rule body: name, enable, weekdays, repeat, start/end action/time, optional bulb `s_light` | Schedules/away/countdown | Complex automation | Defer; not in current HVO API. |
| Edit rule | likely `edit_rule` | same plus existing rule `id` | Schedules/away/countdown | Complex automation | Defer; not in current HVO API. |

Rule field model observed in external source/read metadata:

- `id`
- `name`
- `enable`
- `wday[]`
- `repeat`
- `sact` and `eact`: disabled/off/on/unknown action values.
- `stime_opt` and `etime_opt`: disabled/enabled/sunrise/sunset style time options.
- `smin` and `emin`: minute-of-day style fields.
- Bulb-only `s_light` for scheduled light state.

Schedule design notes:

- For HS300/KP200, read-only schedule/countdown/away scope is child-capable through `context.child_ids`; do not expose parent-only schedule edits as if they fully cover every socket, and validate write shapes separately before any child automation writes.
- Schedule writes are easy to get wrong because they change future behavior. Native Kasa app should remain primary until HVO can show a complete rule editor, audit log, and readback/rollback path.

### Write API Candidates For HVO

When HVO is ready to add writes, start with a deliberately tiny command API rather than exposing raw Kasa JSON.

Candidate first API shape:

| HVO command | Parameters | Maps to | Readback required |
|-------------|------------|---------|-------------------|
| `SetRelayState` | `sourceId`, optional `outletId`, `state` | `system.set_relay_state`, with `context.child_ids` when `outletId` is set | `system.get_sysinfo`; plus child state for outlets. |
| `SetDimmerBrightness` | `sourceId`, `brightness`, optional `transitionMs` | HS220 dimmer `set_brightness` or `set_dimmer_transition` | `system.get_sysinfo` and dimmer parameters. |
| `SetBulbState` | `sourceId`, `on`, optional `brightness`, `colorTemp`, `hue`, `saturation`, `transitionMs` | `transition_light_state` | bulb `get_light_state` and sysinfo. |
| `SetLedMode` | `sourceId`, `enabled` | `system.set_led_off` | `system.get_sysinfo`. |

Commands that should remain out of the normal API:

- raw JSON send,
- schedule add/edit/delete,
- erase energy/runtime counters,
- Wi-Fi join or active scan,
- cloud bind/unbind/server changes,
- MAC changes,
- reboot,
- factory reset,
- firmware operations.

### Operational Blockers Before Any Write

Before enabling even one write command, HVO needs:

1. Per-device and per-child connected-load classification.
2. Identity validation immediately before command execution.
3. Capability/profile validation that prevents plug commands from being sent to bulbs and bulb commands from being sent to plugs.
4. Local authorization separate from read-only API keys.
5. Audit log with requested state, prior readback, command payload type, and post-command readback.
6. Rate limits and duplicate-command handling.
7. A dry-run mode that shows the exact normalized command without sending it.
8. Failure semantics for command sent but readback failed.
9. Tests using `FakeKasaLegacyServer` for each command type before any live device test.
10. Explicit operator approval for the first live command per device kind.

## Safety Boundary

This document describes read-only discovery and metadata. Do not enable live write/control operations from this inventory alone. Any `set_*`, relay, dimmer transition, schedule mutation, reset, reboot, firmware, cloud, or Wi-Fi command still needs explicit operator approval, identity validation, capability validation, and connected-load safety review.