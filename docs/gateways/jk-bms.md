# JK BMS Gateway Manual

## Status

- Runtime status: headless persistent-device collector implemented by [#328](https://github.com/HualapaiValley/HVO.WebSite/issues/328); [#356](https://github.com/HualapaiValley/HVO.WebSite/issues/356) completed the cutover/MQTT validation recorded on August 12/13. This is historical production evidence, not a new live attestation.
- Documentation reconciled: 2026-10-04. Telemetry and the bounded [password-change command](#bounded-settings-password-command) are implemented; explicit settings-query/general BMS control remain unsupported or unproven.
- Source owner: [collector project](../../src/HVO.Hardware.JkBms/README.md), [protocol](../../src/HVO.Hardware.JkBms/Protocol/JkBmsProtocol.cs), [persistent device sessions](../../src/HVO.Hardware.JkBms/Workers/JkBmsDevice.cs). The complete [undated lifecycle proposal](../archive/jkbms-session-lifecycle.md) is superseded research, not an active refactor instruction.
- Cutover/endurance history: [deployment and endurance](jkbms/deployment-and-endurance.md). New backup/rollback checkpoints use the [canonical SQLite backup contract](sqlite-backup-and-rollback.md), preserving the current volume and post-checkpoint observations.

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
| HVO presentation responsibility | Standard protected diagnostics plus bounded HA MQTT current state; no local UI |
| HVO safety classification | Telemetry plus a secret-gated one-shot settings-password write; no generic control/settings-write surface |

## Communication Summary

| Field | Value |
|-------|-------|
| Transport | BLE GATT |
| Protocol | JK UART-over-BLE style fixed command/response frames |
| Service UUID | `0000ffe0-0000-1000-8000-00805f9b34fb` |
| Notify/RX characteristic | `0000ffe1-0000-1000-8000-00805f9b34fb` |
| Documented TX characteristic | `0000ffe2-0000-1000-8000-00805f9b34fb` |
| Implementation write characteristic | FFE1 for targeted old-module devices; FFE2 produced no response in current implementation notes |
| Authentication/credentials | BLE connection for telemetry; separately configured startup secret gates the optional settings-password command |
| Polling model | Connect sessions and poll cell-info per configured interval |
| Reconnect model | Coordinate connection attempts per adapter, keep healthy persistent sessions polling, and reconnect failed sessions independently |

## References

| Type | Reference | Status | Notes |
|------|-----------|--------|-------|
| Existing HVO code | [Protocol](../../src/HVO.Hardware.JkBms/Protocol), [workers](../../src/HVO.Hardware.JkBms/Workers) | Current | Implementation authority; source reading does not run installed-device commands. |
| HVO design evidence | [Archived lifecycle proposal](../archive/jkbms-session-lifecycle.md) | Superseded | Per-adapter fairness/hot-warm-cool rationale and settings-query uncertainty retained. |
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
| Change settings password | Host to BMS | 20-byte `0xA0` command, six secret-derived ASCII digits | Positive acknowledgement then `0x97` DeviceInfo readback | Configured device secret plus registered MQTT topic | Changes device credential | Bounded one-shot; no automatic command retry | Requires `SetupPasscode` match for `succeeded_verified`; not an explicit settings query. |

## Persistent sessions and metadata

`JkBmsDevice` owns each transport/poll/reconnect state. The adapter coordinator serializes connection setup per adapter with bounded attempts while healthy device sessions continue polling independently. Failed sessions reset/back off; the fleet does not require every bank to share one state or a permanent scan-loop control plane.

The preserved hot/warm/cool design distinction remains useful: cell-info telemetry and alarms are the hot lane; initialization/reconnect explicitly requests DeviceInfo and opportunistically captures spontaneous settings frames; arbitrary stale/operator-refresh scheduling is a proposal, not a promised current control endpoint. Missing settings do not prove a successful refresh. Changed config/device-info snapshots are embedded in the durable reading lane rather than separate competing writer streams. Explicit device-info query, spontaneous `0x01` settings capture and unproven explicit settings query are distinct capabilities.

## Fields: Cell Info Frame `0x02`

The field tables retain protocol/outbox/schema facts and former UI coverage for research. A `Former UI` Yes/Candidate cell describes retired presentation only; no current local UI exists. Actual bounded MQTT fields are in the [projection](../../src/HVO.Hardware.JkBms/HomeAssistant/JkBmsHomeAssistantProjection.cs). Storage ownership is [EF models](../../src/HVO.DataModels/README.md); historical alias/schema notes do not authorize a new mapping.

| Name | Type | Unit | Source offset | Access | Semantics | Possible values/range | Former UI | Outbox | Cloud storage | Notes |
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

| Name | Type | Unit | Access | Semantics | Former UI | Outbox | Cloud storage | Notes |
|------|------|------|--------|-----------|----------|--------|---------------|-------|
| CellOvervoltageProtectionMv | uint | mV | Read-only field | Configuration | Candidate | On change | Yes config snapshot | No generic protection-setting write; bounded password command is separate. |
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

| Name | Type | Access | Semantics | Former UI | Outbox | Cloud storage | Notes |
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
| SetupPasscode | string | Read-only parser field | Secret | Do not show | Do not send | Never central | Used internally for bounded password verification; excluded from public telemetry/outbox payload. |

## Local Configuration

Production loads non-secret mounted `/app/config/gateway.json`, then reapplies environment overrides; reload is disabled. Startup-only secret filenames resolve under `/run/secrets`. [Mounted example](../../deploy/pi-gateways/jkbms/gateway.json.example) and [shared runtime configuration](../AGENT_PROJECT_GUIDANCE.md#headless-runtime-configuration-and-health) own the full contract. Configuration/secret changes require an approved restart/replacement.

| Setting | Type | Required | Secret | Runtime editable | Default | Notes |
|---------|------|----------|--------|------------------|---------|-------|
| `JkBms.ConnectTimeoutSeconds` | int | Yes | No | App config | 30 | 5-120. |
| `JkBms.ExchangeTimeoutSeconds` | int | Yes | No | App config | 10 | 1-60. |
| `JkBms.DefaultPollIntervalSeconds` | int | Yes | No | App config | 60 | 10-3600. |
| `JkBms.HciAdapter` | string | Yes | No | App config | hci0 | Shared BLE contention risk. |
| `JkBms.Devices[]` | list | Yes | No | Mounted config | empty | Address, stable DeviceId, alias, adapter/poll overrides. |
| `JkBms.Devices[].SettingsPasswordSecret` | file name | Optional | Yes | Startup only | absent | Gates password button/status; resolved value must contain exactly six ASCII digits. |
| `JkBms.CentralIngestEndpoint` | URI | Yes | No | Mounted config | none | Absolute approved HTTP(S) base URI, no credentials/query/fragment; HTTPS unless explicit internal/testing opt-in. |
| `JkBms.AllowInsecureCentralIngest` | bool | No | No | Mounted config | false | Explicit HTTP opt-in; does not bypass TLS or grant deployment authority. |
| `JkBms.CentralApiKeySecret` | file name | Yes | Yes | Mounted config/secret file | `central-ingest-api-key` | Resolved under `/run/secrets`. |
| `JkBms.RetryExhaustedRequeueMinutes` | int | Yes | No | Mounted config | 15 | Requeues transient retry-exhausted rows. |
| `Outbox.*` | object | Yes | No | Mounted config | shared defaults | Must use `/app/data/outbox.db` and `com.hvo.bms.reading.v1`. |

## Local Diagnostics And Presentation

The vNext collector intentionally has no Razor, Blazor, or static UI. Operators use:

- `/health/live` for process liveness; `/health` and `/health/ready` for the actual health snapshot (Critical → 503; degraded/noncritical can be 200);
- key-protected GET `/diagnostics/health`, `/diagnostics/status`, `/diagnostics/outbox`, plus PUT `/diagnostics/outbox/settings` for runtime batch/sweep overrides that reset on restart; missing/wrong credentials return 403;
- Home Assistant MQTT Discovery for bounded bank telemetry/availability: pack voltage/current/power, charge/health, capacity/cycles, aggregate cell health, temperatures, balancing, charge/discharge state and alarms. Configured password devices additionally expose the bounded button/status below. Devices are inside `/diagnostics/status`; no separate `/diagnostics/devices` exists.

Home Assistant device identifiers remain the stable configured `DeviceId` values. Display names are updated after the first poll to include the source-reported JK model, nominal capacity, and stable bank number; device metadata also includes the reported firmware and hardware revision. Decimal display precision follows protocol resolution without rounding source state: 3 decimals for V/A/Ah, 2 for W, 1 for temperature, and 0 for integer protocol fields such as SOC, SOH, counts, indexes, and alarm masks.

The MQTT view intentionally omits one entity per physical cell. With seven 24-cell banks that would create at least 168 voltage entities before resistances and other diagnostics. Home Assistant instead receives average/minimum/maximum/delta voltage plus the minimum/maximum cell indexes; complete per-cell readings remain in the durable HVO outbox and canonical database.

JK current and derived power preserve the device protocol sign: positive is charging and negative is discharging. This is intentionally different from the EG4 inverter-branch convention. Dashboards must label the source and measurement point and must not sum individual JK banks, SmartShunt whole-bus values, and EG4 branch values as if they were independent loads.

Diagnostics expose stable device IDs and categorized health without Bluetooth addresses,
secret values, or mounted filesystem paths.

## Bounded settings-password command

[PR #383](https://github.com/HualapaiValley/HVO.WebSite/pull/383) implemented `change_settings_password` only for an enabled device with `SettingsPasswordSecret`. [Startup initialization](../../src/HVO.Hardware.JkBms/Hosting/JkBmsServiceCollectionExtensions.cs) resolves its file and rejects anything other than six ASCII digits; the [worker](../../src/HVO.Hardware.JkBms/Workers/BmsPollerWorker.cs) registers only that device's exact command topic. The [shared router](../../src/HVO.Edge.HomeAssistant.Mqtt/HomeAssistantMqttCommandRouter.cs) rejects retained messages, requires `PRESS` after trimming and dispatches only registered topics. MQTT carries the button press, never a password.

The [device session](../../src/HVO.Hardware.JkBms/Workers/JkBmsDevice.cs) rejects offline/busy/already-verified requests, queues one bounded command in its serialized session and performs no automatic command retry after failure. [JkBmsClient](../../src/HVO.Hardware.JkBms/Protocol/JkBmsClient.cs) requires a positive protocol ACK; the session then polls DeviceInfo and reports `succeeded_verified` only when `SetupPasscode` equals the configured secret. ACK without matching readback is `failed_unverified`; failures remain visible. Initialization recognizes an already matching password without writing it again.

This is an existing credential-write capability, not arbitrary BMS control or proof of explicit settings-query support. Source/simulated tests and attributed historical PR evidence are distinct from permission to press the button or change a physical credential. Ordinary local tests perform neither. Keep decoded/configured passwords out of logs, diagnostics, public state and central payloads; broker/device access and secret rotation follow separately authorized operations.

## Outbox / Cloud Candidate Streams

| Stream | Payload type | Cadence | Idempotency key | Cloud treatment | Notes |
|--------|--------------|---------|-----------------|-----------------|-------|
| com.hvo.bms.reading.v1 | Per-bank reading with changed config/device-info embedded | poll interval | device address + recordedAt | Central BMS readings/config/info/alarms | Single shared-outbox payload lane; excludes passcodes. |

## Known Issues And Quirks

| Issue | Evidence | Impact | Workaround | Validation needed |
|-------|----------|--------|------------|-------------------|
| FFE2 documented write characteristic produced no response for targeted devices | `JkBmsProtocol.cs` comments | Must write commands to FFE1 for current devices | Current transport writes to working characteristic | Validate against exact models/firmware. |
| Settings frame explicit query not proven | [Preserved lifecycle research](../archive/jkbms-session-lifecycle.md) and current session flow | Cannot rely on on-demand config refresh | Capture spontaneous settings frame on connect/poll; password readback is a different DeviceInfo query | Research exact installed firmware/protocol under separate scope. |
| Shared BLE adapter contention | SmartShunt docs and JK session notes | JK/SmartShunt can interfere on `hci0` | Sequential sessions; avoid mixed unstable workloads | Hardware adapter validation. |
| Some emitted fields were not persisted centrally | Recent field audit | Lost diagnostic data | Decide schema/UI before mapping | Average/max/min cell index decision. |
| Historical BMS gap / unproven rollup operation | The 2026-08-04 SQL inspection found `v9.BmsReading` ending July 3 and both minute/hourly rollups empty, while the Pi outbox continued through August 4 | Historical gap evidence remains; source docs cannot attest current completeness or a live retention worker | Preserve original rows/outbox evidence and verify continuity/retention separately | The old 30–60-day raw/6–12-month hourly/daily policy is proposed, not an implemented retention promise. |

## Security And Safety Notes

- Preserve the narrow password command's secret gating, one-shot serialization, positive ACK and readback; do not broaden it into generic settings/control without a separate reviewed safety contract.
- Do not send decoded setup passcodes to cloud or logs.
- No central/cloud generic command/control path exists. The implemented broker-local button is the bounded exception, not a telemetry-only interface.

## Open Questions

| Question | Why it matters | Status |
|----------|----------------|--------|
| Is there a safe explicit settings-query command? | Needed for reliable configuration snapshots | Open |
| Which BMS reading fields should become central storage? | Avoid losing diagnostic value | Open |
| Can JK and SmartShunt share `hci0` reliably with revised sessions? | Deployment stability | Open |
