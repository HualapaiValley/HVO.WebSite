# TP-Link / Kasa HVO Implementation Plan

## Design Summary

An HVO TP-Link/Kasa prototype now exists. The first implementation is a reusable device control/configuration library shape plus a read-only legacy Kasa LAN gateway targeting confirmed port `9999` devices. Sanitized live discovery found legacy TCP `9999` responders across observatory and home networks after hvo.lan/Tailscale routing was updated for `192.168.9.0/24`.

The observed device categories are sufficient to design the first library and local gateway shape: plugs, power strips, dual outlets, switches, 3-way switches, dimmers, and bulbs. The final physical inventory is still incomplete and should be treated as configuration/discovery data, not as a fixed enum of deployed devices.

The gateway should use the common gateway standards from [../common-gateway-standards.md](../common-gateway-standards.md): shared identity, shared outbox lifecycle, shared health/status concepts, and common telemetry naming where possible.

## Proposed Scope

Initial implementation target:

- `src/HVO.Gateway.TplinkKasa`
- `tests/HVO.Gateway.TplinkKasa.Tests`
- device control/configuration library first, before outbox integration.
- implement reusable library-style classes inside the gateway namespace initially; keep boundaries clean so protocol/config/capability code can move to a hardware/library project later.
- a simple CLI/console tool is acceptable as an early host for discovery, identity validation, and command dry-run workflows before the full gateway UI is built.
- file-based configured device list first, with a schema that can move into a gateway database before outbox propagation is enabled.
- configured devices keyed by stable device ID, with host/IP as the current connection locator and MAC as a required setup/validation field when known.
- network/subnet, optional friendly name, expected model, expected hardware/software, expected child count, capability flags, metadata flags, MAC validation hints, and safety classification.
- operator-initiated legacy UDP discovery and/or read-only subnet scan for add-device/configuration utility workflows; no background scanning for new devices.
- read-only polling through legacy TCP `9999` Smart Home/XOR protocol for initial devices.
- support the observed status shapes: top-level `relay_state`, multi-outlet `children[]`, bulb `light_state`, energy-meter responses, and unsupported-module error responses.
- explicit capability and metadata model for switch, dimmer, bulb, energy meter, multi-outlet, child outlet, schedule/countdown/away metadata, LED state, diagnostics, and any other read-only data a device exposes.
- implement command modes in the library/gateway surface where protocol support is understood, but require explicit operator approval before sending any live command.
- local dashboard/status only at first: basic inventory and current status.
- shared edge outbox only after local device inventory/configuration and status polling are stable.

## Prototype Capabilities

