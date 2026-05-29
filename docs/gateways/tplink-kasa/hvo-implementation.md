# TP-Link / Kasa HVO Implementation Plan

## Design Summary

No HVO TP-Link/Kasa gateway exists yet. The recommended first implementation is a device control/configuration library plus a read-only legacy Kasa LAN gateway targeting confirmed port `9999` devices. Sanitized live discovery found legacy TCP `9999` responders across observatory and home networks after hvo.lan/Tailscale routing was updated for `192.168.9.0/24`.

The gateway should use the common gateway standards from [../common-gateway-standards.md](../common-gateway-standards.md): shared identity, shared outbox lifecycle, shared health/status concepts, and common telemetry naming where possible.

## Proposed Scope

Initial implementation target:

- `src/HVO.Gateway.TplinkKasa`
- `tests/HVO.Gateway.TplinkKasa.Tests`
- device control/configuration library first, before outbox integration.
- static configured device list with host/IP, network/subnet, optional friendly name, expected model, expected hardware/software, expected child count, capability flags, and safety classification.
- optional legacy UDP discovery and/or read-only subnet scan after TCP polling is stable.
- read-only polling through legacy TCP `9999` Smart Home/XOR protocol for initial devices.
- support the observed status shapes: single-outlet top-level `relay_state`, multi-outlet `children[]`, and bulb `light_state`.
- no command endpoints for switching power.
- local dashboard/status only at first.
- shared edge outbox only after local device inventory/configuration and status polling are stable.

## Implemented Capabilities

| Capability | HVO status | Notes |
|------------|------------|-------|
| Legacy TCP XOR framing | Planned | Deterministic and testable with fake TCP server. |
| `system.get_sysinfo` | Planned | First read-only operation; sanitized live shapes captured for EP25, HS105, HS200, HS210, HS220, HS300, KP200, KL130, and LB230. |
| `emeter.get_realtime` | Planned if device supports it | Must handle unsupported module gracefully; EP25/HS300 returned milli-unit fields, HS105 returned unsupported response. |
| Legacy UDP discovery | Candidate after TCP polling | Need UDP framing validation. |
| Device commands | Deferred | Requires safety/auth/audit design. |
| New Kasa/Tapo auth/KLAP/AES | Deferred | Not needed for observed legacy responders; revisit only if future hardware requires it. |
| Matter | Out of scope | Treat as separate integration path. |

## Proposed Project Layout

| Path | Purpose |
|------|---------|
| `src/HVO.Gateway.TplinkKasa/Protocol/KasaXorCipher.cs` | Legacy XOR autokey encode/decode. |
| `src/HVO.Gateway.TplinkKasa/Protocol/KasaLegacyClient.cs` | TCP client for legacy port `9999` JSON commands. |
| `src/HVO.Gateway.TplinkKasa/Protocol/KasaCommands.cs` | Minimal read-only command builders. |
| `src/HVO.Gateway.TplinkKasa/Configuration/KasaGatewayOptions.cs` | Gateway, network, discovery, and device configuration. |
| `src/HVO.Gateway.TplinkKasa/Devices/KasaDeviceRegistry.cs` | Merge configured devices and discovered read-only inventory. |
| `src/HVO.Gateway.TplinkKasa/Devices/KasaDevicePoller.cs` | Poll configured devices and normalize current state. |
| `src/HVO.Gateway.TplinkKasa/Devices/KasaDeviceSnapshot.cs` | Current read-only device status model. |
| `src/HVO.Gateway.TplinkKasa/Devices/KasaSystemInfoParser.cs` | Parse observed single-outlet, multi-outlet, and bulb status shapes while preserving raw fields separately. |
| `src/HVO.Gateway.TplinkKasa/Outbox/` | Gateway-specific outbox writer/forwarder if central ingest exists. Prefer `HVO.Edge.Outbox`. |
| `src/HVO.Gateway.TplinkKasa/Components/Pages/Status.razor` | Local status dashboard. |
| `tests/HVO.Gateway.TplinkKasa.Tests/Fakes/FakeKasaLegacyServer.cs` | In-process TCP fake for XOR/framing and command responses. |

## Main Classes And Interfaces

