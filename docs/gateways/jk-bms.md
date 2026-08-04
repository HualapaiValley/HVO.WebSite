# JK BMS Gateway Manual

## Status

- Phase 0 status: seeded from current parser/protocol code and session lifecycle notes.
- Last updated: 2026-05-28
- Confidence: medium for implemented read paths; lower for settings-query/write paths.
- Primary references: `src/HVO.Hardware.JkBms`, `docs/JKBMS_SESSION_LIFECYCLE.md`, code reference to `https://github.com/syssi/esphome-jk-bms`.

## Identity

This table is HVO documentation metadata unless a row explicitly references a JK BMS protocol/device-info value. It is not a list of JK BMS API fields.

| Field | Value |
|-------|-------|
| HVO manual subject | JK BMS battery management system fleet |
| HVO integration role | Source |
| Hardware model | Configured JK BMS devices; vendor/model string read from device info, e.g. `JK-B2A24S15P` style |
| HVO project/service | `src/HVO.Hardware.JkBms` |
| HVO deployment target | Pi gateway container, compose file `deploy/pi-gateways/jkbms/docker-compose.yml` |
| Native UI exists | Vendor mobile app |
| Native UI is primary | For vendor writes/settings until HVO paths are proven |
| HVO UI responsibility | Level 2 operational dashboard |
| HVO safety classification | Telemetry-only currently; write/control paths are unapproved high-risk |

## Communication Summary

| Field | Value |
|-------|-------|
| Transport | BLE GATT |
| Protocol | JK UART-over-BLE style fixed command/response frames |
| Service UUID | `0000ffe0-0000-1000-8000-00805f9b34fb` |
| Notify/RX characteristic | `0000ffe1-0000-1000-8000-00805f9b34fb` |
| Documented TX characteristic | `0000ffe2-0000-1000-8000-00805f9b34fb` |
| Implementation write characteristic | FFE1 for targeted old-module devices; FFE2 produced no response in current implementation notes |
| Authentication | BLE pairing/connection only in current implementation; no app passcode write path used |
| Polling model | Connect sessions and poll cell-info per configured interval |
| Reconnect model | Session lifecycle refactor target: connect sequentially per adapter, keep healthy sessions polling, reconnect failed sessions |

## References

| Type | Reference | Status | Notes |
|------|-----------|--------|-------|
| Existing HVO code | `JkBmsProtocol.cs`, packet parsers, worker/session code | Found | Current implemented behavior. |
| HVO design doc | `docs/JKBMS_SESSION_LIFECYCLE.md` | Found | Target session model and known unknowns. |
| Source repo | `https://github.com/syssi/esphome-jk-bms` | Needs validation | Referenced by code comments for frame layouts. |
| Vendor docs/manuals | exact installed JK model docs | Needed | Needed before any writes/settings control. |
| BLE captures | installed devices | Needed | Needed for validating settings query and any write behavior. |

## Known Hardware / Firmware Variants

| Variant | Differences | Detection method | Impact |
|---------|-------------|------------------|--------|
| JK02_24S | Standard offsets in `CellInfoPacket` | Enabled-cells bitmask present at data `0x30` | Current primary layout. |
| JK02_32S / newer EC/EA | Pack-level fields shifted by `0x20`; bitmask may be absent | Bitmask zero, non-zero total voltage at `0x90` | Parser supports alternate offsets. |

## Frame Geometry And Data Types

| Item | Value |
|------|-------|
| Request frame | 20 bytes, starts `AA 55 90 EB`, last byte CRC8 byte-sum of first 19 bytes |
| Response frame | 300 bytes, starts `55 AA EB 90` |
| Response byte 4 | frame type |
| Response byte 5 | counter, ignored |
| Response bytes 6-298 | 293-byte payload |
| Response byte 299 | CRC8 byte-sum of bytes 0-298 |
| BLE notifications | Response delivered in 20-byte chunks and accumulated into 300-byte frame |

## API Calls / Protocol Operations

