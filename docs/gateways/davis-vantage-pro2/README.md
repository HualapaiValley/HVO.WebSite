# Davis Vantage Pro2 Documentation Set

## Status

- Phase 0 status: split baseline created from current HVO code, official Davis protocol PDF, existing Davis inventory, WeeWX, and CumulusMX research.
- Last updated: 2026-08-14
- Confidence: high for current HVO implementation and common Davis serial framing/CRC/LOOP/archive behavior; medium where large official protocol tables still need field-by-field transcription.

## Document Map

| Document | Purpose | Audience |
|----------|---------|----------|
| [manufacturer-protocol.md](manufacturer-protocol.md) | Davis-defined protocol, commands, packet shapes, encodings, units, and quirks. | Anyone implementing a Davis driver. |
| [hvo-implementation.md](hvo-implementation.md) | HVO code structure, classes, public methods, design decisions, and implementation caveats. | HVO developers maintaining the gateway. |
| [hvo-api-contracts.md](hvo-api-contracts.md) | HVO local APIs, outbox payloads, central ingest mappings, and candidate contract decisions. | HVO developers designing local/cloud contracts. |
| [validation-notes.md](validation-notes.md) | Evidence, open questions, live validation checklist, and unresolved research items. | HVO developers validating hardware behavior. |
| [cutover-and-rollback.md](cutover-and-rollback.md) | Production authority, one-owner cutover, archive recovery, validation, and rollback. | HVO operators and deployment reviewers. |
| [weather-underground-deployment.md](weather-underground-deployment.md) | PWS protocol mapping, credentials, disabled-first rollout, diagnostics, verification, and rollback. | HVO operators and deployment reviewers. |

## References

| Type | Reference | Status | Notes |
|------|-----------|--------|-------|
| Official/protocol PDF | `../../VantageSerialProtocolDocs_v261.pdf` | Found | Davis Serial Communication Reference Manual Rev 2.6.1. |
| Official/protocol PDF URL | `https://cdn.shopify.com/s/files/1/0515/5992/3873/files/VantageSerialProtocolDocs_v261.pdf` | Found | Public copy of the same manual. |
| Existing HVO inventory | `../../davis-console-nonloop-values.md` | Found | Detailed field/UI/settings inventory. |
| HVO code | `../../../src/HVO.Hardware.DavisVantagePro2` | Found | Current implementation source of truth. |
| WeeWX Vantage driver | `https://raw.githubusercontent.com/weewx/weewx/master/src/weewx/drivers/vantage.py` | Found | Mature open-source Davis implementation. |
| WeeWX CRC table | `https://raw.githubusercontent.com/weewx/weewx/master/src/weewx/crc16.py` | Found | CRC cross-check. |
| CumulusMX Davis driver | `https://raw.githubusercontent.com/cumulusmx/CumulusMX/master/CumulusMX/DavisStation.cs` | Found | Independent implementation cross-check. |

## Identity

This table is HVO documentation metadata unless a row explicitly references a Davis protocol value.

| Field | Value |
|-------|-------|
| HVO manual subject | Davis Vantage Pro2 weather station |
| HVO integration role | Source |
| Hardware model | Davis Vantage Pro2 family; exact console model is read via `WRD 12 4D` and `StationInfo` |
| HVO project/service | `src/HVO.Hardware.DavisVantagePro2` |
| HVO deployment target | Pi gateway container, compose file `deploy/pi-gateways/davis/docker-compose.yml` |
| Native UI exists | Console display; no rich web UI |
| Native UI is primary | No |
| HVO UI responsibility | Level 3: full local management UI |
| HVO safety classification | Mixed: telemetry-only for LOOP/archive; high-risk for archive clear and alarm/setting writes |

## Capabilities Summary

| Capability group | Read-only | Read-write | Command/action | Local UI | Outbox/cloud | Notes |
|------------------|-----------|------------|----------------|----------|--------------|-------|
| Live weather LOOP values | Yes | No | No | Yes | Partial | Central v9 currently stores slim raw weather fields only. |
| Archive records | Yes | No | Archive clear exists | Yes | Yes | Archive and live records have different semantics. |
| Console identity/time | Yes | Time writable | Set time | Partial | Mostly local | Console clock affects archive timestamp conversion. |
| EEPROM settings | Yes | Some fields | Yes | Yes | Mostly local/config snapshots | Includes archive interval, lat/lon, altitude, rain bucket, timezone. |
| Calibration | Yes | Yes | Yes | Yes | Local/config snapshot candidate | Temperature, humidity, wind direction offsets. |
| Alarm thresholds | Yes | Yes | Clear alarms/actions | Yes | Local/config snapshot candidate | Includes temperature, humidity, wind, UV/solar, rain, soil/leaf. |
| Reception diagnostics | Yes | No | No | Yes | Gateway health candidate | `RXCHECK` and `RECEIVERS`. |