| Capability | HVO status | Notes |
|------------|------------|-------|
| Legacy TCP XOR framing | Prototype implemented/tested | Deterministic and tested with fake TCP server. |
| `system.get_sysinfo` | Prototype implemented/tested/live validated | First read-only operation; sanitized live shapes captured for EP25, HS105, HS200, HS210, HS220, HS300, KP200, KL130, and LB230. |
| `emeter.get_realtime` | Prototype implemented/tested/live validated | Handles unsupported module gracefully; EP25/HS300 returned milli-unit fields, non-energy devices returned unsupported/error shapes. |
| `emeter.get_daystat` / `emeter.get_monthstat` | Prototype read-only probe implemented/live validated | Supported on EP25/HS300 in latest scan; reset/rollover semantics still unknown. |
| `schedule.get_rules` | Prototype read-only probe implemented/live validated | Observed supported on switch/plug/strip/dual-outlet legacy devices; observed unsupported on bulb models in latest scan. |
| `schedule.get_next_action` | Prototype read-only probe implemented/live validated | Observed supported on switch/plug/strip/dual-outlet legacy devices; observed unsupported on bulb models in latest scan. |
| `count_down.get_rules` | Prototype read-only probe implemented/live validated | Observed supported on switch/plug/strip/dual-outlet legacy devices; observed unsupported on bulb models in latest scan. |
| `anti_theft.get_rules` | Prototype read-only probe implemented/live validated | Observed supported on switch/plug/strip/dual-outlet legacy devices; observed unsupported on bulb models in latest scan. |
| `cnCloud.get_info` | Prototype read-only probe implemented/live validated | Treat as sensitive diagnostics; do not forward raw cloud/account values by default. |
| `cnCloud.get_intl_fw_list` | Prototype read-only probe implemented/tested/live validated | Firmware-list metadata from community references; supported on observed non-bulb models via `cnCloud`, unsupported on bulbs via that legacy namespace. |
| `time.get_time` / `time.get_timezone` | Prototype read-only probe implemented/live validated | Useful diagnostics; firmware-specific fields vary. |
| `system.get_led_off` | Prototype read-only probe implemented/live validated | Observed unsupported/error responses in latest scan; keep modeled but not enabled as supported unless validated per device. |
| `system.get_dev_icon` / `system.get_download_state` | Prototype read-only probe implemented/tested/live validated | Device icon and firmware download-state diagnostics. Raw icon data should not be forwarded by default. Support is model-specific. |
| `emeter.get_vgain_igain` | Prototype read-only probe implemented/tested/live validated | Calibration gain read only; calibration writes remain blocked. Supported on EP25 and HS300 hardware `2.0` in latest scan. |
| `smartlife.iot.smartbulb.lightingservice.get_light_state` / `get_light_details` | Prototype read-only probe implemented/tested/live validated | Bulb-specific read namespace from `tplink-smarthome-api`; writes/transitions remain blocked. Supported on KL130 and LB230. |
| Bulb namespaced cloud/time/schedule/emeter reads | Prototype read-only probe implemented/tested/live validated | Uses `smartlife.iot.common.*` module names observed in library references for bulbs. Supported on KL130 and LB230. |
| `smartlife.iot.dimmer.get_default_behavior` / `get_dimmer_parameters` | Prototype read-only probe implemented/tested/live validated | HS220 read-only dimmer detail probes; brightness/switch writes remain blocked. |
| `netif.get_scaninfo` with `refresh:0` | Prototype opt-in read-only probe implemented/tested; live validation pending | Privacy-sensitive. Disabled by default and shape-only; `refresh:1` is blocked by the allowlist. |
| Legacy UDP discovery | Candidate after TCP polling | Need UDP framing validation. |
| Capability research | Prototype model exists | Capability flags mean observed/configured availability, not merely possible protocol support. |
| Device commands | Deferred | Requires safety/auth/audit design. |
| New Kasa/Tapo auth/KLAP/AES | Deferred | Not needed for observed legacy responders; revisit only if future hardware requires it. |
| Matter | Out of scope | Treat as separate integration path. |

## Proposed Project Layout

| Path | Purpose |
|------|---------|
| `src/HVO.Gateway.TplinkKasa/Protocol/KasaXorCipher.cs` | Legacy XOR autokey encode/decode. |
| `src/HVO.Gateway.TplinkKasa/Protocol/KasaLegacyClient.cs` | TCP client for legacy port `9999` JSON commands. |
| `src/HVO.Gateway.TplinkKasa/Protocol/KasaCommands.cs` | Minimal read-only command builders. |
| `src/HVO.Gateway.TplinkKasa/Commands/` | Command mode planning and guarded command executors; live execution requires explicit operator approval. |
| `src/HVO.Gateway.TplinkKasa/Configuration/KasaGatewayOptions.cs` | Gateway, network, discovery, and device configuration. |
| `src/HVO.Gateway.TplinkKasa/Configuration/KasaDeviceConfig.cs` | File-backed device configuration shaped for later database storage. |
| `src/HVO.Gateway.TplinkKasa/Devices/KasaCapabilityDetector.cs` | Prototype profile/capability inference from sysinfo, energy, metadata probes, and config. |
| `src/HVO.Gateway.TplinkKasa/Devices/KasaDeviceLocator.cs` | Prototype configured-host and MAC-assisted locator validation. |
| `src/HVO.Gateway.TplinkKasa/Devices/KasaEnergyParser.cs` | Prototype parser for realtime energy success and unsupported responses. |
| `src/HVO.Gateway.TplinkKasa/Devices/KasaReadMetadataParser.cs` | Typed parser for safe read-only metadata fields from schedules, countdown, away mode, time, firmware/cloud diagnostics, and dimmer detail reads. |
| `src/HVO.Gateway.TplinkKasa/Devices/KasaJsonShapeSummarizer.cs` | Sanitized field/type shape summaries for API-guide evidence. |
| `src/HVO.Gateway.TplinkKasa/Devices/KasaReadOnlyProbe.cs` | Prototype read-only single-host and CIDR probe utility. |
| `src/HVO.Gateway.TplinkKasa/Devices/KasaDeviceRegistry.cs` | Merge configured devices and discovered read-only inventory. |
| `src/HVO.Gateway.TplinkKasa/Devices/KasaIdentityValidator.cs` | Verify connected device ID/model/MAC before accepting data or commands. |
| `src/HVO.Gateway.TplinkKasa/Capabilities/` | Capability records/enums for switch, dimmer, light, energy meter, multi-outlet, and diagnostics. |
| `src/HVO.Gateway.TplinkKasa/Devices/KasaDevicePoller.cs` | Poll configured devices and normalize current state. |
| `src/HVO.Gateway.TplinkKasa/Devices/KasaDeviceSnapshot.cs` | Current read-only device status model. |
| `src/HVO.Gateway.TplinkKasa/Devices/KasaSystemInfoParser.cs` | Parse observed single-outlet, multi-outlet, and bulb status shapes while preserving raw fields separately. |
| `src/HVO.Gateway.TplinkKasa/Outbox/` | Gateway-specific outbox writer/forwarder if central ingest exists. Prefer `HVO.Edge.Outbox`. |
| `src/HVO.Gateway.TplinkKasa/Components/Pages/Status.razor` | Local status dashboard. |
| `tests/HVO.Gateway.TplinkKasa.Tests/Fakes/FakeKasaLegacyServer.cs` | In-process TCP fake for XOR/framing and command responses. |
| `tests/HVO.Gateway.TplinkKasa.Tests/Fixtures/*.json` | Sanitized representative sysinfo, energy, unsupported, schedule, and LED fixtures. |