| Operation | Direction | Request/framing | Response/framing | Auth | Side effects | Timeout/retry | Notes |
|-----------|-----------|-----------------|------------------|------|--------------|---------------|-------|
| Activate | Host to BMS | command code `0x95` | settings frame expected if used | BLE connection | May wake/init device | Exchange timeout | Some firmware requires before cell info. |
| Get cell info | Host to BMS | command code `0x96` | frame type `0x02` | BLE connection | None | Exchange timeout | Hot-lane steady telemetry. |
| Get device info | Host to BMS | command code `0x97` | frame type `0x03` | BLE connection | None | Exchange timeout | Used on connect/reconnect. |
| Settings frame | BMS to host | spontaneous frame | frame type `0x01` | BLE connection | None | Captured opportunistically | Explicit settings-query behavior not proven. |

## Fields: Cell Info Frame `0x02`

| Name | Type | Unit | Source offset | Access | Semantics | Possible values/range | Local UI | Outbox | Cloud storage | Notes |
|------|------|------|---------------|--------|-----------|-----------------------|----------|--------|---------------|-------|
| CellVoltagesMv | list ushort | mV | `0x00` slots | Read-only | Instantaneous | 1-32 cells | Yes | Yes | Yes child rows | 24S/32S aware. |
| CellCount | byte | count | bitmask/voltage count | Read-only | Metadata | 1-32 | Yes | Yes | Partly config | Derived. |
| AverageCellVoltageMv | ushort | mV | `0x34` or `0x44` | Read-only | Instantaneous | 0+ | Candidate | Yes | Not currently central | Emitted by gateway, not persisted centrally today. |
| DeltaCellVoltageMv | ushort | mV | `0x36` or `0x46` | Read-only | Instantaneous | 0+ | Yes | Yes | Yes | Spread. |
| MaxVoltageCellIndex | byte | cell index | `0x38` or `0x48` | Read-only | Metadata | 0-32 | Candidate | Yes | Not currently central | 1-based, 0 none. |
| MinVoltageCellIndex | byte | cell index | `0x39` or `0x49` | Read-only | Metadata | 0-32 | Candidate | Yes | Not currently central | 1-based, 0 none. |
| CellResistancesMOhm | list ushort | mOhm | `0x3A` or `0x4A` | Read-only | Instantaneous/diagnostic | 0+ | Candidate | Yes | Yes child rows | Units use milli-ohm naming in code. |
| TotalVoltageMv | uint | mV | `0x70` or `0x90` | Read-only | Instantaneous | 0+ | Yes | Yes | Yes | Website accepts alias after PR #115 path. |
| CurrentMa | int | mA | `0x78` or `0x98` | Read-only | Instantaneous | signed | Yes | Yes | Yes | Positive = charge, negative = discharge (verified against deployed banks). |
| BatteryTemperature1C | double | deg C | `0x7C` or `0x9C` | Read-only | Instantaneous | sensor dependent | Yes | Yes | Yes |
| BatteryTemperature2C | double | deg C | `0x7E` or `0x9E` | Read-only | Instantaneous | sensor dependent | Yes | Yes | Yes |
| PowerTubeTemperatureC | double | deg C | `0x80` or `0x8A` | Read-only | Instantaneous | sensor dependent | Yes | Yes | Yes |
| AlarmBitmask | uint | bitmask | `0x82` or `0xA0` | Read-only | Alarm | bit flags | Yes | Yes | Yes | Parser comments list common bits. |
| BalancingCurrentMa | double | mA | `0x84` or `0xA4` | Read-only | Instantaneous | signed | Yes | Yes | Yes |
| BalancingActive | bool | boolean | `0x86` or `0xA6` | Read-only | State | false/true | Yes | Yes | Yes |
| StateOfChargePercent | ushort | % | `0x87` or `0xA7` | Read-only | Instantaneous | 0-100 expected | Yes | Yes | Yes | Website alias fixed in PR #115. |
| RemainingCapacityMah | uint | mAh | `0x88` or `0xA8` | Read-only | Instantaneous | 0+ | Yes | Yes | Yes |
| NominalCapacityMah | uint | mAh | `0x8C` or `0xAC` | Read-only | Config-ish telemetry | 0+ | Yes | Yes | Yes |
| CycleCount | uint | count | `0x90` or `0xB0` | Read-only | Lifetime counter | 0+ | Yes | Yes | Yes |
| CycleCapacityMah | uint | mAh | `0x94` or `0xB4` | Read-only | Lifetime counter | 0+ | Candidate | Yes | Yes |
| StateOfHealthPercent | ushort | % | `0x98` or `0xB8` | Read-only | Metadata/health | 0-100 expected | Yes | Yes | Yes | Website alias fixed in PR #115. |

