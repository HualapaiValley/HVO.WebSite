# Davis Vantage Pro2 collector manual

## Status

- Current source: headless direct collector with shared Edge runtime, durable live/archive outbox and optional read-only HA MQTT, Weather Underground and CWOP presentation/publication.
- Documentation reconciled: 2026-10-04. This source inspection is not a new deployment or hardware attestation. [#346](https://github.com/HualapaiValley/HVO.WebSite/issues/346) still owns bounded circular-buffer recovery; production archive catch-up remains disabled.
- Protocol coverage is partial: selected low-level write helpers exist, but no local management UI, `/api/weather/current`, generic write endpoint or cloud command path is mapped by the [collector entry point](../../../src/HVO.Hardware.DavisVantagePro2/Program.cs).

## Document Map

| Document | Purpose | Audience |
|----------|---------|----------|
| [manufacturer-protocol.md](manufacturer-protocol.md) | Davis-defined protocol, commands, packet shapes, encodings, units, and quirks. | Anyone implementing a Davis driver. |
| [hvo-implementation.md](hvo-implementation.md) | HVO code structure, classes, public methods, design decisions, and implementation caveats. | HVO developers maintaining the gateway. |
| [hvo-api-contracts.md](hvo-api-contracts.md) | Current headless endpoints, mounted configuration, typed live/archive payloads, central mappings and explicitly proposed contracts. | Collector/website developers. |
| [console-fields-and-settings.md](console-fields-and-settings.md) | Reusable LOOP/non-LOOP fields, EEPROM/display units, calibration, transmitters, alarms and write limitations extracted from the old inventory. | Protocol maintainers. |
| [validation-notes.md](validation-notes.md) | Evidence, open questions, live validation checklist, and unresolved research items. | HVO developers validating hardware behavior. |
| [cutover-and-rollback.md](cutover-and-rollback.md) | Production authority, one-owner cutover, archive recovery, validation, and rollback. | HVO operators and deployment reviewers. |
| [weather-underground-deployment.md](weather-underground-deployment.md) | PWS protocol mapping, credentials, disabled-first rollout, diagnostics, verification, and rollback. | HVO operators and deployment reviewers. |
| [cwop-deployment.md](cwop-deployment.md) | Distinct APRS-IS mapping, latest-only publication, credentials and controlled rollout. | HVO operators and deployment reviewers. |

## References

| Type | Reference | Status | Notes |
|------|-----------|--------|-------|
| Official/protocol PDF | [Davis Serial Communication Reference Manual Rev 2.6.1](../../VantageSerialProtocolDocs_v261.pdf) | Preserved unchanged | Vendor reference, independent of HVO support status. |
| Official/protocol PDF URL | `https://cdn.shopify.com/s/files/1/0515/5992/3873/files/VantageSerialProtocolDocs_v261.pdf` | Found | Public copy of the same manual. |
| Historical inventory | [Complete May 8 inventory](../../archive/2026-05-08-davis-console-inventory.md) | Superseded | Original field/UI/settings proposals, source/date and verified historical UI links. |
| HVO code | [Collector source](../../../src/HVO.Hardware.DavisVantagePro2) | Current | Implementation source of truth. |
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
| HVO presentation responsibility | Shared protected diagnostics and bounded HA MQTT current state; no local UI |
| HVO safety classification | Runtime acquisition/presentation is telemetry; low-level setting/alarm/archive-clear methods require separately authorized controlled maintenance and safety design before exposure |

## Capabilities Summary

| Capability group | Read-only | Read-write | Command/action | Local UI | Outbox/cloud | Notes |
|------------------|-----------|------------|----------------|----------|--------------|-------|
| Live weather LOOP values | Yes | No | No | No | Yes | Complete typed live outbox; central raw schema persists its selected subset. |
| Archive records | Yes | No | Low-level archive clear exists | No | Yes | Separate typed archive contract; recurring catch-up disabled pending #346. |
| Console identity/time | Yes | Low-level time helper | Not an HTTP/MQTT command | No | Local metadata | Console clock affects archive timestamp conversion. |
| EEPROM settings | Yes | Selected low-level helpers | Not an HTTP/MQTT command | No | Local settings snapshots | Archive interval, coordinates, altitude, rain bucket and timezone. |
| Calibration | Yes | Selected low-level helpers | Not an HTTP/MQTT command | No | Config history proposed | Temperature/humidity/wind offsets; no new live write proof. |
| Alarm thresholds | Yes | Selected low-level helpers | Low-level clear helpers | No | Alarm history proposed | Console threshold support does not implement alarm routing/history. |
| Reception diagnostics | Yes | No | No | No | Selected shared diagnostics | `RXCHECK` and `RECEIVERS` are protocol methods, not standalone web endpoints. |

[Project setup](../../../src/HVO.Hardware.DavisVantagePro2/README.md), [gateway operations](../../GATEWAY_OPERATIONS.md), [Pi deployment](../../../deploy/pi-gateways/README.md) and the [SQLite recovery contract](../sqlite-backup-and-rollback.md) own configuration, rollout and recovery commands. Davis currently consumes [Staging](../../../src/HVO.Staging) for production moon calculations; [#372](https://github.com/HualapaiValley/HVO.WebSite/issues/372) must migrate that consumer after its SDK/package prerequisite.
