# EG4 MPPT100-48HV Protocol Evidence

Status: production read-only telemetry profile validated for one point-to-point controller cable.

Research dates: 2026-08-09 through 2026-08-10. Target: EG4 MPPT100-48HV. The installed controller did not expose a readable firmware identity, so runtime validation uses a fixed request, strict framing, field ranges, and register relationships.

## Safety Boundary

- The owner confirmed that the CH341 adapter is a point-to-point MPPT cable and no battery BMS shares it.
- Production sends only Modbus function `0x03` to unit `1`, start register `200`, quantity `18`.
- No arbitrary register, unit, or function API exists.
- Functions `0x05`, `0x06`, `0x0F`, `0x10`, `0x15`, `0x16`, `0x17`, broadcasts, diagnostics, vendor commands, firmware operations, and address/register scans are prohibited.
- The serial device is mapped only when `EG4_MPPT_0_ENABLED=true` selects `docker-compose.mppt.yml`.
- Controller silence at night is unavailable/asleep, never a numeric zero sample.

## Evidence

| ID | Source | Claims | Limits |
|---|---|---|---|
| M1 | [Official manual](https://eg4electronics.com/wp-content/uploads/2024/04/EG4-MPPT100-48HV-Manual.pdf) | Exact model, RS485 connector, configurable Modbus ID with default `001`. | Does not publish the controller telemetry register map. |
| M2 | Official firmware package associated with the controller | TMS320C28x firmware contains SCI-A at 9600 8N1, Modbus CRC tables, function-`0x03` dispatch, and the holding-register descriptor table. | No readable firmware identity register was found. Firmware is not committed or redistributed. |
| H1 | HVO static analysis, issue #283 | Dispatcher validates request length, quantity, CRC, and function. Descriptor table at C28 word address `0x3EF15C` marks registers 200-217 active and 218-299 reserved holes. | Function `0x10` and vendor handlers also exist and remain prohibited. |
| H2 | HVO nighttime reads, 2026-08-09 | Exact read requests returned no response bytes while the controller was asleep. | Establishes silence behavior, not numeric sentinels. |
| H3 | HVO daylight reads, 2026-08-10 | Two complete 41-byte CRC-valid responses provided stable scaling and internal field relationships. | One operating installation; temperature sensor locations remain unidentified. |

Official package hashes and reverse-engineering details are preserved in issue #283. Firmware binaries remain outside the repository and were never executed or flashed.

## Fixed Wire Profile

| Property | Value |
|---|---|
| Serial | 9600 baud, 8 data bits, no parity, 1 stop bit |
| Framing | Modbus RTU |
| Unit | `1` only |
| Request | `01 03 00 C8 00 12 44 39` |
| Response | unit + `03` + byte count `36` + 18 big-endian words + CRC |
| Total response length | 41 bytes |
| CRC | Modbus CRC16, polynomial `0xA001`, initial `0xFFFF`, low byte first |
| Production cadence | 60 seconds by default |

The decoder rejects wrong unit/function/length/byte count/CRC, out-of-range values, and inconsistent PV voltage/current/power. A timeout retries once after a bounded delay and then reports unavailable without publishing telemetry.

## Register Map

| Register | Production meaning | Scale | Daylight example | Provenance |
|---:|---|---:|---:|---|
| 200 | Raw controller state | raw | 2 | Diagnostic only |
| 201 | Raw controller state/fault channel | raw | 2 | Diagnostic only |
| 202 | Battery/output voltage | 0.1 V | 54.3 V | Direct |
| 203 | Controller output current magnitude | 0.1 A | 9.1 A | Direct; canonical battery sign is negative while charging |
| 204 | Raw charge state | raw | 0 | Diagnostic only |
| 205 | Controller-estimated battery level | 1% | 90% | Diagnostic only; never canonical SOC |
| 206 | Raw controller diagnostic | raw | 4038 | Diagnostic only |
| 207 | PV input voltage | 0.1 V | 400.6-400.7 V | Direct |
| 208 | PV input current | 0.1 A | 1.2 A | Direct |
| 209 | PV input power | 1 W | 480 W | Direct |
| 210 | Temperature channel 1 | 1 C | 40 C | Direct, neutral name |
| 211 | Temperature channel 2 | 1 C | 37 C | Direct, neutral name |
| 212 | Raw controller diagnostic | raw | 40 | Diagnostic only |
| 213-214 | Write-capable settings in firmware | raw | 0 | Never publish or write |
| 215-217 | Internal fingerprint/status values | raw | 13, 91, 2 | Diagnostic only |

Registers 218-299 are explicit reserved holes in the firmware descriptor table and are not polled.

Battery output power is derived as `battery voltage * canonical battery current`; it retains derived provenance. Register 205 is voltage-derived, clamped by controller logic, and capped while charging. JK BMS and SmartShunt remain authoritative SOC sources.

User interfaces invert the canonical current and power only for display, so controller charging appears positive from the battery perspective. Persisted telemetry remains negative while charging and is not rewritten.

## Internal Validation

The daylight captures establish:

- register 209 tracks `register 207 * register 208 / 100` within integer update/rounding tolerance;
- register 203 and internal registers 215/216 move together according to firmware conversion logic;
- the decoded PV values correlate with SmartShunt and JK BMS charging observations while representing different physical measurement points.

No numeric sentinel is documented. An implausible register layout is rejected as a protocol error rather than converted to null or zero.

## Historical Payload

Each successful sample emits:

- a canonical battery charge-controller branch observation;
- one `PowerMpptDetailPayload` tracker with direct PV voltage/current/power;
- battery output detail with derived power;
- neutral temperature channels;
- raw diagnostics including `controllerEstimatedSocPercent`.

Distinct timestamps are retained in `v9.PowerMpptDetailSnapshot`. Repeated equal values are not suppressed, which preserves chartable history.
