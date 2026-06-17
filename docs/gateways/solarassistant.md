# SolarAssistant Gateway Manual

## Status

- Phase 0 status: seeded from current live discovery documents and gateway code.
- Last updated: 2026-06-17
- Confidence: medium-high for observed REST/MQTT inventory; official docs cross-check needed.
- Primary references: `src/HVO.Gateway.SolarAssistant`, `docs/SOLARASSISTANT_DISCOVERY.md`, `docs/FUTURE_WORK.md`.

## Identity

This table is HVO documentation metadata unless a row explicitly references a SolarAssistant-discovered device value. It is not a list of SolarAssistant API fields.

| Field | Value |
|-------|-------|
| HVO manual subject | SolarAssistant |
| HVO integration role | Source/proxy; native system remains primary UI |
| Observed device | Axpert Max, manufacturer Voltronic, model Axpert Max |
| HVO project/service | `src/HVO.Gateway.SolarAssistant` |
| HVO deployment target | Pi gateway container, compose file `deploy/pi-gateways/solarassistant/docker-compose.yml` |
| Native UI exists | Yes |
| Native UI is primary | Yes for full SolarAssistant/inverter management |
| HVO UI responsibility | Level 1-2: local status, inventory, health, charts, and diagnostics |
| HVO safety classification | Read-only currently; command topics observed but not written |

## Communication Summary

| Field | Value |
|-------|-------|
| Transport | REST, MQTT; WebSocket observed |
| REST endpoint | `/api/v1/metrics` |
| MQTT subscriptions | `homeassistant/#`, `solar_assistant/#` |
| WebSocket | `/api/websocket` observed during discovery, not primary collector |
| Authentication | REST basic or bearer token; MQTT username/password |
| Polling/subscription model | REST polling plus read-only MQTT subscription |
| Current live inventory | REST 124 metrics; MQTT 90 unique topics; 48 HA entities; 42 state topics; 14 command topics |

## References

| Type | Reference | Status | Notes |
|------|-----------|--------|-------|
| Existing discovery | `docs/SOLARASSISTANT_DISCOVERY.md` | Found | Sanitized live discovery. |
| Future work | `docs/FUTURE_WORK.md` | Found | Remaining roadmap after the implemented SolarAssistant rollout. |
| REST client code | `SolarAssistantRestClient.cs` | Found | Polls `/api/v1/metrics`. |
| MQTT code | `SolarAssistantMqttClient.cs`, inventory store/worker | Found | Discovery/state/command inventory. |
| Official docs | SolarAssistant API/docs | Needed | Must validate endpoints, auth, units, and command behavior. |

## Capabilities Summary

| Capability group | Read-only | Read-write | Command/action | Local UI | Outbox/cloud | Notes |
|------------------|-----------|------------|----------------|----------|--------------|-------|
| Aggregate power | Yes | No | No | Yes | Yes | Existing `power.reading.v1`. |
| Energy counters | Yes | No | No | Candidate | Yes typed stream | Reset/sign semantics documented in rollout. |
| Inverter detail/PV strings | Yes | No | No | Candidate | Yes typed stream | Useful diagnostics. |
| Device inventory | Yes | No | No | Yes | Low-frequency snapshot | Firmware/model/serial should not be high-cadence. |
| Configuration/selects | Yes | Possibly via MQTT command topics | Command topics observed | Inventory only | Config snapshot/inventory only | No HVO writes currently. |
| Gateway health | Yes | Application config only | No | Yes | Yes | REST/MQTT freshness, outbox status. |

## Observed Interfaces

| Interface | Result | Notes |
|-----------|--------|-------|
| REST `/api/v1/metrics` | 124 metrics | Prefixes: `inverter_1` 102, `total` 17, `battery_1` 5. |
| MQTT `#` | 90 unique topics | 48 HA discovery entities and 42 state topics. |
| WebSocket `/api/websocket` | 104 definition/data topics | Observed, not primary collector. |

