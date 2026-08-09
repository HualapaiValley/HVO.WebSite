# EG4 MPPT100-48HV Protocol Evidence

Status: battery-facing bus characterized; controller telemetry adapter blocked.

Research and hardware proof date: 2026-08-09. Target: EG4 MPPT100-48HV. Firmware was not available from the controller interface and remains unknown.

## Scope And Safety

This controller exposes an RS485 port documented for battery-BMS communication. The controller normally acts as the Modbus master, reads battery/BMS data, and adjusts charging. It is not a supported controller-monitoring port. Running another master after controller polling becomes active risks collisions.

HVO used a dedicated cable during controlled proof sessions on 2026-08-04 and 2026-08-09. The August 4 results are operator-reported; the August 9 frames are captured evidence. See [proof notes](mppt100-48hv-proof-notes.md). No write function, configuration command, firmware operation, service interruption, or deployment occurred. Active probing is now stopped; future work on this bus is passive-only unless a vendor-supported host interface is identified.

The captures characterize the battery-facing protocol and negative results. They do not show that the MPPT exposes its own voltage, current, power, SOC, state, temperature, or alarms. The related EG4 3000EHV controller map is rejected for this port.

## Evidence Matrix

| ID | Source | Exact claims | Limits |
|---|---|---|---|
| M1 | [EG4 MPPT100-48HV product page](https://eg4electronics.com/categories/charge-controllers-mppt/mppt100-48hv-eg4-solar-charge-controller/) and [manual v1.0.2](https://eg4electronics.com/wp-content/uploads/2024/04/EG4-MPPT100-48HV-Manual.pdf) | Exact model; BMS communication, configurable Modbus ID `001-247` default `001`, RS485 BMS connector. | No host protocol, serial format, functions, registers, scaling, CRC, cadence, or multidrop guidance. |
| M2 | Official firmware package `3000_C6385A9V1` linked from M1 | Exact product page associates this package with the controller. | Name suggests 3000-family heritage but does not prove a register map. |
| M3 | [PythonProtocolGateway issue #72](https://github.com/HotNoob/PythonProtocolGateway/issues/72) and its `eg4_3000ehv_v1` map | Maintainer reports EG4 support supplied the 3000EHV protocol in response to an MPPT100-48HV request; the related map uses Modbus RTU, 9600 baud, and holding registers. | Project compatibility matrix says MPPT100-48HV is unconfirmed; requester had not tested hardware; it does not establish this model's serial framing, unit, or field meanings. |
| M4 | HVO DevPi5 proof, 2026-08-04 | Passive listening found no controller polling. External reads found unit 1 returning zeros for the common battery block; addresses 2 and `0x10` did not respond. | Consistent with disabled/unconfigured BMS communication, no compatible battery, or cable/pinout mismatch. It does not identify a controller telemetry slave. |
| M5 | HVO DevPi5 capture in `standby-2026-08-09.json` | The owner confirmed the dedicated cable endpoint. An unidentified unit-1 slave answered `0x03`; unvalidated ranges 186-197 and 215-234 returned zeros except raw register 217=`1`; the first response repeated after approximately 84 seconds. | Response origin is not proven to be the controller. Values do not validate 3000EHV meanings, identity, firmware, or telemetry. |

## Battery-Facing Protocol Profile

| Property | Validated result |
|---|---|
| Intended bus role | Vendor topology and operator protocol evidence indicate MPPT master and battery/BMS slave; no controller-originated poll was observed in the available passive session |
| Connection used | MPPT100-48HV BMS cable through an owner-confirmed CH341 adapter during controlled proof |
| Port ownership | No process held the adapter before proof |
| Serial | 9600 baud, 8 data bits, no parity, 1 stop bit |
| Framing | Modbus RTU |
| Battery addresses | Commonly `0x01-0x10`; controlled proof saw unit 1 only, while 2 and `0x10` did not respond |
| Function | `0x03` Read Holding Registers observed/used for battery reads |
| CRC | Standard Modbus CRC16 (`0xA001`, initial `0xFFFF`), low byte then high byte |
| Operator-reported candidate battery poll | `01 03 00 13 00 11 74 03`, requesting battery registers `0x0013-0x0023`; not passively observed from this controller |
| Additional negative-proof requests | `01 03 00 BA 00 0C 64 2A`; `01 03 00 D7 00 14 F5 FD` |
| Repeatability | Range 186-197 returned the same response twice, approximately 84 seconds apart |

The candidate battery block includes SOC, voltage, current, temperature, capacity, and charge/discharge limits. Those are battery inputs consumed by the MPPT; they are not controller-output telemetry. Official manual v1.0.2 assigns connector pin 4 to ground; verify the manual version and physical cable before future wiring. Isolation, termination, bias, and cable length remain unknown.

## Captured Raw Results

- Candidate identity registers 186-197: twelve zero words in both reads.
- Candidate telemetry registers 215-234 during standby: register 217 contained raw word `1`; all other words were zero.
- These values are raw evidence only. Zero may be a legitimate standby value, unavailable data, or an incompatible map. The ranges accepted reads but have no validated MPPT100-48HV meaning.

## Active-Probe Disposition And Prohibitions

No active MPPT proof codec or tool is committed. The controlled reads answered the research question and should not become a production poller. Once MPPT-to-BMS polling is active, use passive high-impedance monitoring only.

Prohibit Modbus functions `0x05`, `0x06`, `0x0F`, `0x10`, `0x15`, `0x16`, and `0x17`; prohibit broadcasts, diagnostics, vendor-specific functions, firmware commands, and broad register sweeps. Function `0x04` is also unsupported because it was not evidenced.

## Field Map

No telemetry field is validated yet. The 3000EHV candidate meanings for registers 215-234 are not adopted. Specifically unresolved:

- battery/output voltage and scale;
- charging current, direction, signedness, and scale;
- reported or derived power;
- genuine SOC provenance;
- controller state;
- battery/controller temperature;
- alarms and warnings;
- numeric sentinels and stale/night behavior;
- byte/word order beyond the captured 16-bit words;
- register update cadence and safe production poll rate.

## Multiple Controllers

The configurable Modbus ID relates to battery/BMS communication and does not prove that MPPT controllers are slaves on a host multidrop bus. No second controller telemetry address exists in current evidence. Address scanning is prohibited.

## Adapter Blocker

Issue #283 is blocked from using this BMS connector for MPPT branch telemetry. The next step is to identify a separate monitoring/service port or obtain a vendor-supported controller telemetry interface and register specification. Do not implement a production observation from battery registers `0x0000-0x0026`, the 3000EHV map, or the negative-proof ranges. SolarAssistant remains the source of PV telemetry; no PV-generation aggregation is added here.
