# Victron SmartShunt Gateway Manual

## Status

- Phase 0 status: seeded from current SmartShunt plan and code.
- Last updated: 2026-05-28
- Confidence: medium for public read-only telemetry; low for private/write behavior.
- Primary references: `src/HVO.Hardware.VictronSmartShunt`, `docs/SMARTSHUNT_PLAN.md`.

## Identity

This table is HVO documentation metadata unless a row explicitly references a Victron/device value. It is not a list of SmartShunt API fields.

| Field | Value |
|-------|-------|
| HVO manual subject | Victron SmartShunt |
| HVO integration role | Source |
| Known device | `SmartShunt LiFePo4` |
| HVO project/service | `src/HVO.Hardware.VictronSmartShunt` |
| HVO deployment target | Pi gateway container, compose file `deploy/pi-gateways/smartshunt/docker-compose.yml` |
| Native UI exists | VictronConnect |
| Native UI is primary | Yes for settings/sync/write paths |
| HVO UI responsibility | Level 2 operational dashboard for read-only telemetry |
| HVO safety classification | Read-only telemetry currently; writes/sync deferred |

## Communication Summary

| Field | Value |
|-------|-------|
| Transport | BLE GATT |
| Public service/characteristic family | UUIDs beginning `6597...` |
| Private enrichment family | UUIDs beginning `306b...` in current plan |
| Authentication | BLE connection/pairing behavior; private behavior not fully documented |
| Polling/subscription model | Public session reads/keepalive; optional private refresh/enrichment |
| Current deployment note | SmartShunt and JK BMS sharing `hci0` is not considered stable |

## References

| Type | Reference | Status | Notes |
|------|-----------|--------|-------|
| Existing HVO plan | `docs/SMARTSHUNT_PLAN.md` | Found | Current field trust and limitations. |
| Existing HVO code | `SmartShuntPublicProtocol.cs`, worker/session/private source | Found | Current implementation. |
| Victron public docs | Victron BLE/VE.Direct docs | Needed | VE.Direct docs are supporting only, not proof of BLE writes. |
| VictronConnect capture | Real capture from installed device | Needed | Required before write/sync work. |

## Capabilities Summary

| Capability group | Read-only | Read-write | Command/action | Local UI | Outbox/cloud | Notes |
|------------------|-----------|------------|----------------|----------|--------------|-------|
| Public live telemetry | Yes | No | No | Yes | Partial | Production baseline. |
| Private product metadata | Yes, optional | No | No | Candidate | Local/config candidate | Useful enrichment. |
| Private history/statistics | Yes, optional | No | No | Candidate | Local/detail candidate | Not primary telemetry. |
| Alarm thresholds/private settings | Partial | Not approved | Not approved | Local candidate | No | Needs capture. |
| Sync/write paths | No | Deferred | Deferred | Native UI only | No | Do not implement without capture. |

## Public Fields

| Name | Type | Unit | UUID | Access | Semantics | Null/not available | Local UI | Outbox | Cloud storage | Notes |
|------|------|------|------|--------|-----------|--------------------|----------|--------|---------------|-------|
| StateOfChargePercent | double? | % | `65970fff-...` | Read-only | Instantaneous | `ffff` | Yes | Yes | Yes | Sometimes invalid at 0; private overlay may help. |
| VoltageV | double? | V | `6597ed8d-...` | Read-only | Instantaneous | `ff7f` | Yes | Yes | Yes | Stable. |
| CurrentA | double? | A | `6597ed8c-...` | Read-only | Instantaneous | `ffffff7f` | Yes | Yes | Yes | Signed thousandths; positive is charge into the battery and negative is discharge. |
| PowerW | double? | W | `6597ed8e-...` | Read-only | Instantaneous | `ff7f` | Yes | Yes | Yes | Signed watts; positive is charge into the battery and negative is discharge. |
| ConsumedAh | double? | Ah | `6597eeff-...` | Read-only | Cumulative/session | `ffffff7f` | Yes | Local-only currently | Not central | Stable locally. |
| StarterVoltageV | double? | V | `6597ed7d-...` | Read-only | Instantaneous | `ff7f` | Yes if present | Local-only currently | Not central | Often unavailable on this device. |
| TemperatureC | double? | deg C | `65970383-...` | Read-only | Instantaneous | `ff7f` | Yes if present | Local-only currently | Not central | Often unavailable. |
| RemainingMinutes | double? | minutes | `65970ffe-...` | Read-only | Estimate | `ffff` | Yes if present | Local-only currently | Not central | Often unavailable. |

## Sign-Correction Rollout

The source-native SmartShunt convention is positive charge into the battery and negative discharge. The shared composed power-system contract intentionally converts this to positive discharge and negative charge.

When deploying the corrected decoder from issue #302:

1. Stop the SmartShunt gateway before changing retained data or deploying the new image.
2. Archive the complete Pi `smartshunt_smartshunt-outbox` database with `scripts/outbox-maintenance.sh`; do not replay old opposite-sign payloads.
3. Back up the self-hosted SQL Server database and remove existing `v9.PowerReading` rows where `SourceSystem = 'victron-smartshunt'`.
4. Deploy the website first so its host-local SQL connection remains authoritative over the legacy Key Vault setting.
5. Deploy and start the corrected SmartShunt gateway, then verify source `+` charging becomes composed `-` charging exactly once.
6. Leave Azure SQL history untouched; it is not part of the local production data path.