## Fields: Settings Frame `0x01`

| Name | Type | Unit | Access | Semantics | Local UI | Outbox | Cloud storage | Notes |
|------|------|------|--------|-----------|----------|--------|---------------|-------|
| CellOvervoltageProtectionMv | uint | mV | Read-only currently | Configuration | Candidate | On change | Yes config snapshot | Write path not approved. |
| CellOvervoltageRecoveryMv | uint | mV | Read-only currently | Configuration | Candidate | On change | Yes config snapshot | |
| CellUndervoltageProtectionMv | uint | mV | Read-only currently | Configuration | Candidate | On change | Yes config snapshot | |
| CellUndervoltageRecoveryMv | uint | mV | Read-only currently | Configuration | Candidate | On change | Yes config snapshot | |
| BalancePressureDifferenceMv | uint | mV | Read-only currently | Configuration | Candidate | On change | Yes config snapshot | |
| BalanceStartingVoltageMv | uint | mV | Read-only currently | Configuration | Candidate | On change | Yes config snapshot | |
| BalancingEnabled | bool | boolean | Read-only currently | Configuration | Candidate | On change | Yes config snapshot | |
| Charge/discharge OCP fields | uint | mA/s | Read-only currently | Configuration | Candidate | On change | Yes config snapshot | See `SettingsPacket`. |
| Short-circuit fields | uint | us/s | Read-only currently | Configuration | Candidate | On change | Yes config snapshot | |
| Temperature protection fields | double | deg C | Read-only currently | Configuration | Candidate | On change | Yes config snapshot | |
| CellCount | byte | count | Read-only currently | Configuration | Yes | On change | Yes config snapshot | |
| NominalCapacityMah | uint | mAh | Read-only currently | Configuration | Yes | On change | Yes config snapshot | |
| ChargingEnabled | bool | boolean | Read-only currently | Configuration | Yes | On change | Yes config snapshot | |
| DischargingEnabled | bool | boolean | Read-only currently | Configuration | Yes | On change | Yes config snapshot | |

## Fields: Device Info Frame `0x03`

| Name | Type | Access | Semantics | Local UI | Outbox | Cloud storage | Notes |
|------|------|--------|-----------|----------|--------|---------------|-------|
| ManufacturerName | string | Read-only | Metadata | Yes | On change | Device info snapshot | Vendor/model ID. |
| HardwareName | string | Read-only | Metadata | Yes | On change | Device info snapshot | Hardware version. |
| FirmwareVersion | string | Read-only | Metadata | Yes | On change | Device info snapshot | Software/firmware version. |
| UptimeSeconds | uint | Read-only | Runtime | Candidate | Local-only likely | TBD | Not currently in central payload. |
| PowerOnCount | uint | Read-only | Lifetime counter | Candidate | Local-only likely | TBD | Not currently in central payload. |
| DeviceName | string | Read-only | Metadata | Yes | On change | Device info snapshot | User configured name. |
| ManufacturingDate | string | Read-only | Metadata | Yes | On change | Device info snapshot | Raw string. |
| SerialNumber | string | Read-only | Metadata | Yes | On change | Device info snapshot | Treat as potentially sensitive. |
| UserData | string | Read-only | Metadata | Candidate | On change | Device info snapshot | Free-form. |
| SetupPasscode | string | Read-only in parser | Secret | Do not show | Do not send | Never central | Parser can decode but central payload excludes it. |

