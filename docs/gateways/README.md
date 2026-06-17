# Gateway Manuals

This folder is the Phase 0 discovery area for observatory gateways and adjacent systems.

The goal is to build implementer-grade documentation before locking local models, outbox contracts, central storage, or main-site UI.

Each gateway should separate vendor-defined behavior from HVO implementation decisions. The manufacturer/protocol document should be useful to anyone building a compatible driver. HVO documents should explain how this repository uses that protocol, what classes were created, which APIs/outbox contracts exist, and what validation remains.

## Documentation Rules

- Do not invent device/API fields, enum values, commands, response structures, or possible values.
- Separate HVO documentation metadata from vendor/API fields.
- If a label is created by HVO for UI, storage, source identity, or organization, mark it as HVO metadata/local concept.
- Every device field, command, operation, enum, and data structure must cite or trace to official docs, external source references, live captures, or existing HVO code.
- Anything inferred but not proven must be marked `Needs validation`.
- Keep manufacturer/vendor protocol facts separate from HVO design choices, normalized names, code structure, API contracts, and validation notes.

## Documentation Set Structure

Use this structure for gateways with enough complexity to justify separate files:

| Document | Purpose |
|----------|---------|
| `README.md` | Gateway overview, status, references, identity, document map, capability summary. |
| `manufacturer-protocol.md` | Vendor-defined protocol/API: transport, auth, commands, request/response structures, packet layouts, field encodings, units, enums, quirks. |
| `hvo-implementation.md` | HVO code map, classes/interfaces, public methods, sample calls, worker flows, design decisions, caveats, known implementation debt. |
| `hvo-api-contracts.md` | Local endpoints, outbox payloads, central ingest mappings, aliases, idempotency, stream decisions, cloud treatment. |
| `validation-notes.md` | Evidence, live test steps, open questions, packet captures, unresolved behavior, `Needs validation` items. |

Small or placeholder systems can start as one file, but should split before APIs/contracts are locked.

## Manuals

| System | Manual | Status |
|--------|--------|--------|
| Davis Vantage Pro2 | [davis-vantage-pro2/](davis-vantage-pro2/) | Split docs baseline created; needs live validation and deeper field-by-field protocol transcription. |
| JK BMS | [jk-bms.md](jk-bms.md) | Seeded from parser/protocol code; needs external repo/manual validation. |
| SolarAssistant | [solarassistant.md](solarassistant.md) | Seeded from live discovery docs and gateway code; needs official docs cross-check. |
| Victron SmartShunt | [victron-smartshunt.md](victron-smartshunt.md) | Seeded from current plan/code; write paths intentionally unvalidated. |
| TP-Link/Kasa | [tplink-kasa/](tplink-kasa/) | Split docs baseline plus sanitized live read-only discovery; initial recommendation is legacy Kasa LAN read-only. |
| Future integration candidates | [future-integrations.md](future-integrations.md) | Consolidated placeholders for Govee, Digital Loggers, Blue Iris, AllSky, Roof/Dome, and Motion Sensors. |

## Shared Design Notes

| Topic | Document | Status |
|-------|----------|--------|
| Camera/media boundary | [camera-media-model.md](camera-media-model.md) | Seeded from legacy/v9 image models and Phase 0 discussion. |
| Common gateway standards | [common-gateway-standards.md](common-gateway-standards.md) | Current standard for shared outbox, telemetry, health/status, and gateway-specific extension points. |

## Process

1. Fill each documentation set from confirmed sources only.
2. Link official docs, source repositories, protocol captures, and live observations.
3. Mark uncertain behavior as `Needs validation` instead of treating it as fact.
4. Capture all vendor-readable fields, writable settings, and commands with units, ranges, possible values, reset behavior, and safety risk in the manufacturer/protocol document.
5. Capture HVO classes, public methods, usage samples, worker flows, and design decisions in the HVO implementation document.
6. Design outbox/cloud storage only after checking the gateway's field model against the wider capability inventory.

## Supporting Documents

- [Phase 0 Gateway Capability Inventory](PHASE_0_GATEWAY_CAPABILITY_INVENTORY.md)
- [Gateway documentation set template](templates/gateway-manual-template.md)
- Existing Davis inventory: [../davis-console-nonloop-values.md](../davis-console-nonloop-values.md)
- Current future-work roadmap: [../FUTURE_WORK.md](../FUTURE_WORK.md)