## Private Enrichment Fields

| Name | Type | Unit | Access | Semantics | Local UI | Outbox/cloud | Notes |
|------|------|------|--------|-----------|----------|--------------|-------|
| Firmware version | string | n/a | Read-only | Metadata | Candidate | Device inventory candidate | Product field `0x02` in current plan. |
| Serial number | string | n/a | Read-only | Metadata | Candidate/redacted | Treat carefully | Product field `0x0a`. |
| Deepest/last/average discharge | double? | Ah | Read-only | History | Candidate | Optional detail | History fields `0x00`-`0x02`. |
| Total charge cycles | uint? | count | Read-only | Lifetime counter | Candidate | Optional detail | History `0x03`. |
| Full discharges | uint? | count | Read-only | Lifetime counter | Candidate | Optional detail | History `0x04`. |
| Cumulative Ah drawn | double? | Ah | Read-only | Lifetime counter | Candidate | Optional detail | History `0x05`. |
| Min/max battery voltage | double? | V | Read-only | History | Candidate | Optional detail | History `0x06`/`0x07`. |
| Charged/discharged energy | double? | kWh | Read-only | Lifetime counter | Candidate | Optional detail | History `0x10`/`0x11`. |
| Alarm threshold fields | double? | V/% | Read-only? | Configuration | Candidate | Local-only until validated | Private overlay includes threshold fields. |
| CurrentCoarseA | double? | A | Read-only | Diagnostic | Candidate | Local-only | Approx fallback, not forwarded. |

## Local Configuration

| Setting | Type | Required | Secret | Runtime editable | Default | Notes |
|---------|------|----------|--------|------------------|---------|-------|
| `SmartShunt.Address` | string | Yes for live | No | App config | empty disables | BLE address. |
| `SmartShunt.Adapter` | string | Yes | No | App config | hci0 | BLE adapter. |
| `SourceId` | string | Yes | No | App config | smartshunt-main | Cloud source id. |
| `DeviceId` | string | Yes | No | App config | smartshunt-lifepo4 | Cloud device id. |
| `PublicOnly` | bool | No | No | App config | true | Public baseline. |
| `EnablePrivateEnrichment` | bool | No | No | App config | false | Optional enrichment. |
| `SampleIntervalSeconds` | int | Yes | No | App config | 5 | Live sample cadence. |
| `SnapshotIntervalSeconds` | int | Yes | No | App config | 15 | Outbox snapshot cadence. |

## Local API Plan

Existing endpoints:

| Endpoint | Purpose | Auth | Response | Notes |
|----------|---------|------|----------|-------|
| `/status` | Current snapshot, private info, history, outbox, health | none/internal today | anonymous object | Existing. |
| `/gateway-health` | Gateway health snapshot | none/internal today | health snapshot | Existing. |

## Local UI Plan

Current pages:

- status
- telemetry

Needed before local completeness:

- explicit public/private data path display
- stale sample state
- BLE adapter/shared workload warning
- private enrichment age and confidence
- clear distinction between trusted telemetry and diagnostic/private fields

## Outbox / Cloud Candidate Streams

| Stream | Payload type | Cadence | Idempotency key | Cloud treatment | Notes |
|--------|--------------|---------|-----------------|-----------------|-------|
| power.reading | battery monitor summary | snapshot interval | source + device + recordedAt | Existing central power reading | Public-first only. |
| smartshunt.detail | public+private detail | low frequency/manual | device + recordedAt | Optional future detail | Needs trust decision. |
| smartshunt.device-info | product metadata | on change | device + hash | Optional inventory | Serial privacy decision. |
| gateway.status | runtime/health | low frequency | gateway + recordedAt | Central gateway cards | Needed. |

## Known Issues And Quirks

| Issue | Evidence | Impact | Workaround | Validation needed |
|-------|----------|--------|------------|-------------------|
| Public SoC can be invalid | Current plan notes public `soc == 0` suspicious | Wrong SoC if trusted blindly | Private overlay when fresh or public looks invalid | VictronConnect comparison. |
| Write/sync not proven | `SMARTSHUNT_PLAN.md` | Unsafe to alter battery monitor settings | Keep read-only | Real VictronConnect capture. |
| BLE contention with JK BMS | Current plan | Gateway instability | Avoid mixed stable workload on same `hci0` | Adapter strategy. |

## Security And Safety Notes

- Keep SmartShunt write/sync disabled until a real capture proves framing and safety.
- Treat serial/product fields as potentially sensitive.
- Do not let coarse/private fallback fields silently replace trusted public telemetry in forwarded payloads.

## Open Questions

| Question | Why it matters | Status |
|----------|----------------|--------|
| What official Victron BLE docs apply to this exact SmartShunt firmware? | Contract validation | Open |
| Can private enrichment be made deterministic and safe? | Local UI accuracy | Open |
| What second Bluetooth adapter, if any, works reliably on Pi? | JK/SmartShunt coexistence | Open |