## Main Classes And Interfaces

| Class/interface | Responsibility | Used by |
|-----------------|----------------|---------|
| `KasaXorCipher` | Encode/decode legacy Smart Home TCP frames. | Client and fake server tests. |
| `IKasaLegacyClient` | Abstraction for read-only device commands. | Poller and tests. |
| `KasaLegacyClient` | Sends JSON commands over TCP `9999` with timeout/retry. | Poller. |
| `KasaCommandService` | Builds and validates supported commands, performs identity validation, and requires explicit approval before live sends. | CLI/UI and tests. |
| `KasaDeviceRegistry` | Tracks configured device IDs, discovered responders, current locator, network location, expected shape, and capability flags. | Worker and local UI. |
| `KasaDeviceLocator` | Resolves a configured device's current host from configured IP, MAC/ARP hints, and setup discovery results. | Registry, poller, CLI/setup utility. |
| `KasaIdentityValidator` | Confirms sysinfo identity matches the configured device before data/commands are trusted. | Poller, command service, tests. |
| `KasaCapabilityDetector` | Describes model/instance capabilities without inheritance-heavy device subclasses. | Registry, poller, UI, outbox mapping. |
| `KasaMetadataSnapshot` | Captures optional read-only metadata such as schedules, countdown, away mode, LED state, firmware, and diagnostics when supported. | UI/API/outbox candidates. |
| `KasaSystemInfoParser` | Converts observed vendor response shapes into HVO snapshots without hiding raw/vendor data. | Poller and tests. |
| `KasaReadOnlyProbe` | Runs single-host or explicit CIDR read-only capability validation. | CLI/setup utility and live validation. |
| `KasaDevicePoller` | Coordinates polling all configured devices. | Background worker and local UI. |
| `KasaGatewayWorker` | Hosted service for polling, outbox enqueue, and health state. | ASP.NET host. |
| `KasaDeviceSnapshot` | HVO current-state model. | UI/API/outbox. |

## Public Methods And Samples

### `KasaLegacyClient.SendReadOnlyAsync`

Purpose: send a raw legacy JSON command to one configured device and return the JSON response document.

Side effects: none beyond network I/O when used with read-only commands.

Validation/safety: command builders should be allowlisted; do not expose arbitrary JSON in runtime endpoints.

```csharp
using var response = await client.SendReadOnlyAsync(host, 9999, KasaCommands.GetSystemInfo, ct);
```

### `KasaSystemInfoParser.Parse`

Purpose: read legacy device system info.

Side effects: none expected.

Validation/safety: response fields remain vendor/raw until fixtures confirm mappings.

```csharp
using var response = await client.SendReadOnlyAsync(host, 9999, KasaCommands.GetSystemInfo, ct);
var info = systemInfoParser.Parse(response);
```

### `KasaEnergyParser.Parse`