| Class/interface | Responsibility | Used by |
|-----------------|----------------|---------|
| `KasaXorCipher` | Encode/decode legacy Smart Home TCP frames. | Client and fake server tests. |
| `IKasaLegacyClient` | Abstraction for read-only device commands. | Poller and tests. |
| `KasaLegacyClient` | Sends JSON commands over TCP `9999` with timeout/retry. | Poller. |
| `KasaDeviceRegistry` | Tracks configured devices, discovered responders, network location, expected shape, and capability flags. | Worker and local UI. |
| `KasaSystemInfoParser` | Converts observed vendor response shapes into HVO snapshots without hiding raw/vendor data. | Poller and tests. |
| `KasaDevicePoller` | Coordinates polling all configured devices. | Background worker and local UI. |
| `KasaGatewayWorker` | Hosted service for polling, outbox enqueue, and health state. | ASP.NET host. |
| `KasaDeviceSnapshot` | HVO current-state model. | UI/API/outbox. |

## Public Methods And Samples

### `KasaLegacyClient.SendAsync`

Purpose: send a raw legacy JSON command to one configured device and return the JSON response document.

Side effects: none beyond network I/O when used with read-only commands.

Validation/safety: command builders should be allowlisted; do not expose arbitrary JSON in runtime endpoints.

```csharp
using var response = await client.SendAsync(host, KasaCommands.GetSystemInfo(), ct);
```

### `KasaLegacyClient.GetSystemInfoAsync`

Purpose: read legacy device system info.

Side effects: none expected.

Validation/safety: response fields remain vendor/raw until fixtures confirm mappings.

```csharp
var info = await client.GetSystemInfoAsync(host, ct);
```

### `KasaLegacyClient.TryGetRealtimeEnergyAsync`

Purpose: read realtime energy meter fields when the device supports the `emeter` module.

Side effects: none expected.

Validation/safety: unsupported module should return no reading, not fail the whole device poll.

```csharp
var energy = await client.TryGetRealtimeEnergyAsync(host, ct);
```

## Data Models

| Model | Purpose | Notes |
|-------|---------|-------|
| `KasaDeviceConfig` | Static HVO configuration for one device. | Host, source/device IDs, expected model, polling interval, safety classification. |
| `KasaNetworkConfig` | Subnet/discovery configuration. | Allows observatory/home networks to be scanned separately and reported separately. |
| `KasaDeviceSnapshot` | Current state displayed locally and optionally forwarded. | Keep vendor fields and HVO normalized values separated. |
| `KasaOutletSnapshot` | Current state for top-level or child outlet. | Needed for HS300/KP200 multi-outlet shapes. |
| `KasaEnergySnapshot` | Optional realtime power telemetry. | Only present for supported energy-meter devices. |
| `KasaLightSnapshot` | Optional bulb light state. | Status-only for KL130; commands deferred. |
| `KasaGatewayStatus` | Gateway/device health state. | Should align with common gateway standards. |

## Worker Flow

1. Load gateway and device configuration.
2. Validate network/device configuration and mark missing/placeholder settings as misconfigured.
3. Optionally perform read-only discovery by configured network and record responder counts per subnet.
4. Merge discovered devices with the static configured registry without treating discovery-only devices as production telemetry sources until approved.
5. Poll each configured legacy device on its interval.
6. Read `system.get_sysinfo`.
7. Parse status based on observed shape:
   - top-level `relay_state` for EP25/HS105 plugs and HS200/HS210/HS220 switches.
   - `children[].state` for HS300/KP200-style multi-outlet devices.
   - `light_state.on_off` for KL130-style bulbs.
8. If configured/observed as energy-capable, read `emeter.get_realtime`.
9. Convert observed energy milli-units to normalized display units only after preserving raw values.
10. Update current local snapshots and health state.
11. Defer outbox enqueue/forwarding until local discovery, configuration, and status semantics are stable.

## Error Handling And Retries

