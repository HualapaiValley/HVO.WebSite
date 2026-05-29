# TP-Link / Kasa Manufacturer And Protocol Notes

This document describes vendor/protocol behavior only. HVO implementation choices are in [hvo-implementation.md](hvo-implementation.md).

Important provenance note: the initial research did not find official TP-Link local API documentation. The details below are from community-maintained libraries and reverse-engineering references. Treat them as practical protocol references, not vendor guarantees.

## Provenance

| Source | Use |
|--------|-----|
| `python-kasa` documentation | Device families, supported models, discovery ports, authentication requirement notes, protocol/transport overview. |
| `softScheck/tplink-smartplug` README and `tplink_smartplug.py` | Legacy Smart Home TCP port `9999`, 4-byte length prefix, XOR autokey encoding, command examples. |
| `softScheck/tplink-smartplug/tplink-smarthome-commands.txt` | Community command list for HS100/HS110-era devices. |
| `plasticrake/tplink-smarthome-api` README | Legacy Smart Home supported device categories, TCP/UDP send options, simulator reference. |
| `plasticrake/tplink-smarthome-simulator` README | Simulator existence and legacy device model examples. |
| HVO read-only live scans on 2026-05-29 | Sanitized legacy TCP `9999` response shapes and field/type summaries for EP25, HS105, HS200, HS210, HS220, HS300, KP200, KL130, and LB230 devices on HVO networks. |
| Official TP-Link/Kasa product pages | Feature/context checks for observed models, including HomeKit/Matter wording where present. |

## Known Hardware / Firmware Variants

| Variant | Differences | Detection method | Impact |
|---------|-------------|------------------|--------|
| Legacy Kasa `IOT` devices | Use original Kasa Smart Home protocol and XOR-style transport; many use port `9999`. | Discovery/known model; `python-kasa` `--type` can bypass discovery for legacy types. | Best initial HVO implementation target. |
| Newer Kasa devices | Some require authentication; some use newer `SMART` protocol and transports such as KLAP/AES over HTTP. | Discovery and `DeviceConfig` in `python-kasa`; model/hardware/firmware list. | Do not assume legacy port `9999` behavior. |
| Tapo devices | Require authentication according to `python-kasa` supported-device notes; may use `SMART` protocol and newer transports. | Discovery on port `20002`; credentials commonly required. | Out of initial HVO scope unless exact device is confirmed. |
| Matter-suffixed devices such as some `M` models | May have Matter support and/or authenticated local behavior depending on model. | Model/hardware/firmware confirmation. | Treat as separate capability family until validated. |
| HomeKit-capable Kasa devices | Some Kasa models are also marketed for Apple HomeKit. | Official product page/model label/native app. | Track as metadata; do not implement HomeKit unless it provides needed extra functionality. |

`python-kasa` supported-device notes mark many specific Kasa/Tapo models and hardware/firmware combinations. Do not infer protocol support from model prefix alone; hardware revision and firmware matter.

## Observed HVO Legacy Devices

The following table is from a sanitized HVO read-only scan. It intentionally omits aliases, MAC addresses, device IDs, coordinates, per-device IPs, and connected-load names.

| Model | Count | Hardware version | Software version | `mic_type` / `type` | `feature` | State fields observed | Energy response observed |
|-------|------:|------------------|------------------|---------------------|-----------|-----------------------|--------------------------|
| EP25(US) | 8 | `1.0` | `1.0.14 Build 240424 Rel.094105` | `IOT.SMARTPLUGSWITCH` from `mic_type` | `TIM:ENE` | top-level `relay_state`, `on_time`, `next_action` | `current_ma`, `power_mw`, `total_wh`, `voltage_mv` |
| HS105(US) | 2 | `1.0` | `1.5.6 Build 191114 Rel.104204` | `IOT.SMARTPLUGSWITCH` from `type` | `TIM` | top-level `relay_state`, `on_time`, `next_action` | unsupported module response contained `err_code` `-1` and `err_msg` |
| HS300(US) | 4 | `1.0` | `1.0.21 Build 210524 Rel.161309` | `IOT.SMARTPLUGSWITCH` from `mic_type` | `TIM:ENE` | `children[].state`, `children[].on_time`, `children[].next_action`; `child_num` `6` | `current_ma`, `power_mw`, `total_wh`, `voltage_mv` |
| HS300(US) | 2 | `2.0` | `1.0.12 Build 220121 Rel.175814` | `IOT.SMARTPLUGSWITCH` from `mic_type` | `TIM:ENE` | `children[].state`, `children[].on_time`, `children[].next_action`; `child_num` `6` | `current_ma`, `power_mw`, `slot_id`, `total_wh`, `voltage_mv` |
| HS200(US) | 11 | `1.0` | `1.2.6 Build 200727 Rel.121953` | `IOT.SMARTPLUGSWITCH` from `type` | `TIM` | top-level `relay_state` | no realtime energy fields observed |
| HS210(US) | 3 | `1.0` | `1.5.8 Build 191118 Rel.135937` | `IOT.SMARTPLUGSWITCH` from `type` | `TIM` | top-level `relay_state` | unsupported module response contained `err_msg` |
| HS220(US) | 3 | `1.0` | `1.5.11 Build 200214 Rel.152651` | `IOT.SMARTPLUGSWITCH` from `type` | `TIM` | top-level `relay_state` | unsupported module response contained `err_msg` |
| KP200(US) | 5 | `1.0` | `1.0.9 Build 200618 Rel.140140` | `IOT.SMARTPLUGSWITCH` from `mic_type` | `TIM` | `children[].state`, `children[].on_time`, `children[].next_action`; `child_num` `2` | no realtime energy fields observed |
| KL130(US) | 3 | `1.0` | `1.8.11 Build 191113 Rel.105336` | `IOT.SMARTBULB` from `mic_type` | none observed | `light_state.on_off`; `is_dimmable`, `is_color`, and `is_variable_color_temp` flags present | no realtime energy fields observed |
| LB230(E26) | 1 | `1.0` | `1.8.11 Build 191113 Rel.105336` | `IOT.SMARTBULB` from `mic_type` | none observed | `light_state.on_off`; light capability flags present | no realtime energy fields observed |

