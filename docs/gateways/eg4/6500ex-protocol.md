# EG4 6500EX Battery Telemetry Protocol Evidence

Status: RS232/COM inquiry profile and direct USB HID transport validated for limited battery fields; BMS RS485 blocked.

Research date: 2026-08-09. Target: EG4 6500EX-48, observed firmware `79.02` / `61.00` and `79.71` / `61.13`, model `MKS2-6500`, general model `045`.

## Scope Decision

The gateway uses the inverter's RS232/COM monitoring interface. It does not connect to or transmit on the closed-loop BMS RS485 bus. Although a PI30 status response also contains PV and AC values, this epic decodes and publishes battery-side values only.

The BMS RS485 bus participates in battery charge/discharge control. EG4 publishes no safe request, serial format, framing, CRC definition, unit address, field map, or multi-master guidance for that bus. It must not be treated as Modbus or actively probed.

## Evidence Matrix

| ID | Source | Applicability and claims | Limits |
|---|---|---|---|
| E1 | [EG4 6500EX product archive](https://eg4electronics.com/categories/legacy-products/eg4-6500ex-48-all-in-one-off-grid-inverter/) and [manual v2.3.2](https://eg4electronics.com/wp-content/uploads/2024/05/EG4_6.5_Manual.pdf) | Direct model. BMS RJ45 pins: 1 RS232TX, 2 RS232RX, 3 RS485B, 5 RS485A, 6 CANH, 7 CANL, 8 GND. BMS communication controls charging voltage/current, cutoff, and charge/discharge state. | No BMS wire protocol or RS232 monitoring command protocol. |
| E2 | [EG4 6500EX firmware archive](https://eg4electronics.com/documentation/6500ex-48-firmware-legacy/) | Direct model. Firmware notes establish CRC-sensitive BMS traffic and firmware-dependent receive timing/error handling. | Does not define framing, CRC algorithm, fields, or safe external requests. |
| E3 | [HVO owner's public 6500EX capture](https://github.com/jblance/mpp-solar/discussions/284#discussioncomment-4489705), 2022-12-24 | Direct device capture: `QMN=MKS2-6500`, `QGMN=045`, firmware `79.02/61.00`; `QPIGS` returned battery 52.50 V, charge 0 A, SOC 83%, discharge 16 A. | RS232/USB PC-monitoring interface, not BMS RS485. Published payload omitted response CRC bytes. |
| E4 | [PI30MAX protocol, 2021-02-17](https://github.com/jblance/mpp-solar/blob/master/docs/protocols/PI30MAX.Communication.Protocol20210217.pdf) | OEM-family document corroborated by E3. Defines 2400 8N1, ASCII inquiry plus adjusted CRC-CCITT/XMODEM and CR, and QPIGS field positions/scales. | Community-hosted copy; not an EG4 publication. Model applicability comes from E3/E5. |
| E5 | [SolarAssistant 6500EX support](https://solar-assistant.io/help/inverters/eg4/6500EX-48) and [RS232 instructions](https://solar-assistant.io/help/inverters/eg4/6500EX-48/rs232) | Independent direct-model implementation. Uses the RS232/COM port and Voltronic driver; documents RJ45 pins 1 RX, 2 TX, 8 GND from its cable perspective. | Does not publish frames, parser, cadence, or field provenance. |
| E6 | [Independent 6500EX SOC comparison](https://diysolarforum.com/threads/raw-data-vs-watchpower-battery-capacity-question.83721/) | Direct model. `QPIGS` SOC matched WatchPower; `QPGS0/1/2` SOC did not. | Community observation, one installation. Does not prove SOC is always genuine BMS SOC. |
| E7 | [`mpp-solar` HID transport at `eafdd43`](https://github.com/jblance/mpp-solar/blob/eafdd4328c77f516cf82430c5bcb6fa042c45d3a/mppsolar/inout/hidrawio.py) | Maintained PI30 implementation opens `/dev/hidraw*`, writes inquiry frames in 8-byte HID reports, and reads until CR. | Community implementation; corroborated by H1 on the target device. |
| H1 | HVO live capture, 2026-08-09, fixture `charging-2026-08-09-live-hid.json` | Target device enumerated as USB HID `0665:5161`; identity `PI30` / `MKS2-6500` / `045`; firmware `79.71` / `61.13`; byte-complete QPIGS response with valid CRC reported 54.40 V, 68 A charging, 0 A discharging, and 100% reported SOC. | One charging-state capture. Current fields are inverter-reported integer magnitudes. |

## Validated RS232 Profile

| Property | Value | Evidence |
|---|---|---|
| Electrical interface | RS232/COM RJ45, not TTL and not BMS RS485 | E3, E5 |
| Serial | 2400 baud, 8 data bits, no parity, 1 stop bit | E4, corroborated by E3 behavior |
| Request | ASCII inquiry + two adjusted CRC bytes + `CR` | E3, E4 |
| CRC | CRC-CCITT/XMODEM, polynomial `0x1021`, initial `0x0000`, high byte then low byte; bytes `00`, `0A`, `0D`, `28` incremented | E4 and known `QPIGS B7 A9 0D` vector |
| Response | `(` + payload + adjusted CRC high/low + `CR` | E4 |
| Negative response | `(NAK` with CRC and CR | E3, E4 |
| Ownership | One port owner and one outstanding inquiry | Community operational evidence; conservative HVO rule |
| Direct USB transport | Linux hidraw, USB `0665:5161`, 8-byte HID reports; all allowlisted inquiry frames fit one report | E7, H1 |

## Inquiry Allowlist

| Inquiry | Purpose | Frequency |
|---|---|---|
| `QPI`, `QMN`, `QGMN`, `QVFW`, `QVFW3` | Identity and firmware proof | Startup/manual proof only |
| `QPIGS` | General status; HVO consumes battery fields only | One proof request; future production cadence no faster than 5 seconds pending live validation |

No arbitrary command API exists. All `P...` setting commands, `DAT`, resets, firmware operations, command discovery, and undocumented inquiries are prohibited.

## Battery Field Map

QPIGS fields are one-based in this table.

| Field | Meaning | Encoding | HVO interpretation | Confidence |
|---:|---|---|---|---|
| 9 | Battery voltage | Decimal volts | Direct inverter-reported value | High for field/scale |
| 10 | Battery charging current | Unsigned integer amperes | Charging magnitude | High for field/scale |
| 11 | Battery capacity | Integer percent | `ReportedStateOfChargePercent`; not asserted to be genuine BMS SOC on every battery mode | Medium |
| 16 | Battery discharge current | Unsigned integer amperes | Discharging magnitude | High for field/scale |

Canonical HVO current is derived as `discharge - charge`, so positive means discharge and negative means charge. Power is derived as `voltage * canonical current`; it is not a reported PI30 field. Both derived values must retain derived provenance.

## Sentinels And Failures

No model-specific numeric sentinel is validated. Zero is a valid current. `NAK`, timeout, CRC failure, missing CR, wrong field count, or numeric parse failure makes the current observation unavailable; none is converted to zero. Previous good values must remain distinguishable from current communication health.

## Fixture Provenance

`tests/HVO.Hardware.Eg4.Tests/Fixtures/6500ex/discharging-2022-public-capture.json` preserves the public E3 payload and expected battery values. Its frame is explicitly reconstructed because the published capture omitted response CRC bytes. It is not represented as a byte-perfect hardware capture.

`tests/HVO.Hardware.Eg4.Tests/Fixtures/6500ex/charging-2026-08-09-live-hid.json` preserves H1's exact QPIGS request and response, including captured CRC bytes and the expected canonical `-68 A` / `-3699.2 W` charging result. The public discharging fixture proves a zero charging-current field, and H1 proves a zero discharging-current field. Their combination exhausts the idle parser case, so a separately timed zero/zero hardware capture is useful shadow-run evidence but is not a production-parser blocker.

## DevPi5 Inventory

Read-only inventory on 2026-08-09 found both expected USB devices. The MPPT BMS cable is the CH341 serial adapter at USB path `1-2`; it remains excluded from 6500EX traffic. The inverter monitoring cable is a separate Cypress/STMicroelectronics HID device `0665:5161` at USB path `1-1`, exposed as `/dev/hidraw0`. The owner confirmed both endpoints, and bounded identity inquiries proved that the HID device is the 6500EX.

No process owned the HID node during proof. Five allowlisted identity inquiries were sent before one QPIGS request. An immediate second HID session timed out before writing its first identity inquiry; after a conservative cooldown, the complete identity and status sequence succeeded. Production reconnect handling must reopen, repeat identity validation, and retain bounded timeout/backoff behavior.

## Explicit Blockers

- BMS RS485 remains unsupported: protocol identity, serial settings, role, framing, CRC, addressing, telemetry, and safe cadence are unknown.
- QPIGS SOC is reported by the inverter; genuine closed-loop BMS provenance is not guaranteed by current evidence.
- Battery alarms and battery/cell temperatures have no validated RS232 field map.
- Numeric sentinels remain unknown; zero is a valid current magnitude.
- Firmware layouts other than the exact captured `79.02/61.00` and `79.71/61.13` tuples are unsupported.
- A simultaneous zero/zero idle byte capture is unavailable and should be recorded during the shadow run; both individual zero field encodings are validated.
- No PV or AC value from QPIGS is in HVO scope.
