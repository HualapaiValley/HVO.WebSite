# Phase 0 Gateway Capability Inventory

Status: active discovery plan.

Phase 0 is a research and design inventory. It should happen before committing to shared local API shapes, outbox payloads, central database tables, or main-site UI assumptions.

## Objective

Create implementer-grade documentation sets for each gateway or adjacent system. A set is complete enough when another developer could write a compatible local gateway, parser, poller, or proxy using the manufacturer/protocol document plus linked references, and could maintain HVO's implementation using the HVO implementation/API documents.

## Documentation Rules

- Do not invent device/API fields, enum values, commands, response structures, or possible values.
- Keep HVO metadata separate from vendor/API fields. Examples of HVO metadata include manual subject, integration role, UI level, safety classification, source IDs, storage aliases, and normalized field names.
- Only put a name in a device `Fields` or `API Calls / Protocol Operations` table when it is documented, captured, or present in existing implementation code.
- If HVO creates an alias or normalized field, show both the vendor/API source name and the HVO name.
- If a value is inferred, label it `Needs validation` and record the evidence.
- Split complex gateways into `README.md`, `manufacturer-protocol.md`, `hvo-implementation.md`, `hvo-api-contracts.md`, and `validation-notes.md` before local/cloud contracts are locked.

## Why This Exists

Gateway features have been discovered while building downstream cloud and UI work. That causes model churn when a later device exposes a field, command, reset behavior, or capability shape the existing contract did not anticipate.

Phase 0 reduces that risk by discovering device capabilities up front:

- what each system can read
- what each system can write
- which actions are commands rather than state
- which values are optional or device/model dependent
- which data is local-only, cloud-worthy, or safety-restricted
- which systems have native UIs that HVO should link/proxy rather than replace

## Inventory Dimensions

| Dimension | Meaning |
|-----------|---------|
| Integration role | Source, consumer, both, proxy, standalone, safety controller. |
| UI responsibility | None, health-only, status/proxy, operational dashboard, full local management. |
| Safety class | Telemetry-only, low-risk command, high-risk command, safety-critical. |
| Communication method | TCP, BLE, REST, MQTT, WebSocket, HTTP form, file drop, webhook, vendor cloud. |
| Capability type | Read-only telemetry, read-write setting, command/action, derived value, local-only state. |
| Data semantics | Instantaneous, cumulative counter, interval total, configuration, metadata, alarm, event. |
| Cloud treatment | Do not send, send as telemetry, send as event, send as config snapshot, expose as proxy only. |

## UI Responsibility Levels

| Level | Name | Description | Examples |
|------:|------|-------------|----------|
| 0 | No HVO UI | API/health only. | Internal shared service. |
| 1 | Status/proxy | HVO shows health, last event, and links/proxies native assets. | Blue Iris, AllSky. |
| 2 | Operational dashboard | HVO shows live state, status, configuration summary, and limited operations. | JK BMS, SmartShunt. |
| 3 | Full local management | HVO owns rich local UI for settings, diagnostics, and operations. | Davis, roof/dome if appropriate. |

## Initial Capability Matrix

| System | Role | Primary communication | Native UI | HVO UI level | Safety class | Current project/docs |
|--------|------|-----------------------|-----------|--------------|--------------|----------------------|
| Davis Vantage Pro2 | Source | TCP serial protocol via WeatherLink/IP adapter | Console only | 3 | Settings/commands include high-risk actions | `src/HVO.Hardware.DavisVantagePro2`, `docs/VantageSerialProtocolDocs_v261.pdf` |
| JK BMS | Source | BLE GATT UART-like frames | Vendor mobile app | 2 | Telemetry now; writes not proven | `src/HVO.Hardware.JkBms` |
| SolarAssistant | Source/proxy | REST, MQTT, WebSocket observed | Yes | 1-2 | Command topics exist but HVO is read-only | `src/HVO.Gateway.SolarAssistant` |
| Victron SmartShunt | Source | BLE public service plus private enrichment | VictronConnect | 2 | Telemetry now; writes deferred | `src/HVO.Hardware.VictronSmartShunt` |
| Govee | Source TBD | BLE/LAN/cloud TBD | App/cloud likely | TBD | TBD | No project yet |
| TP-Link/Kasa | Source/controller TBD; initial implementation is source/status only | Legacy LAN TCP `9999` confirmed for observed EP25/HS300/KP200/HS105/KL130 responders; UDP discovery and newer Kasa/Tapo auth protocols deferred | Kasa/Tapo app | 1-2 | Commands likely but deferred until connected-load safety is documented | `src/HVO.Gateway.TplinkKasa`, `tests/HVO.Gateway.TplinkKasa.Tests`, and split docs in `docs/gateways/tplink-kasa/` |
| Digital Loggers | Source/controller | HTTP/API TBD | Web UI | 2 | Commands control power outlets | No project yet |
| Blue Iris | Event source/media proxy | HTTP/JSON/webhooks TBD | Yes | 1 | Commands possible but not primary | No project yet |
| AllSky Camera | Standalone/consumer/provider | HTTP/file/API TBD | Yes | 1 | Mostly telemetry/media | Existing legacy/v9 image models only |
| Roof/Dome | Safety controller/source | Existing API TBD | Own system | 2-3 | Safety-critical | External API docs needed |
| Motion Sensors TBD | Event source | TBD | TBD | 1 | Event/notification | Hardware not selected |

## Research Depth Required

For every system, gather:

- official vendor documentation
- API/protocol docs and examples
- source repositories or reverse-engineering references
- library references and license implications
- exact hardware model and firmware/software version
- authentication, authorization, and secret handling
- transport details and message framing
- polling, subscription, event, or webhook behavior
- rate limits, timing constraints, retries, reconnect requirements
- field list with data types, units, ranges, possible enum values, null/not-available values, reset behavior, and timestamps
- command list with parameters, side effects, safety risk, idempotency, confirmation requirements, and whether cloud command is allowed
- local configuration values and which are runtime-editable
- known quirks, vendor bugs, model-specific differences, and undocumented behavior
- test/simulation options

## Design Questions To Answer After Manuals Exist

1. Which domains need typed models and which need generic capability/measurement models?
2. Which values are shared across devices but optional by capability?
3. Which values must stay local-only?
4. Which commands are never allowed from cloud paths?
5. Which systems should HVO proxy/link to instead of replacing their native UI?
6. Which gateway-local APIs should be standardized across all services?
7. Which outbox envelope metadata is mandatory for all payloads?
8. Which central storage tables need sparse/optional fields versus typed child tables?
9. Which devices can be simulated in tests without live hardware?

## Completion Criteria

Phase 0 is complete when:

- every known/planned system has a documentation set or placeholder manual, even if some sections are marked `Needs validation`
- every current repo-backed gateway documentation set has been checked against code and at least one external reference
- every command/write path is classified by safety and cloud eligibility
- every high-cardinality or ambiguous telemetry family has a proposed local/cloud treatment
- a reviewed design note recommends shared local API, outbox, and central storage patterns based on the inventory