| Scenario | HVO behavior | Notes |
|----------|--------------|-------|
| TCP timeout/connect failure | Mark device offline/degraded; continue polling other devices. | Do not crash gateway. |
| Invalid length prefix or decrypt failure | Mark protocol error; increment decode failure metric. | Fake server tests should cover. |
| Malformed JSON response | Mark protocol error; do not enqueue telemetry. | Dead-letter not needed if payload is never enqueued. |
| Unsupported `emeter` module | Record capability unsupported; continue system-info polling. | Expected for non-energy devices. |
| Intermittent/offline device | Keep last known snapshot with stale/offline health; continue polling other configured devices. | Live scans observed 21 then 22 responders with different timeouts. |
| Cloud/outbox failure | Use shared outbox retry/dead-letter classification. | Do not block current local polling. |

## Safety Decisions

| Decision | Reason | Notes |
|----------|--------|-------|
| No power-switching commands initially | Unknown connected loads and weak/no auth on legacy LAN protocol. | Commands require explicit safety design. |
| No arbitrary JSON command endpoint | Would bypass command allowlist and safety checks. | Debug-only tooling can be separate if ever needed. |
| Native app remains primary | Pairing, firmware, account/cloud, schedules, and complex device management are vendor-owned. | HVO local UI focuses on status/diagnostics. |
| Tapo/new Kasa auth deferred | Observed devices fit legacy TCP `9999`; newer authenticated protocols add credential and transport complexity. | Revisit only if future hardware requires it. |

## Implementation Decision Log

| Decision | Status | Rationale | Consequences / follow-up |
|----------|--------|-----------|--------------------------|
| Start with legacy Kasa LAN read-only | Proposed | Matches observed installed responders and has the best simulator coverage. | Confirm production subset before deploy. |
| Build device library/configuration before outbox | Proposed | Inventory and control semantics must be stable before cloud payloads are useful. | Outbox work follows local registry, polling, and status UI. |
| Treat installed legacy devices as a heterogeneous capability set | Proposed | Live scan observed plugs, power strips, dual outlets, light switches, 3-way switches, dimmers, and multiple bulb models with different capability shapes. | Parser tests need fixtures for all observed shapes. |
| Use shared outbox standards | Proposed | New gateway should not duplicate Davis-specific outbox behavior. | May require `HVO.Edge.Outbox` failure-kind updates first. |
| Build in-process fake server | Proposed | Keeps tests deterministic without Node/npm simulator dependency. | Compare behavior against `plasticrake` simulator later. |
| Defer commands | Proposed | Load safety unknown. | Add command design only after device/load inventory. |

## Implementation Caveats And Debt

| Caveat | Impact | Follow-up |
|--------|--------|-----------|
| Connected loads and production subset unknown | Cannot safely expose commands or decide final cloud payload scope. | Operator inventory and safety classification. |
| Home network likely undercounted | Some `192.168.2.0/24` devices may need Wi-Fi reset/rejoin after network changes. | Rescan after Wi-Fi recovery and update configuration. |
| Home switch network depends on hvo.lan/Tailscale routing | `192.168.9.0/24` returned 17 legacy responders after route updates. | Keep per-network discovery status visible so route regressions are obvious. |
| Community protocol references, not official docs | Vendor could change protocol/behavior. | Capture live fixtures and cite library/source behavior. |
| Shared outbox lacks failure kind today | New gateway would either extend shared outbox or temporarily duplicate behavior. | Update `HVO.Edge.Outbox` before or during implementation. |
| Central ingest contract for outlet/power-device telemetry not finalized | Outbox payload may need new contract. | Design after confirmed device capabilities. |

## Implementation Readiness Assessment

| Area | Current HVO status | Readiness | Next action |
|------|--------------------|-----------|-------------|
| Documentation | Baseline in progress | Medium | Keep PR updated with discovery/config decisions. |
| Legacy XOR protocol | Research complete enough for prototype | Medium-high | Implement cipher/framing tests. |
| Device library/configuration | Not implemented | High priority | Implement registry/options and read-only discovery before outbox. |
| Device model/firmware | Sanitized live scan captured initial legacy models plus home switch models | Medium-high for observed legacy scope | Confirm production subset and connected loads. |
| Simulator/mock | External simulator exists; in-process fake planned | High | Implement fake TCP server with fixture responses. |
| Outbox/cloud | Common standard exists; code needs extension | Medium | Add shared failure kind/requeue support before production. |
| Commands | Deferred | Low | Require safety design. |