These observations confirm that a single legacy gateway must handle at least three status shapes: single-outlet/switch top-level `relay_state`, multi-outlet `children[]`, and bulb `light_state`. Energy capability is not implied by smart plug/switch family alone; use `feature` and/or actual `emeter.get_realtime` success. Dimmer-level fields/commands for HS220 were not captured because the live scan did not send dimmer-specific operations.

## HVO Read-Only Payload Shape Validation

The prototype utility validates payload shapes without retaining raw values. It records field paths and JSON value kinds only, grouped by model/hardware/software. This is the evidence used for HVO's local API guide.

Latest shape scan summary:

| Network | Responders | Device ID field present | MAC/mic MAC field present | Notes |
|---------|-----------:|------------------------:|--------------------------:|-------|
| `192.168.1.0/24` | 14 | 14 | 14 | EP25, HS105, HS300, KP200, KL130. |
| `192.168.2.0/24` | 7 | 7 | 7 | EP25 and HS300 only in latest scan. |
| `192.168.9.0/24` | 20 | 20 | 20 | HS105, HS200, HS210, HS220, KL130, LB230. |

Identity fields:

- Plugs, strips, outlets, and switches used `system.get_sysinfo.mac` in observed responses.
- Bulbs used `system.get_sysinfo.mic_mac` in observed responses.
- All latest-scan responders had `deviceId` plus either `mac` or `mic_mac`.

Per-model module support from the latest read-only shape scan:

| Model/firmware group | Count | Supported read-only modules | Unsupported/error modules |
|----------------------|------:|-----------------------------|---------------------------|
| EP25(US) hw `1.0`, sw `1.0.14 Build 240424 Rel.094105` | 7 | sysinfo, realtime energy, day/month energy stats, schedule rules, next schedule action, countdown rules, away rules, cloud info, time, timezone | LED get returned error `-2` |
| HS300(US) hw `1.0`, sw `1.0.21 Build 210524 Rel.161309` | 4 | sysinfo, realtime energy, day/month energy stats, schedule rules, next schedule action, countdown rules, away rules, cloud info, time, timezone | LED get returned error `-2` |
| HS300(US) hw `2.0`, sw `1.0.12 Build 220121 Rel.175814` | 2 | sysinfo, realtime energy, day/month energy stats, schedule rules, next schedule action, countdown rules, away rules, cloud info, time, timezone | LED get returned error `-2`; realtime energy includes `slot_id` |
| KP200(US) hw `1.0`, sw `1.0.9 Build 200618 Rel.140140` | 5 | sysinfo, schedule rules, next schedule action, countdown rules, away rules, cloud info, time, timezone | emeter realtime/day/month returned error `-1`; LED get returned error `-2` |
| HS105(US) hw `1.0`, sw `1.5.6 Build 191114 Rel.104204` | 2 | sysinfo, schedule rules, next schedule action, countdown rules, away rules, cloud info, time, timezone | emeter realtime/day/month returned error `-1`; LED get returned error `-2` |
| HS200(US) hw `1.0`, sw `1.2.6 Build 200727 Rel.121953` | 11 | sysinfo, schedule rules, next schedule action, countdown rules, away rules, cloud info, time, timezone | emeter realtime/day/month returned error `-1`; LED get returned error `-2` |
| HS210(US) hw `1.0`, sw `1.5.8 Build 191118 Rel.135937` | 3 | sysinfo, schedule rules, next schedule action, countdown rules, away rules, cloud info, time, timezone | emeter realtime/day/month returned error `-1`; LED get returned error `-2` |
| HS220(US) hw `1.0`, sw `1.5.11 Build 200214 Rel.152651` | 3 | sysinfo, schedule rules, next schedule action, countdown rules, away rules, cloud info, time, timezone | emeter realtime/day/month returned error `-1`; LED get returned error `-2` |
| KL130(US) hw `1.0`, sw `1.8.11 Build 191113 Rel.105336` | 3 | sysinfo with `light_state`, `preferred_state`, color/dimming flags | emeter, schedule, countdown, away, cloud, time, and timezone returned error `-2001`; LED get returned error `-2000` |
| LB230(E26) hw `1.0`, sw `1.8.11 Build 191113 Rel.105336` | 1 | sysinfo with `light_state`, `preferred_state`, color/dimming flags | emeter, schedule, countdown, away, cloud, time, and timezone returned error `-2001`; LED get returned error `-2000` |

