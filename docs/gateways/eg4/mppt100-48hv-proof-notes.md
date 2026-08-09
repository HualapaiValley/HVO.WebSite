# MPPT100-48HV Proof Notes

## Evidence Classes

- The 2026-08-04 observations were supplied by the system owner from a prior controlled session. They are operator-reported because raw session logs are not present in this repository.
- The 2026-08-09 transactions were executed in this issue session and are preserved byte-for-byte in `standby-2026-08-09.json`.

## Pre-Proof Inventory

- Host: sanitized DevPi5.
- Stable adapter: sanitized `/dev/serial/by-id` CH341 identity; one serial adapter was present.
- Endpoint: owner confirmed the cable was connected only to the MPPT100-48HV BMS interface.
- Ownership: `fuser` reported no process holding the TTY.
- Existing containers: JK BMS, SmartShunt, SolarAssistant, and Davis were running and healthy.
- Initial TTY state: 9600 baud, 8 data bits, no parity, one stop bit, canonical mode.

## Complete 2026-08-09 Transmit Sequence

No frame other than these three was transmitted:

| UTC | Request | Result |
|---|---|---|
| `13:18:53.085145` | `01 03 00 BA 00 0C 64 2A` | 29-byte normal response, CRC `6C F4` |
| `13:20:17.657434` | `01 03 00 BA 00 0C 64 2A` | Same 29-byte response |
| `13:20:27.725658` | `01 03 00 D7 00 14 F5 FD` | 45-byte normal response, CRC `4B 5A` |

Each read used 9600 8N1, unit 1, function `0x03`, exclusive pyserial ownership, a two-second timeout, and no retry. No write-capable function, broadcast, address scan, configuration command, or firmware command was sent.

## Operator-Reported 2026-08-04 Session

- Protocol: Modbus RTU, 9600 8N1, function `0x03`.
- Candidate battery addresses: `0x01-0x10`.
- Candidate battery blocks: main data `0x0000-0x0026`, model `0x0069` for 11 registers, firmware `0x0075` for 3, serial `0x0078` for 8.
- Candidate request `01 03 00 13 00 11 74 03` asks for battery data `0x0013-0x0023`.
- Unit 1 responded with zeros; units 2 and `0x10` did not respond within the bounded attempts.
- Passive listening observed no controller-originated polling during that session.

These observations are consistent with BMS communication being disabled/unconfigured, no compatible battery being present, or cable/pinout mismatch. They do not validate controller telemetry.

## Post-Proof Verification

- The same four gateway containers remained healthy.
- No process held the serial adapter.
- Pyserial left the unowned TTY in raw mode; the session restored the recorded canonical 9600 8N1 state, including the original break and max-bell flags.
- No repository deployment or gateway configuration changed.

## Stop Decision

Active probing ended after the approved sequence. If the controller begins BMS polling, another master must not share this bus. Further BMS-bus research is passive-only; MPPT telemetry requires a separate vendor-supported monitoring/service interface.
