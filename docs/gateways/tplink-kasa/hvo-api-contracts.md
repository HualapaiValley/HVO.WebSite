# TP-Link / Kasa HVO API And Payload Contracts

Status: draft. No local APIs, configuration library, outbox payloads, or cloud ingest contracts are implemented yet.

Implementation sequencing: establish local device control/configuration, discovery reporting, and current-state APIs before any outbox/cloud forwarding work.

Next design pass should define the device library and capability model first: classes/interfaces/enums for transport, protocol operations, device identity, capabilities, snapshots, local UI models, telemetry models, and outbox payload candidates. Do not lock outbox payloads before the capability model is stable.

## HVO Normalization And Aliases

Do not lock central/cloud field names until implementation tests and central ingest contracts are created. The read-only live scan did validate several local parser candidates for the observed legacy devices.

Candidate mappings for initial legacy read-only scope:

| Vendor/API source name | HVO local name | HVO normalized unit | Conversion | Notes |
|------------------------|----------------|---------------------|------------|-------|
| `system.get_sysinfo` response | `KasaDeviceSnapshot.RawSystemInfo` | raw JSON | none | Preserve raw/vendor object for diagnostics until fields are validated. |
| top-level `relay_state` | `KasaOutletSnapshot.IsOn` | boolean | `1` -> true, `0` -> false | Observed on EP25 and HS105. |
| `children[].state` | `KasaOutletSnapshot.IsOn` | boolean | `1` -> true, `0` -> false | Observed on HS300 and KP200 child outlets. |
| `light_state.on_off` | `KasaLightSnapshot.IsOn` | boolean | `1` -> true, `0` -> false | Observed on KL130 bulbs; commands deferred. |
| top-level or child `on_time` | `OnTimeSeconds` | seconds | none | Reset behavior needs validation before cloud use. |
| `emeter.get_realtime` response | `KasaEnergySnapshot.RawRealtimeEnergy` | raw JSON | none | Keep raw object until field names and units are confirmed. |
| `power_mw` | `PowerW` | W | divide by `1000` | Observed on EP25 and HS300. |
| `voltage_mv` | `VoltageV` | V | divide by `1000` | Observed on EP25 and HS300. |
| `current_ma` | `CurrentA` | A | divide by `1000` | Observed on EP25 and HS300. |
| `total_wh` | `EnergyKWh` | kWh | divide by `1000` | Reset/rollover semantics need validation before treating as a durable counter. |
| `slot_id` | `OutletIndex` or raw field | none | none | Observed only on HS300 hardware `2.0` realtime energy response; do not require it globally. |

## Candidate Capability Model

These are HVO local concepts for design and implementation. They are not vendor protocol fields.

| Concept | Candidate values / shape | Purpose |
|---------|--------------------------|---------|
| `KasaProtocolFamily` | `LegacyKasaTcp9999`, future `KasaSmartAuthenticated`, `TapoAuthenticated`, `Matter`, `HomeKit` | Keep future protocol support explicit. |
| `KasaDeviceKind` | `Plug`, `PowerStrip`, `DualOutlet`, `Switch`, `ThreeWaySwitch`, `Dimmer`, `Bulb`, `Unknown` | UI grouping and default capability hints. |
| `KasaCapability` | `SwitchState`, `ChildOutlets`, `EnergyRealtime`, `LightState`, `Dimming`, `Color`, `VariableColorTemperature`, `ScheduleMetadata`, `LedState`, `Diagnostics` | Composition-based behavior flags. |
| `KasaCommandCapability` | `SwitchPower`, `DimLevel`, `LightColor`, `LightColorTemperature`, `ScheduleWrite`, `EnergyReset`, `DeviceReset`, `Reboot` | Document command surface separately from read-only capability. Initially disabled. |
| `KasaSafetyClass` | `TelemetryOnly`, `LowRiskCommand`, `HighRiskCommand`, `SafetyCritical` | Commands require explicit operator classification. |
| `KasaDeviceProfile` | model, hardware version, software version, protocol family, kind, capabilities | Model/firmware detection result, not an inheritance hierarchy. |

## Local Configuration