Read-only response field families confirmed by shape scan:

| Operation | Success shape fields | Unsupported/error shape fields | Notes |
|-----------|----------------------|--------------------------------|-------|
| `system.get_sysinfo` single relay/switch | `alias`, `deviceId`, `feature`, `hw_ver`, `mac`, `mic_type`, `model`, `relay_state`, `on_time`, `next_action.type`, `rssi`, `sw_ver`, `updating`, plus model-specific diagnostics | N/A | HS220 also exposes top-level `brightness` and `preferred_state[].brightness`. |
| `system.get_sysinfo` multi-outlet | `child_num`, `children[]`, `children[].id`, `children[].alias`, `children[].state`, `children[].on_time`, `children[].next_action.type`, plus shared device fields | N/A | HS300 child count `6`; KP200 child count `2`. |
| `system.get_sysinfo` bulb | `mic_mac`, `light_state.on_off`, `light_state.brightness`, `light_state.hue`, `light_state.saturation`, `light_state.color_temp`, `light_state.mode`, `preferred_state[]`, `is_dimmable`, `is_color`, `is_variable_color_temp` | N/A | Some KL130 responses represented defaults under `light_state.dft_on_state`. Parser must tolerate both direct light fields and nested default state. |
| `emeter.get_realtime` | `current_ma`, `power_mw`, `total_wh`, `voltage_mv`, `err_code`; HS300 hw `2.0` also `slot_id` | `err_code`, `err_msg` | Supported by EP25 and HS300 groups in latest scan. |
| `emeter.get_daystat` | `day_list[]`, `day_list[].year`, `day_list[].month`, `day_list[].day`, `day_list[].energy_wh`, `err_code` | `err_code`, `err_msg` | Read-only historical counters; reset/rollover semantics still unknown. |
| `emeter.get_monthstat` | `month_list[]`, `month_list[].year`, `month_list[].month`, `month_list[].energy_wh`, `err_code` | `err_code`, `err_msg` | Read-only historical counters; reset/rollover semantics still unknown. |
| `schedule.get_rules` | `enable`, `version`, `rule_list[]`, `err_code`; non-empty switch schedule rules included `id`, `name`, `enable`, `wday[]`, `smin`, `stime_opt`, `sact`, `emin`, `etime_opt`, `eact`, `repeat`, `year`, `month`, `day` | `err_code`, `err_msg` | HVO reads metadata only; writes remain unsupported. |
| `schedule.get_next_action` | `type`, `err_code` | `err_code`, `err_msg` | Do not enum-lock `type` values yet. |
| `count_down.get_rules` | `rule_list`, `err_code` | `err_code`, `err_msg` | Latest observed successful rule lists were empty. |
| `anti_theft.get_rules` | `enable`, `version`, `rule_list`, `err_code` | `err_code`, `err_msg` | Latest observed successful rule lists were empty. |
| `time.get_time` | `year`, `month`, `mday`, `hour`, `min`, `sec`, optional `wday`, `err_code` | `err_code`, `err_msg` | Switch firmware groups varied on `wday` presence. |
| `time.get_timezone` | `index`, optional `dst_offset`, `tz_str`, `zone_str`, `err_code` | `err_code`, `err_msg` | Firmware-specific fields vary. |
| `cnCloud.get_info` | `binded`, `cld_connection`, `server`, `username`, `tcspInfo`, `tcspStatus`, `fwDlPage`, `fwNotifyType`, `illegalType`, `stopConnect`, `err_code` | `err_code`, `err_msg` | Treat as sensitive diagnostics; do not forward raw cloud/account values by default. |
| `system.get_led_off` | No success shape observed in latest scan | `err_code`, `err_msg` | `led_off` is available in sysinfo for many devices; standalone getter returned errors. |