## Local Configuration

| Setting | Type | Required | Secret | Runtime editable | Default | Notes |
|---------|------|----------|--------|------------------|---------|-------|
| `JkBms.ConnectTimeoutSeconds` | int | Yes | No | App config | 30 | 5-120. |
| `JkBms.ExchangeTimeoutSeconds` | int | Yes | No | App config | 10 | 1-60. |
| `JkBms.DefaultPollIntervalSeconds` | int | Yes | No | App config | 60 | 10-3600. |
| `JkBms.HciAdapter` | string | Yes | No | App config | hci0 | Shared BLE contention risk. |
| `JkBms.Devices[]` | list | Yes | No | App config | empty | Address, alias, adapter/poll overrides. |
| Outbox API endpoint/key | string | Forwarding only | API key secret | App config/secret | placeholder | Preserve remote `.env`. |

## Local UI Plan

Current pages:

- status
- devices
- device detail

Needed before local completeness:

- explicit per-bank connection/session state
- per-bank last poll age/staleness
- settings snapshot completeness and age
- device info snapshot age
- alarm bit decode table
- BLE adapter contention visibility
- local-only secret redaction for setup passcode if ever displayed from raw frames

## Outbox / Cloud Candidate Streams

| Stream | Payload type | Cadence | Idempotency key | Cloud treatment | Notes |
|--------|--------------|---------|-----------------|-----------------|-------|
| bms.reading | per-bank reading | poll interval | device address + recordedAt | Central BMS readings | Existing. |
| bms.config | per-bank settings snapshot | on change/connect | device + hash/recordedAt | Central config snapshot | Existing subset. |
| bms.device-info | per-bank device metadata | on change/connect | device + hash/recordedAt | Central device info | Existing subset, excludes passcodes. |
| gateway.status | gateway runtime/health | low frequency | gateway + recordedAt | Central gateway cards | Needed. |

## Known Issues And Quirks

| Issue | Evidence | Impact | Workaround | Validation needed |
|-------|----------|--------|------------|-------------------|
| FFE2 documented write characteristic produced no response for targeted devices | `JkBmsProtocol.cs` comments | Must write commands to FFE1 for current devices | Current transport writes to working characteristic | Validate against exact models/firmware. |
| Settings frame explicit query not proven | `JKBMS_SESSION_LIFECYCLE.md` | Cannot rely on on-demand config refresh | Capture spontaneous settings frame on connect/poll | Research protocol and capture. |
| Shared BLE adapter contention | SmartShunt docs and JK session notes | JK/SmartShunt can interfere on `hci0` | Sequential sessions; avoid mixed unstable workloads | Hardware adapter validation. |
| Some emitted fields were not persisted centrally | Recent field audit | Lost diagnostic data | Decide schema/UI before mapping | Average/max/min cell index decision. |
| Central BMS history retention and rollups are not operating | SQL inspection on 2026-08-04 found `v9.BmsReading` ending 2026-07-03, while the Pi outbox continued through 2026-08-04; `v9.BmsReadingMinute` and `v9.BmsReadingHourly` both contained zero rows | Central history has a multi-week gap and no long-range trend source | Restore ingest continuity, then add a scheduled retention/rollup worker | Preserve raw readings for at least 30-60 days, hourly rollups for 6-12 months, and daily rollups beyond that; add monitoring for ingest gaps and rollup freshness. |

## Security And Safety Notes

- Do not implement settings writes until protocol, safety constraints, and rollback/readback are validated.
- Do not send decoded setup passcodes to cloud or logs.
- Treat any command/control operation as local-only until explicit safety/auth/audit design exists.

## Open Questions

| Question | Why it matters | Status |
|----------|----------------|--------|
| Is there a safe explicit settings-query command? | Needed for reliable local config UI | Open |
| Which BMS reading fields should become central storage? | Avoid losing diagnostic value | Open |
| Can JK and SmartShunt share `hci0` reliably with revised sessions? | Deployment stability | Open |
