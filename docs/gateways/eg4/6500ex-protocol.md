# EG4 6500EX Read-Only Telemetry Protocol

Status: battery, dual-MPPT, AC/load, temperature, operating-state, and firmware detail validated through allowlisted PI30 inquiries. BMS RS485 and all setters remain prohibited.

Research dates: 2026-08-09 through 2026-08-10. Target: EG4 6500EX-48, model `MKS2-6500`, general model `045`; installed firmware `79.71/61.13`; official update `79.72/61.13` statically analyzed.

## Safety Boundary

- Use only the inverter RS232/COM monitoring interface, exposed on the installed unit as USB HID `0665:5161`.
- Never connect this adapter to the closed-loop BMS RS485 bus.
- One process owns the HID endpoint and sends one inquiry at a time with bounded timeout/reconnect behavior.
- No arbitrary-command API exists. PI30 setters, date/time writes, resets, firmware operations, and command discovery are prohibited.
- On reconnect, identity and the exact firmware tuple are revalidated before status polling.

## Evidence

| ID | Source | Claims | Limits |
|---|---|---|---|
| E1 | [Official manual](https://eg4electronics.com/wp-content/uploads/2024/05/EG4_6.5_Manual.pdf) | Exact model and physical communication interfaces. | Does not publish the PI30 command map. |
| E2 | [PI30MAX protocol](https://github.com/jblance/mpp-solar/blob/master/docs/protocols/PI30MAX.Communication.Protocol20210217.pdf) | Inquiry framing, adjusted CRC, QPIGS, QPIGS2, and QPGS field definitions. | Community-hosted OEM-family document. |
| E3 | [SolarAssistant 6500EX support](https://solar-assistant.io/help/inverters/eg4/6500EX-48) | Independent direct-model implementation; current documentation supports RS232 and Micro USB. | Does not publish its polling implementation. |
| H1 | HVO HID capture, 2026-08-09 | Installed identity and exact CRC-valid QPIGS response. | One installed firmware tuple. |
| H2 | HVO aligned HID capture, 2026-08-10 | QPIGS, QPGS0, and Q1 returned battery, AC/load, dual PV voltage/current, and four temperature channels. | QPGS0 PV currents are integer-resolution. |
| H3 | Official `EG4-6500-firmware.zip` static analysis | ZIP SHA-256 `bcc42603a72479cb7165ab107567b0ee2dcbdd7c44ebee0199f6ae680f68f0`; `DSP7972.inv` SHA-256 `6eb3202ed579dbfdf484f1f6568f6e3514401ff65dec1fcb7a5fb548d7f4aa65`. Firmware 79.72 contains exact QPIGS2 command/dispatcher/handler entries. | Firmware was treated as data only; never executed or flashed. |
| H4 | SolarAssistant REST metrics, 2026-08-10 | Existing authenticated read-only API at the SolarAssistant host reports direct `pv_power_1/2`, `pv_voltage_1/2`, and `pv_current_1/2`. | The inverter WiFi module at `192.168.1.122` had no listener on bounded likely TCP telemetry ports; it is not used by HVO. |

## Wire Profile

| Property | Value |
|---|---|
| RS232 serial | 2400 baud, 8 data bits, no parity, 1 stop bit |
| Request | ASCII inquiry + adjusted CRC high/low + `CR` |
| CRC | CRC-CCITT/XMODEM, polynomial `0x1021`, initial `0x0000`; bytes `00`, `0A`, `0D`, `28` incremented |
| Response | `(` + payload + adjusted CRC high/low + `CR` |
| Negative response | `(NAK` + CRC + `CR` |
| USB HID | Linux hidraw, 8-byte reports, USB `0665:5161` |

## Inquiry Allowlist

| Inquiry | Purpose | Firmware rule |
|---|---|---|
| `QPI`, `QMN`, `QGMN`, `QVFW`, `QVFW3` | Identity and exact firmware proof | Startup/reconnect |
| `QPIGS` | Battery branch, AC input/output, local load, temperature, direct MPPT 1 | All supported tuples |
| `QPGS0` | Mode, fault, status, parallel totals, PV2 voltage and coarse integer current | All supported tuples |
| `Q1` | SCC/inverter/battery-channel/transformer temperatures, fan, charge stage, diagnostics | All supported tuples |
| `QPIGS2` | Direct MPPT 2 current, voltage, and power | Firmware `79.72/61.13` only |

Installed firmware `79.71/61.13` does not respond to QPIGS2. Production never sends it to that tuple. Static C28 analysis found `QPIGS2` at word address `0x3EF3DA`, dispatch pointer `0x3EFA16`, and handler `0x3DC6AA` in official 79.72. Firmware 79.72 is allowlisted and conditionally enables the inquiry, but HVO does not flash firmware as part of this work.

## Field Use

### QPIGS

QPIGS provides direct AC input/output voltage and frequency, active/apparent load, load percentage, DC bus voltage, battery voltage, charge/discharge current magnitudes, inverter-reported battery capacity, inverter temperature, and MPPT 1 voltage/current/power.

Canonical battery current is `discharge - charge`: positive discharge and negative charge. Battery power is `voltage * canonical current`; both are derived. Inverter-reported battery capacity remains an observation and does not replace JK BMS or SmartShunt SOC authority.

The gateway and main-site user interfaces intentionally present the opposite, battery-facing sign for readability: positive means energy entering the batteries and negative means battery discharge supporting the inverter/load. This is a display-only inversion; persisted payloads and composition retain the canonical convention above.

### QPGS0

The 29-field response provides operating mode, fault code, output/load totals, status flags, output mode, charger priority, and both tracker voltage/current channels. MPPT 2 current is whole-ampere resolution on installed 79.71, so `voltage * current` is marked derived/coarse and is not preferred for the canonical three-array aggregate.

The device serial field is parsed only to validate field shape and is never forwarded, logged, or committed in fixtures.

### Q1

The 17-field base plus optional 10-field extension provides:

- SCC PWM, inverter, battery-channel, and transformer temperatures;
- parallel role, fan-lock state, fan PWM percentage;
- MPPT 1/SCC charge power and parallel-warning flags;
- charge stage (`none`, `bulk`, `absorb`, or `float`);
- equalization configuration/status extension values retained only in the raw decoder when needed.

The Q1 timing fields are not firmware identity. Firmware comes only from QVFW/QVFW3.

## Canonical Three-Array Policy

SolarAssistant already provides direct high-resolution metrics for both 6500EX trackers. HVO therefore composes site PV from exactly:

1. `solarassistant-total/mppt-1`
2. `solarassistant-total/mppt-2`
3. `eg4-mppt100-48hv-a/mppt-1`

The aggregate is derived only when all three powers are fresh and timestamps are within the configured skew. Otherwise the existing SolarAssistant aggregate remains the fallback. The coarse HID MPPT 2 estimate is retained in raw EG4 detail history for comparison but excluded from the canonical list and sum, preventing double-counting.

## Failures And History

Zero current is valid. Timeout, NAK, CRC failure, missing CR, wrong field count, identity mismatch, unsupported firmware, or parse failure makes the current observation unavailable; none is converted to zero.

Every successful sample emits a battery branch, MPPT detail, and typed inverter detail. Inverter detail includes AC input/output, active/apparent load, battery branch, mode/fault/load/status flags, four temperatures, PV channels, firmware, fan, and charge-stage diagnostics. Distinct MPPT timestamps are retained for historical queries.

## Supported Firmware

- `79.02/61.00`: historical/public exact-model evidence.
- `79.71/61.13`: installed and live validated; uses QPIGS/QPGS0/Q1.
- `79.72/61.13`: official package statically validated; additionally uses QPIGS2.

Any other tuple is rejected before status polling.