Additional read-only probes from the expanded privacy-safe live scan:

| Operation | Source/evidence | Safety handling | Live validation status |
|-----------|-----------------|-----------------|------------------------|
| `system.get_dev_icon` | `softScheck` command list | Read-only icon metadata; raw icon payload should remain local/diagnostic only. | Supported on HS200 in latest scan; unsupported/error on EP25, HS105, HS210, HS220, HS300, KP200, KL130, and LB230. |
| `system.get_download_state` | `softScheck` command list | Firmware download-state read only; `download_firmware` and `flash_firmware` remain blocked. | Supported on EP25, HS105, HS210, HS220, HS300, KP200, KL130, and LB230 in latest scan; unsupported/error on HS200. |
| `emeter.get_vgain_igain` | `softScheck` command list | Calibration gain read only; `set_vgain_igain` and `start_calibration` remain blocked. | Supported on EP25 and HS300 hardware `2.0`; unsupported/error on HS105, HS200, HS210, HS220, KP200, KL130, LB230, and HS300 hardware `1.0`. |
| `cnCloud.get_intl_fw_list` | `softScheck` command list and `tplink-smarthome-api` `Cloud#getFirmwareList` | Firmware-list read only; cloud bind/unbind/server writes remain blocked. | Supported on plug/switch/strip/outlet models using `cnCloud`; unsupported/error on bulbs using legacy `cnCloud`. Some HS200 devices returned mixed support by responder. |
| `smartlife.iot.smartbulb.lightingservice.get_light_state` | `tplink-smarthome-api` `Lighting#getLightState` | Bulb state read only; `transition_light_state` remains blocked. | Supported on KL130 and LB230; unsupported/error on plug/switch/strip/outlet models. |
| `smartlife.iot.smartbulb.lightingservice.get_light_details` | `tplink-smarthome-api` `Lighting#getLightDetails` | Bulb detail read only; no light writes. | Supported on KL130 and LB230; returned wattage/lumen/voltage/beam-angle detail fields. |
| `smartlife.iot.common.cloud.get_info` | `tplink-smarthome-api` bulb module namespace | Bulb cloud info read only; raw account/cloud values sensitive. | Supported on KL130 and LB230; unsupported/error on plug/switch/strip/outlet models. |
| `smartlife.iot.common.timesetting.get_time` / `get_timezone` | `tplink-smarthome-api` bulb module namespace | Bulb time diagnostics read only; time writes blocked. | Supported on KL130 and LB230; unsupported/error on plug/switch/strip/outlet models. |
| `smartlife.iot.common.schedule.get_rules` / `get_next_action` | `tplink-smarthome-api` bulb module namespace | Bulb schedule metadata read only; schedule writes blocked. | Supported on KL130 and LB230; unsupported/error on plug/switch/strip/outlet models. |
| `smartlife.iot.common.emeter.get_realtime` | `tplink-smarthome-api` bulb module namespace | Bulb energy read only; unsupported responses expected on some bulbs. | Supported on KL130 and LB230 with `power_mw`; treat as bulb namespaced energy/status until units are validated. |
| `smartlife.iot.dimmer.get_default_behavior` | `tplink-smarthome-api` `Dimmer#getDefaultBehavior` | HS220 dimmer behavior read only; action/brightness writes blocked. | Supported on HS220; returned `double_click`, `hard_on`, `long_press`, and `soft_on` mode fields. |
| `smartlife.iot.dimmer.get_dimmer_parameters` | `tplink-smarthome-api` `Dimmer#getDimmerParameters` | HS220 dimmer parameter read only; fade/gentle/transition writes blocked. | Supported on HS220; returned `bulb_type`, fade/gentle timing, `minThreshold`, and `rampRate` fields. |
| `netif.get_scaninfo` with `refresh:0` | `softScheck` WLAN command list adapted to cached-only probe | Privacy-sensitive Wi-Fi list shape only, disabled by default; `refresh:1` is blocked until explicitly approved. | Implemented/tested but not live-scanned in latest pass. |

## Alternate Protocol / Ecosystem Notes

Observed devices can support multiple ecosystems while still responding to legacy Kasa TCP `9999`. HVO should track these as metadata and only implement a non-legacy protocol when legacy Kasa does not provide required functionality or a future device requires a different protocol.