Purpose: read realtime energy meter fields when the device supports the `emeter` module.

Side effects: none expected.

Validation/safety: unsupported module should return no reading, not fail the whole device poll.

```csharp
using var response = await client.SendReadOnlyAsync(host, 9999, KasaCommands.GetRealtimeEnergy, ct);
var energy = energyParser.Parse(response);
```

### Prototype CLI

Purpose: run operator-initiated read-only probes during setup/research.

Side effects: sends only allowlisted read-only requests. Default output redacts identifiers and locators and does not emit stable unsalted hashes. CIDR scans are capped by host count and concurrency.

```bash
dotnet run --project src/HVO.Gateway.TplinkKasa -- scan --cidr 192.168.1.0/24 --summary true --max-hosts 256
```

### Phase 1 Read-Only Host

Purpose: run configured-device polling and expose local status/inventory without outbox forwarding or live command execution.

Side effects: polls configured devices with allowlisted read-only requests only. `/status` and `/inventory` require a Davis-style `X-Api-Key` matching `KasaGateway:ApiKey`; `/health` and `/gateway-health` expose local health only. Public DTOs avoid raw vendor device IDs, MACs, aliases, per-device hosts, and raw vendor JSON.

```bash
dotnet run --project src/HVO.Gateway.TplinkKasa
```

### Pi Deployment Scaffold

The read-only Pi deployment scaffold lives under `deploy/pi-gateways/tplink-kasa/` and follows the existing per-gateway compose pattern:

- `docker-compose.yml` builds `src/HVO.Gateway.TplinkKasa/Dockerfile` and exposes the local API on port `5500` by default.
- `.env.example` contains placeholder pilot slots only; copy it to `.env` on the Pi and never commit real device IDs, MACs, hosts, aliases, or API keys.
- Device slots are disabled by default with `KASA_DEVICE_<n>_ENABLED=false` so an empty scaffold does not create invalid configured devices.
- Phase 1 remains local read-only status only: no outbox forwarding, no live commands, and no continuous discovery scan.

## Data Models

| Model | Purpose | Notes |
|-------|---------|-------|
| `KasaDeviceConfig` | Static HVO configuration for one device. | Device ID, source ID, configured/current host, MAC address when known, expected model, polling interval, capability metadata, safety classification. Starts in config file and should map cleanly to database columns later. |
| `KasaNetworkConfig` | Subnet/discovery configuration. | Allows observatory/home networks to be scanned separately and reported separately. |
| `KasaCapabilitySet` | Observed/configured capabilities for one device. | Avoid hard-coding behavior by model only; model/firmware may still matter. |
| `KasaDeviceMetadata` | Optional non-state metadata known about a device. | Schedule/countdown/away/LED/diagnostic availability and values should be modeled even when write operations are deferred. |
| `KasaDeviceSnapshot` | Current state displayed locally and optionally forwarded. | Keep vendor fields and HVO normalized values separated. |
| `KasaOutletSnapshot` | Current state for top-level or child outlet. | Needed for HS300/KP200 multi-outlet shapes. |
| `KasaEnergySnapshot` | Optional realtime power telemetry. | Only present for supported energy-meter devices. |
| `KasaLightSnapshot` | Optional bulb light state. | Status-only for KL130; commands deferred. |
| `KasaGatewayStatus` | Gateway/device health state. | Should align with common gateway standards. |

## Worker Flow

1. Load gateway and device configuration.
2. Validate network/device configuration and mark missing/placeholder settings as misconfigured.
3. For normal gateway operation, do not scan for new devices.
4. For operator-initiated add-device/setup utility workflows, accept a specific IP or MAC when provided, or perform read-only discovery by requested network/target and record responder counts per scan.
5. Merge discovered devices with the static configured registry by stable device ID, not by IP address.
6. Use configured IP, ARP/MAC, or discovery results as locator hints only; update the current/last-known host for a known device after identity validation.
7. Poll each configured legacy device on its interval using its current locator.
8. Read `system.get_sysinfo` first.
9. Validate the connected device ID and any configured guard fields before accepting the poll result.
10. Parse status based on observed shape:
   - top-level `relay_state` for EP25/HS105 plugs and HS200/HS210/HS220 switches.
   - `children[].state` for HS300/KP200-style multi-outlet devices.
   - `light_state.on_off` for KL130/LB230-style bulbs.