## Fields: Current Aggregate Power Candidates

| HVO field | SolarAssistant topics | Type | Unit | Access | Semantics | Cloud treatment |
|-----------|-----------------------|------|------|--------|-----------|-----------------|
| PvPowerW | `total/pv_power` | double? | W | Read-only | Instantaneous | `power.reading.v1` |
| LoadPowerW | `total/load_power` | double? | W | Read-only | Instantaneous | `power.reading.v1` |
| GridPowerW | `total/grid_power` | double? | W | Read-only | Instantaneous | `power.reading.v1` |
| BatteryPowerW | `total/battery_power` | double? | W | Read-only | Instantaneous | `power.reading.v1` |
| SystemPowerW | `total/system_power`, `total/power` | double? | W | Read-only | Instantaneous | `power.reading.v1` |
| BatteryStateOfChargePercent | `total/battery_state_of_charge` | double? | % | Read-only | Instantaneous | `power.reading.v1` |
| BatteryVoltageV | `total/battery_voltage`, `battery_1/voltage` | double? | V | Read-only | Instantaneous | `power.reading.v1` |
| BatteryCurrentA | `total/battery_current`, `battery_1/current` | double? | A | Read-only | Instantaneous | `power.reading.v1` |
| BatteryCapacityKwh | `total/battery_capacity`, `battery_1/capacity` | double? | kWh | Read-only | Capacity/config-ish | `power.reading.v1` currently |
| GridVoltageV | `total/grid_voltage`, `inverter_1/grid_voltage` | double? | V | Read-only | Instantaneous | `power.reading.v1` |
| GridFrequencyHz | `total/grid_frequency`, `inverter_1/grid_frequency` | double? | Hz | Read-only | Instantaneous | `power.reading.v1` |
| OutputVoltageV | `total/ac_output_voltage`, `inverter_1/ac_output_voltage` | double? | V | Read-only | Instantaneous | `power.reading.v1` |
| OutputFrequencyHz | `total/ac_output_frequency`, `inverter_1/ac_output_frequency` | double? | Hz | Read-only | Instantaneous | `power.reading.v1` |
| LoadPercentage | `total/load_percentage`, `inverter_1/load_percentage` | double? | % | Read-only | Instantaneous | `power.reading.v1` |
| InverterMode | `total/inverter_mode`, `inverter_1/device_mode` | string? | enum/string | Read-only | State | `power.reading.v1` |
| OutputSourcePriority | `total/output_source_priority`, `inverter_1/output_source_priority` | string? | enum/string | Read-only | Configuration/state | `power.reading.v1` |
| ChargerSourcePriority | `inverter_1/charger_source_priority` | string? | enum/string | Read-only | Configuration/state | `power.reading.v1` |

## API Calls / Protocol Operations

| Operation | Direction | Request/framing | Response/framing | Auth | Side effects | Timeout/retry | Notes |
|-----------|-----------|-----------------|------------------|------|--------------|---------------|-------|
| REST metrics poll | Gateway to SolarAssistant | HTTP GET `/api/v1/metrics` | JSON metric rows with topic/group/name/value/unit | Basic or bearer | None | Request timeout configured | Main REST source. |
| MQTT discovery/state subscribe | Gateway to MQTT broker | Subscribe `homeassistant/#`, `solar_assistant/#` | MQTT messages | MQTT credentials | None | Reconnect delay configured | Read-only. |
| MQTT command topics | HVO observes discovery topics ending in command/set paths | No publish | Discovery metadata | MQTT credentials | None because no writes | N/A | Inventory only. |
| WebSocket discovery | Manual/probe | `/api/websocket` | definition/data topics | Needs docs | None if read-only | TBD | Not current runtime path. |

## Local Configuration