| Model | Legacy TCP `9999` observed | Other protocol/ecosystem status | Evidence |
|-------|----------------------------|---------------------------------|----------|
| EP25(US) | Yes | HomeKit confirmed; Matter not confirmed | Official Kasa EP25 page says HomeKit. |
| HS105(US) | Yes | HomeKit/Matter/Tapo not confirmed | No alternate support confirmed in current evidence. |
| HS200(US) | Yes | HomeKit/Matter not confirmed | Official page checked; it mentions Kasa app, Alexa, Google Assistant, Wi-Fi. |
| HS210(US) | Yes | HomeKit/Matter not confirmed | Official page checked; it mentions Kasa app, Alexa, Google Assistant, Wi-Fi. |
| HS220(US) | Yes | HomeKit/Matter not confirmed | Official page checked; it mentions Kasa app, Alexa, Google Assistant, Wi-Fi. |
| HS300(US) | Yes | HomeKit/Matter not confirmed | Official page checked; it mentions Kasa app, Alexa, Google Assistant, Wi-Fi. |
| KP200(US) | Yes | HomeKit/Matter not confirmed | Official page checked; it mentions Kasa app, Alexa, Google Assistant, Wi-Fi. |
| KL130(US) | Yes | HomeKit/Matter not confirmed | Official page checked; it mentions Kasa app, Alexa, Google Assistant, SmartThings. |
| LB230(E26) | Yes | HomeKit/Matter not confirmed | Official page checked; it mentions Kasa app, Alexa, Google Assistant. |

## Communication Summary

| Field | Legacy Kasa LAN | Newer Kasa/Tapo |
|-------|------------------|-----------------|
| Transport | TCP and UDP, commonly port `9999` | Discovery on UDP `20002`; communication may use HTTP port `80` and stronger transport layers |
| Protocol | `IOT` in `python-kasa` terms; JSON commands | `SMART` in `python-kasa` terms; method/parameters shape |
| Encoding/encryption | XOR autokey cipher with starting key `171`; TCP messages include 4-byte big-endian length prefix | AES/KLAP/KLAP v2 depending on device/firmware; authentication usually required |
| Authentication | `softScheck` documents no auth for legacy HS100/HS110/KP115-class Smart Home protocol | Username/password or credentials hash commonly needed |
| Discovery | UDP broadcast on `9999` | UDP broadcast on `20002`; basic info may be discoverable before auth |
| Command shape | JSON object with modules such as `system`, `emeter`, `time`, `schedule` | Different method/parameters model; not documented here |
| HVO initial target | Yes | No, document-only unless future hardware requires it |

## Legacy XOR Encoding

The `softScheck` Python client defines the legacy command encoding as an XOR autokey cipher with starting key `171`.

TCP request framing:

1. Serialize command JSON as UTF-8/ASCII text.
2. Prefix the encrypted payload with a 4-byte big-endian plaintext length.
3. Encrypt each byte by XOR with the current key.
4. Update the key to the encrypted byte after each byte.

TCP response handling:

1. Read the 4-byte big-endian length.
2. Read the encrypted payload.
3. Decrypt each byte by XOR with the current key.
4. Update the key to the encrypted byte after each byte.

UDP discovery framing needs separate validation before implementation. Do not assume the exact TCP length-prefix framing applies to UDP payloads until tested against a simulator/live device.

## Data Types And Encodings

| Type/encoding | Description | Null/not-available | Notes |
|---------------|-------------|--------------------|-------|
| JSON command object | Legacy command payload after decrypting XOR transport. | Module-specific. | Examples use `null` or `{}` as request values. |
| Integer relay state | `set_relay_state.state` examples use `1` for on and `0` for off. | Unknown. | Command deferred by HVO. |
| Energy readings | Returned by `emeter.get_realtime` on energy-meter models. | Device/model dependent. | Observed fields use milli-units/Wh: `current_ma`, `power_mw`, `voltage_mv`, `total_wh`; HS300 hardware `2.0` also included `slot_id`. |
| Schedule/countdown/away rules | JSON rule objects in community command list. | Device/model dependent. | Deferred. |

## Unit Handling

| Measurement area | Device/protocol raw unit | Device display/config unit | Does config affect raw values? | Validation status | Notes |
|------------------|--------------------------|----------------------------|-----------------------------|-------------------|-------|
| Relay/switch state | top-level `relay_state`, `children[].state`, or `light_state.on_off` depending on observed device shape | On/off | N/A | Live observed for installed legacy devices | Commands still deferred; parse read-only state only. |
| Realtime power/energy | `current_ma`, `power_mw`, `voltage_mv`, `total_wh`; optional `slot_id` on observed HS300 hardware `2.0` | App display units | Needs validation | Live observed for EP25 and HS300 | Convert milliamp/milliwatt/millivolt to A/W/V for normalized display; preserve raw fields locally. `total_wh` can be shown as Wh or converted to kWh, but reset/rollover semantics remain unvalidated. |
| Historical usage | Daily/monthly statistics from `emeter` commands | App display units | Needs validation | Needs validation | Reset behavior must be documented before HVO stores or resets counters. |

## Vendor Fields

The tables below include only fields observed in sanitized live captures or named by cited community references. Sensitive identity/location fields were observed but are intentionally not listed as normal telemetry candidates.

### Observed `system.get_sysinfo` Fields