11. Opportunistically read `emeter.get_realtime` only after identity validation succeeds so energy-capable devices such as EP25 do not require preconfigured capability flags.
12. Treat unsupported unconfigured realtime energy as a normal non-degraded result; malformed/network failures still degrade a device when energy is configured or the failure is not a clean unsupported response.
13. Convert observed energy milli-units to normalized display units only after preserving raw values locally.
14. Read safe typed metadata for schedule, countdown, away mode, time/timezone, firmware/cloud diagnostics, and HS220 dimmer details.
15. Update current local snapshots and health state.
16. Capture supported read-only metadata and availability flags without assuming every device supports every field.
17. Defer outbox enqueue/forwarding until local discovery, configuration/database mapping, identity validation, metadata, and status semantics are stable.

## Command Flow

Commands are part of the eventual gateway/library surface, but live command execution is gated.

1. Operator selects a configured device by device ID, not IP address.
2. Gateway resolves the current locator from configuration/discovery/ARP hints.
3. Gateway connects and reads `system.get_sysinfo`.
4. Gateway validates device ID and configured guard fields.
5. Gateway validates command capability and safety class.
6. Gateway displays the intended command, current state, target identity, and connected-load metadata when available.
7. Operator explicitly approves the specific live command.
8. Gateway sends the command, reads back state, records audit/status, and marks failures clearly.

No live command may be sent from automated tests, background polling, or cloud paths by default.

## Error Handling And Retries

| Scenario | HVO behavior | Notes |
|----------|--------------|-------|
| TCP timeout/connect failure | Mark device offline/degraded; continue polling other devices. | Do not crash gateway. |
| Connected device ID mismatch | Mark configured device as identity mismatch; do not accept telemetry and never run commands. | Protects against DHCP/IP reuse causing wrong-device control. |
| MAC/ARP mismatch | Treat as locator suspect; force sysinfo identity validation before using the address. | MAC is a hint, not the primary identity. |
| Invalid length prefix or decrypt failure | Mark protocol error; increment decode failure metric. | Fake server tests should cover. |
| Malformed JSON response | Mark protocol error; do not enqueue telemetry. | Dead-letter not needed if payload is never enqueued. |
| Unsupported `emeter` module | Record capability unsupported; continue system-info polling. | Expected for non-energy devices. |
| Intermittent/offline device | Keep last known snapshot with stale/offline health; continue polling other configured devices. | Live scans observed 21 then 22 responders with different timeouts. |
| Cloud/outbox failure | Use shared outbox retry/dead-letter classification. | Do not block current local polling. |

## Safety Decisions

| Decision | Reason | Notes |
|----------|--------|-------|
| Commands require explicit live approval | Avoid accidental control of critical loads. | Ask every time before sending a live command until a later safety policy changes this. |
| No arbitrary JSON command endpoint | Would bypass command allowlist and safety checks. | Debug-only tooling can be separate if ever needed. |
| Native app remains primary | Pairing, firmware, account/cloud, schedules, and complex device management are vendor-owned. | HVO local UI focuses on status/diagnostics. |
| Tapo/new Kasa auth deferred | Observed devices fit legacy TCP `9999`; newer authenticated protocols add credential and transport complexity. | Revisit only if future hardware requires it. |

## Implementation Decision Log