| Setting | Type | Required | Secret | Runtime editable | Default | Notes |
|---------|------|----------|--------|------------------|---------|-------|
| `GatewayId` | string | Yes | No | No | `hvo-tplink-kasa` | Common gateway identity. |
| `Networks` | array | No | No | App config | empty | Optional network/subnet labels for discovery and status reporting. |
| `Networks[].Name` | string | Yes when network configured | No | App config | empty | Example labels: `observatory`, `home`. |
| `Networks[].Cidr` | string | Yes when network configured | No | App config | empty | Example: `192.168.1.0/24`; avoid committing per-device IP inventory. |
| `Networks[].DiscoveryEnabled` | bool | No | No | App config | false | Enables read-only discovery scans for the network. |
| `Devices` | array | Yes | No | App config | empty | Static configured device list for initial implementation. |
| `Devices[].Host` | string | Yes | No | App config | empty | IP or DNS name. Prefer static DHCP reservation. |
| `Devices[].NetworkName` | string | No | No | App config | empty | Associates a device with a configured network label. |
| `Devices[].SourceId` | string | Yes | No | App config | empty | Stable source ID for outbox/cloud. |
| `Devices[].DeviceId` | string | Recommended | No | App config | empty | Stable device ID; may be HVO-assigned until vendor ID is validated. |
| `Devices[].ExpectedModel` | string | No | No | App config | empty | Used to detect swapped devices. |
| `Devices[].ExpectedHardwareVersion` | string | No | No | App config | empty | Optional guard for known hardware variants. |
| `Devices[].ExpectedSoftwareVersion` | string | No | No | App config | empty | Optional inventory/diagnostic guard; firmware may change. |
| `Devices[].SupportsEnergyMeter` | bool | No | No | App config | false | Avoids repeated `emeter` errors on non-energy devices. |
| `Devices[].ExpectedChildCount` | int | No | No | App config | null | Useful for HS300/KP200 devices where outlets are represented as children. |
| `Devices[].DeviceKind` | enum/string | No | No | App config | `Auto` | Candidate values could be `Plug`, `PowerStrip`, `DualOutlet`, `Switch`, `ThreeWaySwitch`, `Dimmer`, `Bulb`, `Unknown`, or `Auto`; do not expose commands from this alone. |
| `Devices[].ProtocolFamily` | enum/string | No | No | App config | `LegacyKasaTcp9999` initially | Future values may be needed for HomeKit, Matter, Tapo, or newer authenticated Kasa devices. |
| `Devices[].AlternateEcosystems` | array | No | No | App config/discovery | empty | Metadata such as `HomeKit`, `Matter`, `Alexa`, `GoogleAssistant`, `SmartThings`; do not imply HVO protocol support. |
| `Devices[].Capabilities` | array | No | No | App config/discovery | observed/configured | Read-only capability flags from profile and discovery. |
| `Devices[].CommandCapabilities` | array | No | No | App config | empty | Command possibilities only; runtime commands remain disabled unless safety gates are met. |
| `Devices[].SafetyClass` | enum/string | Yes before commands | No | App config | `TelemetryOnly` | Commands disabled unless explicitly classified later. |
| `PollIntervalSeconds` | int | Yes | No | App config | TBD | Must avoid flooding devices. |
| `SocketTimeoutSeconds` | int | Yes | No | App config | TBD | Applies to TCP command round trips. |
| `Outbox` settings | object | If cloud forwarding enabled | API key secret | App config | shared | Use `HVO.Edge.Outbox` conventions. |

Credentials for newer Kasa/Tapo devices are not part of initial scope. If later needed, use secret configuration and never log usernames/passwords/tokens.

## Local APIs

Proposed local endpoints:

| Endpoint | Method | Auth | Purpose | Response |
|----------|--------|------|---------|----------|
| `/api/devices` | GET | local API key TBD | List configured/discovered device summaries. | Array of `KasaDeviceSummary`. |
| `/api/devices/{deviceId}` | GET | local API key TBD | Current status for one device. | `KasaDeviceSnapshot`. |
| `/api/gateway/status` | GET | local API key TBD | Gateway health, sample age, outbox state. | Common gateway health/status object. |
| `/health` | GET | none/internal | Container health check. | ASP.NET health status. |

No command endpoints are planned for initial implementation.

## Candidate Local DTOs

These are HVO local concepts, not vendor response contracts.

### `KasaDeviceSummary`

| Field | Type | Notes |
|-------|------|-------|
| `deviceId` | string | HVO/device identity. |
| `sourceId` | string | Outbox/cloud source identity. |
| `host` | string | Local configured address. |
| `alias` | string? | Vendor or HVO friendly label if validated. |
| `model` | string? | Vendor model if validated. |
| `hardwareVersion` | string? | Vendor hardware version if captured. |
| `softwareVersion` | string? | Vendor software version if captured. |
| `isOnline` | bool | Current polling status. |
| `lastSeenUtc` | DateTime? | Last successful read. |
| `networkName` | string? | Configured network label for grouping observatory/home devices. |
| `protocolFamily` | string? | Observed/configured protocol family. |
| `alternateEcosystems` | array? | Confirmed ecosystem metadata such as HomeKit or Matter. |
| `supportsEnergyMeter` | bool? | Configured/observed capability. |
| `capabilities` | array? | HVO capability flags for local UI and diagnostics. |
| `commandCapabilities` | array? | Potential command capabilities; not enabled by default. |
| `outletCount` | int? | Child outlet count for strips/dual outlets; `1` for top-level plug if normalized that way. |
| `isOn` | bool? | Current switch/light state for single-state devices only. Multi-outlet devices use `outlets`. |

### `KasaDeviceSnapshot`