| Field | Observed on | Meaning / handling | Notes |
|-------|-------------|--------------------|-------|
| `sw_ver` | EP25, HS105, HS300, KP200, KL130 | Device software/firmware version string | Inventory candidate. |
| `hw_ver` | EP25, HS105, HS300, KP200, KL130 | Hardware version string | Inventory candidate. |
| `model` | EP25, HS105, HS300, KP200, KL130 | Model string | Inventory candidate. |
| `type` | HS105 | Device family/type | Some devices use `mic_type` instead. |
| `mic_type` | EP25, HS300, KP200, KL130 | Device family/type | Observed values include `IOT.SMARTPLUGSWITCH` and `IOT.SMARTBULB`. |
| `feature` | EP25, HS105, HS300, KP200 | Capability hint | Observed `TIM` and `TIM:ENE`; absence does not imply no capabilities for bulbs. |
| `relay_state` | EP25, HS105, HS200, HS210, HS220 | Top-level switch state | Read-only parser should treat `0`/`1` as observed integer state. |
| `on_time` | EP25, HS105, child outlets | Seconds on, inferred from field name and values | Treat as device-reported duration; exact reset behavior needs validation. |
| `next_action` | EP25, HS105, child outlets | Next scheduled action object | Observed nested `type`; schedule semantics not validated. |
| `children` | HS300, KP200 | Child outlet list | Child fields observed include `state`, `on_time`, and `next_action`. |
| `child_num` | HS300, KP200 | Child outlet count | Observed values `6` and `2`. |
| `light_state` | KL130, LB230 | Bulb current/default state object | Observed nested `on_off`, `dft_on_state`, brightness/color fields. Commands deferred. |
| `is_dimmable` | KL130, LB230 | Bulb capability flag | Read-only status only initially. |
| `is_color` | KL130, LB230 | Bulb capability flag | Read-only status only initially. |
| `is_variable_color_temp` | KL130, LB230 | Bulb capability flag | Read-only status only initially. |
| `preferred_state` | KL130, LB230 | Bulb preset list | Local diagnostics only unless UI requirements are explicit. |
| `led_off` | EP25, HS105, HS300, KP200 | LED/night mode state | Writes deferred. |
| `updating` | EP25, HS105, HS300, KP200 | Firmware/update state | Inventory/status candidate. |
| `status` | EP25, HS300, KP200 | Device status string | Observed values should not be enum-locked yet. |
| `ntc_state` | EP25, KP200 | Device-reported state | Meaning not validated. |
| `active_mode` | EP25, HS105, KL130 | Device mode string | Do not enum-lock from current observations. |
| `err_code` | EP25, HS105, HS300, KP200, KL130 | Operation result code | `0` observed for successful system info. |

### Observed `emeter.get_realtime` Fields

| Field | Observed on | Raw/protocol unit | HVO normalized candidate | Notes |
|-------|-------------|-------------------|--------------------------|-------|
| `current_ma` | EP25, HS300 | milliamps | `CurrentA = current_ma / 1000` | Preserve raw field in diagnostics. |
| `power_mw` | EP25, HS300 | milliwatts | `PowerW = power_mw / 1000` | Preserve raw field in diagnostics. |
| `voltage_mv` | EP25, HS300 | millivolts | `VoltageV = voltage_mv / 1000` | Preserve raw field in diagnostics. |
| `total_wh` | EP25, HS300 | watt-hours | `EnergyKWh = total_wh / 1000` if storing kWh | Reset/rollover semantics not validated. |
| `slot_id` | HS300 hardware `2.0` group | outlet/slot identifier | `OutletIndex` candidate | Only observed on one HS300 hardware/firmware group. |
| `err_code` | All queried devices | operation result code | status only | `0` for supported responses; `-1` observed for unsupported HS105 emeter response. |
| `err_msg` | HS105 unsupported response | error text | diagnostic only | Do not treat unsupported emeter as device failure. |

Confirmed operation-level field families from community references:

