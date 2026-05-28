# Govee Gateway Manual

## Status

- Phase 0 status: placeholder.
- Last updated: 2026-05-28
- Confidence: low; exact hardware is not selected or confirmed in repo.

## Identity

This table is HVO documentation metadata. It is not a list of Govee API fields.

| Field | Value |
|-------|-------|
| HVO manual subject | Govee environmental sensors |
| HVO integration role | Source TBD |
| Hardware model | Needs selection/confirmation |
| HVO project/service | None |
| Native UI exists | Likely Govee mobile/cloud app |
| Native UI is primary | TBD |
| HVO UI responsibility | TBD, likely Level 1-2 depending on local API quality |
| HVO safety classification | Telemetry-only unless controllable devices are selected |

## Research Targets

| Topic | What to find | Status |
|-------|--------------|--------|
| Exact model numbers | Sensor model, hardware revision, firmware | Needed |
| Communication options | BLE advertisement, BLE GATT, LAN API, cloud API, MQTT bridge options | Needed |
| Official docs | Govee developer/API docs, if applicable | Needed |
| Community references | Reverse-engineered BLE/cloud libraries and examples | Needed |
| Local-only viability | Whether readings can be collected without cloud dependency | Needed |
| Battery/RSSI fields | Whether sensor health is available | Needed |

## Expected Capability Areas

| Capability group | Read-only | Read-write | Command/action | Local UI | Outbox/cloud | Notes |
|------------------|-----------|------------|----------------|----------|--------------|-------|
| Temperature | Likely | No | No | Yes | Candidate | Units and precision TBD. |
| Relative humidity | Likely | No | No | Yes | Candidate | Optional by model. |
| Battery level | Maybe | No | No | Yes | Gateway status candidate | TBD. |
| Signal/RSSI | Maybe | No | No | Diagnostics | Local-only likely | TBD. |

## Design Notes

- Prefer hardware that supports local BLE or LAN access without mandatory cloud dependency.
- Treat Govee as an environmental source that may not support the same field set as Davis.
- Do not force Govee into the Davis weather schema; use capabilities or a sparse environmental observation model.

## Open Questions

| Question | Why it matters | Status |
|----------|----------------|--------|
| Which Govee devices will be purchased? | Determines API/protocol and fields | Open |
| Is local BLE sufficient for reliable gateway use? | Avoids cloud dependency | Open |
| Should Govee readings join weather data or a separate environment-sensor model? | Central storage design | Open |