| Decision | Status | Rationale | Consequences / follow-up |
|----------|--------|-----------|--------------------------|
| Start with legacy Kasa LAN read-only | Proposed | Matches observed installed responders and has the best simulator coverage. | Confirm production subset before deploy. |
| File config first, database later | Proposed | Faster prototype while preserving the future need for durable inventory and outbox propagation. | Config schema should map cleanly to database tables. |
| Build device library/configuration before outbox | Proposed | Inventory, metadata, and control semantics must be stable before cloud payloads are useful. | Outbox work follows local registry, database mapping, polling, and status UI. |
| Discovery is operator initiated | Proposed | Avoid constant network scanning and accidental broad probes. | Discovery belongs in an add-device/configuration workflow. |
| Same API-key auth pattern as Davis | Proposed | Keeps gateway local API auth consistent. | Apply to local status/config APIs before deployment. |
| Start with basic inventory/status UI | Proposed | Prioritize library correctness and identity safety. | Expand UI after protocol/capability model stabilizes. |
| Use device ID as primary identity | Proposed | IP addresses can change and may be reassigned by DHCP. | All polling/commands must validate identity after connect. |
| Require IP and MAC for configured devices when known | Proposed | IP is the direct connection locator; MAC is more useful for finding a device after IP changes. | If MAC is missing, support direct-IP polling but do not promise offline rediscovery. |
| Treat MAC/IP as locator hints | Proposed | MAC can support ARP-assisted lookup, but it is still secondary to device ID. | Discovery may update host locators only after identity validation. |
| Treat installed legacy devices as a heterogeneous capability set | Proposed | Live scan observed plugs, power strips, dual outlets, light switches, 3-way switches, dimmers, and multiple bulb models with different capability shapes. | Parser tests need fixtures for all observed shapes. |
| Prefer capability composition over per-model inheritance | Proposed | The same protocol family spans many model-specific capability combinations. | Use model/firmware as detection hints, not the main type system. |
| Model all available read-only metadata | Proposed | Energy, schedules, countdown, away mode, LED, diagnostics, firmware, and similar availability affect UI, config, and cloud contracts. | Poll/control support can be staged, but model shape should not ignore known device data categories. |
| Capabilities mean observed/configured availability | Accepted | Firmware/model differences mean a protocol command existing is not enough. | Live validation corrected the detector so schedule/LED metadata is only marked supported after successful read or explicit config. |
| Probe realtime energy after identity validation | Accepted | Real EP25 hardware supports energy even when config lacks `EnergyRealtime`; capability should be discovered from the read. | Unsupported unconfigured energy is non-degrading; successful energy adds `EnergyRealtime` capability and `/status` values. |
| Expose typed read metadata in local `/status` | Accepted | App-comparable read data should not require ad hoc probe scripts after deployment. | Public status includes safe counts/status/mode fields but still excludes raw vendor JSON, aliases, MACs, device IDs, hosts, and cloud/account values. |
| Use shared outbox standards | Proposed | New gateway should not duplicate Davis-specific outbox behavior. | May require `HVO.Edge.Outbox` failure-kind updates first. |
| Build in-process fake server | Proposed | Keeps tests deterministic without Node/npm simulator dependency. | Compare behavior against `plasticrake` simulator later. |
| Defer commands | Proposed | Load safety unknown. | Add command design only after device/load inventory. |

## Implementation Caveats And Debt

| Caveat | Impact | Follow-up |
|--------|--------|-----------|
| Connected loads and production subset unknown | Cannot safely expose commands or decide final cloud payload scope. | Operator inventory and safety classification. |
| Initial config file is not durable inventory | Later outbox propagation needs a database-backed inventory/configuration source. | Design config records to map cleanly to database rows. |
| Full device inventory incomplete | More devices may be reset/rejoined later, likely moving from `192.168.2.0/24` to `192.168.9.0/24`. | Keep discovery/config update path simple and repeatable. |
| Home switch/bulb network depends on hvo.lan/Tailscale routing | `192.168.9.0/24` returned 20 legacy responders after route updates and added devices. | Keep per-network discovery status visible so route regressions are obvious. |
| Community protocol references, not official docs | Vendor could change protocol/behavior. | Capture live fixtures and cite library/source behavior. |
| Shared outbox lacks failure kind today | New gateway would either extend shared outbox or temporarily duplicate behavior. | Update `HVO.Edge.Outbox` before or during implementation. |
| Central ingest contract for outlet/power-device telemetry not finalized | Outbox payload may need new contract. | Design after confirmed device capabilities. |

## Implementation Readiness Assessment

| Area | Current HVO status | Readiness | Next action |
|------|--------------------|-----------|-------------|
| Documentation | Baseline in progress | Medium | Keep PR updated with discovery/config decisions. |
| Legacy XOR protocol | Research complete enough for prototype | Medium-high | Implement cipher/framing tests. |
| Capability model | Prototype implemented/tested | Medium-high | Add more fixtures before locking cloud contracts. |
| Device library/configuration | Prototype implemented | High priority | Add registry/database mapping before outbox. |
| Device model/firmware | Sanitized live scan captured initial legacy models plus home switch models | Medium-high for observed legacy scope | Confirm production subset and connected loads. |
| Simulator/mock | In-process fake implemented/tested | High | Compare behavior against external simulator later if needed. |
| Outbox/cloud | Common standard exists; code needs extension | Medium | Add shared failure kind/requeue support before production. |
| Commands | Deferred | Low | Require safety design. |