| Field | Type | Notes |
|-------|------|-------|
| `deviceId` | string | HVO/device identity. |
| `sourceId` | string | Outbox/cloud source identity. |
| `observedAtUtc` | DateTime | Observation time. |
| `isOnline` | bool | Polling result. |
| `isOn` | bool? | Current switch state if validated. |
| `powerW` | double? | Only for validated energy-meter fields. |
| `voltageV` | double? | Only for validated energy-meter fields. |
| `currentA` | double? | Only for validated energy-meter fields. |
| `energyKWh` | double? | Only for validated energy-meter fields. |
| `outlets` | array? | Per-outlet states for HS300/KP200 or normalized single-outlet devices. |
| `light` | object? | Bulb status for KL130/LB230-style devices. |
| `rawSystemInfo` | JsonElement? | Optional local diagnostics; do not forward by default. |
| `rawRealtimeEnergy` | JsonElement? | Optional local diagnostics; do not forward by default. |

### `KasaOutletSnapshot`

| Field | Type | Notes |
|-------|------|-------|
| `outletId` | string | HVO/local outlet identity. For child devices this can map from vendor child `id` when present; otherwise use a stable configured ID. |
| `index` | int? | Physical/logical outlet index when known. |
| `isOn` | bool? | Parsed from `relay_state` or `children[].state`. |
| `onTimeSeconds` | long? | Parsed from `on_time`; reset behavior not validated. |
| `nextAction` | JsonElement? | Local diagnostics only until schedule semantics are validated. |

### `KasaLightSnapshot`

| Field | Type | Notes |
|-------|------|-------|
| `isOn` | bool? | Parsed from `light_state.on_off`. |
| `isDimmable` | bool? | Parsed from `is_dimmable` when present. |
| `isColor` | bool? | Parsed from `is_color` when present. |
| `isVariableColorTemperature` | bool? | Parsed from `is_variable_color_temp` when present. |
| `rawLightState` | JsonElement? | Local diagnostics only; commands deferred. |

## Outbox / Cloud Candidate Streams

Use the common gateway outbox standards. Do not add a gateway-local outbox unless shared code is missing a required capability.

| Stream | Payload type | Cadence | Idempotency key | Cloud treatment | Notes |
|--------|--------------|---------|-----------------|-----------------|-------|
| Device inventory | `device.inventory` or future typed name | On startup/change | source/device + content hash | Candidate config snapshot | Model/firmware/MAC/etc. only after field validation. |
| Device switch state | `device.switch-state` or future typed name | Poll cadence/change | source/device/outlet + recordedAt | Candidate event/status | Must include outlet identity for HS300/KP200. |
| Device light state | `device.light-state` or future typed name | Poll cadence/change | source/device + recordedAt | Candidate status | Bulb support is status-only initially. |
| Device power telemetry | `power.device-reading` or future typed name | Poll cadence | source/device/outlet-if-known + recordedAt | Candidate telemetry | Keep distinct from inverter/site aggregate power. |
| Gateway status | `gateway.status` | low frequency | gateway + recordedAt | Candidate common status | Should match common gateway standards. |

Do not mix TP-Link per-outlet power with SolarAssistant inverter/site aggregate power without explicit source/device attribution. Same units do not imply same domain semantics.

## Cloud Command Policy

Cloud-originated commands are not allowed for initial TP-Link/Kasa work.

If commands are ever added:

- local-only first.
- no cloud-to-device path without separate safety design.
- require connected-load classification.
- require operator confirmation for any high-risk load.
- require audit events.
- require read-back verification.
- keep destructive operations such as factory reset and energy-stat reset unsupported unless there is a compelling maintenance case.

## Security Notes

- Legacy Kasa LAN devices may accept commands without authentication on the LAN. Treat this as a network security risk.
- Do not expose arbitrary JSON command endpoints.
- Do not log credentials, cloud account fields, or raw payloads before reviewing them for sensitive data.
- Do not forward aliases, MAC addresses, device IDs, coordinates, or connected-load labels by default. Treat them as inventory/debug data requiring explicit review.
- Prefer static device configuration over unauthenticated broad discovery for production if network exposure is a concern.

## Open Contract Decisions

| Decision | Options | Blocking? | Notes |
|----------|---------|-----------|-------|
| Central payload names for per-device outlet power | Reuse generic power model vs new `power.device-reading` | Yes for cloud forwarding | Must stay distinct from site/inverter aggregate power. |
| Whether raw vendor JSON is exposed locally | Hidden, debug-only, or redacted endpoint | No for MVP | Useful for validation but may expose private network/cloud fields. |
| Local API auth | Same gateway API key pattern vs internal LAN only | Yes before deploy | Davis current weather endpoint uses API key pattern. |
| Command contract | None vs local-only command endpoints | No for read-only MVP | Defer. |
| Production device subset | Static all detected devices vs explicitly configured subset | Yes before deploy | Live discovery saw 42 legacy responders so far; not all may belong in HVO telemetry. |