| Vendor/API family | Source operation | Access | Semantics | Notes |
|-------------------|------------------|--------|-----------|-------|
| System info | `system.get_sysinfo` | Read-only | Metadata/status | Live capture confirms multiple shape variants; sensitive identity/location fields should not be logged or forwarded by default. |
| Relay state | `system.set_relay_state` and system info response | Command/read-only | Instantaneous state/command | Live read-only capture shows top-level `relay_state`, child `children[].state`, and bulb `light_state.on_off`; commands remain deferred. |
| Energy realtime | `emeter.get_realtime` | Read-only | Instantaneous telemetry | Live capture confirms EP25 and HS300 fields listed above; HS105 returned unsupported module response. |
| Energy statistics | `emeter.get_daystat`, `emeter.get_monthstat` | Read-only | Historical counters/interval totals | Requires reset/rollover validation. |
| Cloud info | `cnCloud.get_info`, `cnCloud.get_intl_fw_list`, bulb `smartlife.iot.common.cloud.get_info` | Read-only | Metadata/configuration | Could contain account/cloud state; avoid logging raw secrets if any. |
| Time | `time.get_time`, `time.get_timezone` | Read-only/write | Configuration/time | Writes deferred. |
| Schedule/countdown/away | `schedule`, `count_down`, `anti_theft`, and bulb `smartlife.iot.common.schedule` modules | Read/write commands | Configuration/actions | Defer all writes; native UI primary. |
| Bulb lighting | `smartlife.iot.smartbulb.lightingservice.get_light_state`, `get_light_details`, `transition_light_state` | Read/write commands | Current light state/details and light commands | HVO allowlists only read operations; transition/write operations remain blocked. |
| Dimmer parameters | `smartlife.iot.dimmer.get_default_behavior`, `get_dimmer_parameters`, and `set_*` commands | Read/write commands | HS220 dimmer behavior and fade/brightness settings | HVO allowlists only read operations; brightness/switch/transition/action writes remain blocked. |
| Wi-Fi scan/config | `netif.get_scaninfo`, `netif.set_stainfo` | Read/write commands | Nearby AP scan and station config | HVO only permits cached `get_scaninfo refresh:0` as opt-in shape-only evidence; Wi-Fi connect is not supported. |

## API Calls / Protocol Operations

| Operation | Direction | Request/framing | Response/framing | Auth | Side effects | Timeout/retry | Notes |
|-----------|-----------|-----------------|------------------|------|--------------|---------------|-------|
| Legacy TCP send | Client to device | TCP port `9999`, 4-byte big-endian length plus XOR-autokey JSON | Same length/XOR pattern per `softScheck` client | None documented for legacy | Depends on command | Socket timeout required | Initial HVO implementation target. |
| Legacy UDP discovery | Client broadcast to LAN | UDP broadcast port `9999` | Discovery replies | None documented for legacy | None for read-only discovery | Discovery timeout required | Needs simulator/live validation. |
| Newer discovery | Client broadcast to LAN | UDP broadcast port `20002` | Basic discovery info | Full query likely requires credentials | None for discovery | Discovery timeout required | Document-only initially. |
| Newer authenticated query | Client to device | HTTP/transport-specific AES/KLAP | Encrypted/authenticated response | Credentials commonly required | Depends on method | Needs protocol-specific retry | Out of initial scope. |

## Command Coverage Table

This table lists operation groups from community command references. It is not a claim that HVO supports them.