| Setting | Type | Required | Secret | Runtime editable | Default | Notes |
|---------|------|----------|--------|------------------|---------|-------|
| `SolarAssistant.Host` | string | Yes to poll | No | App config | empty disables polling | Local SolarAssistant host/IP. |
| `SolarAssistant.RestPort` | int | Yes | No | App config | 80 | REST port. |
| `RestUsername` / `RestPassword` | string | If basic auth | Password secret | Secret/app config | admin/empty | Do not log. |
| `BearerToken` | string | If token auth | Secret | Secret/app config | empty | Alternative auth. |
| `EnableMqttDiscovery` | bool | No | No | App config | true | Enables read-only MQTT inventory. |
| `MqttUsername` / `MqttPassword` | string | If MQTT auth | Password secret | Secret/app config | empty | Do not log. |
| `SnapshotIntervalSeconds` | int | Yes | No | App config | 30 | REST poll cadence. |
| `GatewayStatusIntervalSeconds` | int | Yes | No | App config | 60 | Status payload cadence. |

## Local API Plan

Existing endpoints:

| Endpoint | Purpose | Auth | Response | Notes |
|----------|---------|------|----------|-------|
| `/status` | Current REST/local history/outbox/health summary | none/internal today | anonymous object | Existing. |
| `/inventory` | Sanitized REST topic metadata | none/internal today | latest inventory | Existing. |
| `/mqtt-inventory` | Sanitized MQTT discovery/state/command metadata | none/internal today | inventory object | Existing. |
| `/gateway-health` | Gateway health snapshot | none/internal today | health snapshot | Existing. |

## Local UI Plan

Current local UI includes:

- status dashboard
- REST inventory
- MQTT inventory
- rolling power history charts
- gateway health/outbox summary

Do not rebuild SolarAssistant's native management UI. HVO local UI should remain status, diagnostics, inventory, and links.

## Outbox / Cloud Candidate Streams

| Stream | Payload type | Cadence | Idempotency key | Cloud treatment | Notes |
|--------|--------------|---------|-----------------|-----------------|-------|
| power.reading | aggregate power | REST poll cadence | source + device + recordedAt | Existing central power reading | Implemented. |
| power.energy | cumulative counters | low frequency/when present | source + device + recordedAt | Typed central stream | Counter reset detection needed. |
| power.inverter-detail | PV string/load/inverter detail | low frequency/when present | source + device + recordedAt | Typed central stream | Bounded details only. |
| power.device-inventory | model/firmware/serial/device metadata | on change/low frequency | source + hash | Device inventory | Avoid high cadence. |
| power.configuration | read-only settings/selects | on change/low frequency | source + hash | Config snapshot | No writes. |
| gateway.status | REST/MQTT/outbox health | low frequency | gateway + recordedAt | Central gateway cards | Implemented. |

## Known Issues And Quirks

| Issue | Evidence | Impact | Workaround | Validation needed |
|-------|----------|--------|------------|-------------------|
| MQTT command topics exist | Live inventory found 14 command topics | Accidental writes could affect inverter settings | Inventory only, no publish path | Official docs and safety design. |
| Historical outbox failures can warn while current forwarding works | Deployment observations | Noisy health if not separated | Health split current vs historical | Continue shared outbox work. |
| Not every metric belongs in central history | Discovery docs | Storage bloat/ambiguous semantics | Typed streams and local-only classification | Official unit/reset cross-check. |

## Security And Safety Notes

- REST/MQTT credentials are local secrets.
- Do not log raw payloads that may contain credentials or private local topology.
- No MQTT publish/command path should be added without separate safety/auth/audit design.
- Main website should not directly reach LAN SolarAssistant; use gateway-forwarded typed streams and local dashboard links.

## Open Questions

| Question | Why it matters | Status |
|----------|----------------|--------|
| Where are official REST/MQTT/WebSocket docs and version guarantees? | Contract stability | Open |
| Which command topics are safe to inventory/display, and which should be hidden? | Prevent unsafe operator assumptions | Open |
| Should SolarAssistant local API require an API key on LAN? | Local network security | Open |