| Command/API operation | Vendor purpose | Access/safety | HVO support | HVO method/class | Unit test | Simulator test | Live test | Notes |
|-----------------------|----------------|---------------|-------------|------------------|-----------|----------------|-----------|-------|
| `system.get_sysinfo` | Read device info/status | Read-only | Prototype implemented | `KasaCommands.GetSystemInfo` | Implemented | Fake server | Completed | Sanitized field shapes captured for installed legacy devices. |
| `system.set_relay_state` | Turn on/off | Command, safety depends on load | Deferred | None | N/A | N/A | N/A | Requires load classification, local-only auth, confirmation/audit. |
| `system.set_led_off` | LED/night mode | Command, low risk | Deferred | None | N/A | N/A | N/A | Not needed initially. |
| `system.reboot` | Reboot device | Command, disruptive | Not supported | None | N/A | N/A | N/A | Exclude unless explicit maintenance mode is designed. |
| `system.reset` | Factory reset | High-risk destructive command | Not supported | None | N/A | N/A | N/A | Do not implement in HVO gateway. |
| `netif.get_scaninfo` | Scan Wi-Fi networks | Read-only-ish, privacy-sensitive | Prototype opt-in shape-only | `KasaCommands.GetCachedWifiScanInfo` | Implemented | Fake server | Pending | Only cached `refresh:0` is allowlisted; `refresh:1` is blocked. Could expose nearby SSIDs; avoid raw output. |
| `netif.set_stainfo` | Join Wi-Fi network | Write/configuration | Not supported | None | N/A | N/A | N/A | Native app primary. |
| `cnCloud.get_info` | Cloud connection info | Read-only metadata | Prototype implemented | `KasaCommands.GetCloudInfo` | Implemented | Fake server | Completed | Treat account/cloud fields as sensitive until captured. |
| `cnCloud.get_intl_fw_list` | Cloud firmware list | Read-only metadata | Prototype implemented | `KasaCommands.GetCloudFirmwareList` | Implemented | Fake server | Completed | Firmware diagnostics only; support is model/firmware dependent. |
| `cnCloud.bind` / `unbind` | Account binding | High-risk write | Not supported | None | N/A | N/A | N/A | Native app primary. |
| `time.get_time` / `get_timezone` | Read time/timezone | Read-only | Prototype implemented | `KasaCommands.GetTime` / `GetTimezone` | Implemented | Fake server | Completed | Useful diagnostics. |
| `time.set_timezone` | Set timezone/time | Write/configuration | Deferred | None | N/A | N/A | N/A | Native app primary. |
| `emeter.get_realtime` | Realtime voltage/current/power | Read-only | Prototype implemented | `KasaCommands.GetRealtimeEnergy` | Implemented | Fake server | Completed | Observed milli-unit fields on EP25/HS300; unsupported error on HS105. |
| `emeter.get_daystat` / `get_monthstat` | Historical energy stats | Read-only | Prototype implemented | `KasaCommands.GetEnergyDayStats` / `GetEnergyMonthStats` | Implemented | Fake server | Completed | Rollover/reset semantics needed. |
| `emeter.get_vgain_igain` | EMeter calibration gain read | Read-only | Prototype implemented | `KasaCommands.GetEnergyGain` | Implemented | Fake server | Completed | Calibration writes are blocked; support is model/firmware dependent. |
| `emeter.erase_emeter_stat` | Erase energy stats | Destructive command | Not supported | None | N/A | N/A | N/A | Do not implement initially. |
| `schedule.get_rules` | List schedule rules | Read-only config | Prototype implemented | `KasaCommands.GetScheduleRules` | Implemented | Fake server | Completed | Native UI primary. |
| `schedule.get_next_action` | Read next schedule action | Read-only config | Prototype implemented | `KasaCommands.GetNextScheduleAction` | Implemented | Fake server | Completed | Do not enum-lock action values yet. |
| `count_down.get_rules` | Read countdown rules | Read-only config | Prototype implemented | `KasaCommands.GetCountdownRules` | Implemented | Fake server | Completed | Writes blocked. |
| `anti_theft.get_rules` | Read away/anti-theft rules | Read-only config | Prototype implemented | `KasaCommands.GetAwayRules` | Implemented | Fake server | Completed | Writes blocked. |
| `smartlife.iot.smartbulb.lightingservice.get_light_state` / `get_light_details` | Read bulb light state/details | Read-only | Prototype implemented | `KasaCommands.GetBulbLightState` / `GetBulbLightDetails` | Implemented | Fake server | Completed | Bulb transition/write commands blocked. |
| `smartlife.iot.dimmer.get_default_behavior` / `get_dimmer_parameters` | Read HS220 dimmer configuration | Read-only | Prototype implemented | `KasaCommands.GetDimmerDefaultBehavior` / `GetDimmerParameters` | Implemented | Fake server | Completed | Dimmer brightness/switch/transition writes blocked. |
| Schedule add/edit/delete | Manage schedule rules | Command/configuration | Not supported initially | None | N/A | N/A | N/A | Needs safety design. |
| Countdown get/add/edit/delete | Countdown rule management | Command/configuration | Not supported initially | None | N/A | N/A | N/A | Needs safety design. |
| Away/anti-theft get/add/edit/delete | Randomized on/off rules | Command/configuration | Not supported initially | None | N/A | N/A | N/A | Needs safety design. |

## Commands And Writable Settings

All commands/writes are deferred for initial HVO implementation. The command list includes operations that can switch power, erase statistics, modify Wi-Fi/cloud account binding, reboot devices, factory reset devices, and alter scheduled behavior. HVO must classify connected loads and add local-only authorization/audit before any command path is implemented.

## Events / Notifications / Webhooks

No TP-Link/Kasa push event mechanism is confirmed for the initial scope. Treat the gateway as a polling/discovery integration until a model-specific event mechanism is validated.

## Manufacturer Quirks

| Issue | Evidence | Impact | Workaround | Validation needed |
|-------|----------|--------|------------|-------------------|
| Protocol family changed over time | `python-kasa` docs describe legacy `IOT`, newer `SMART`, XOR/AES/KLAP/KLAP v2 transports, and auth changes. | A driver for legacy port `9999` will not work for all Kasa/Tapo devices. | Scope initial implementation to observed legacy devices. | Reassess if future hardware appears. |
| Some newer Kasa/Tapo devices require authentication | `python-kasa` supported-device notes and CLI docs. | Credentials and stronger transport required. | Defer until confirmed hardware requires it. | Future model-specific discovery. |
| Legacy protocol provides weak/no security | `softScheck` documents no auth and trivial XOR autokey for legacy devices. | Any host on LAN may be able to command devices. | Keep HVO commands disabled initially; use network segmentation where possible. | Connected-load and command safety review. |
| Simulator exists for legacy protocol | `plasticrake/tplink-smarthome-simulator`. | Good non-live test option. | Use external simulator or build in-process fake server. | Compare HVO fake behavior against simulator. |
