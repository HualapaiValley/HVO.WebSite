# Davis Vantage Pro2 Manufacturer Protocol

This document describes the Davis-defined protocol and data model. It should read like a technical reference for building a Davis driver. HVO implementation decisions belong in [hvo-implementation.md](hvo-implementation.md), not here.

## Provenance

| Source | Use |
|--------|-----|
| `../../VantageSerialProtocolDocs_v261.pdf` | Authoritative command, packet, EEPROM, CRC, and unit reference. |
| Official PDF URL | `https://cdn.shopify.com/s/files/1/0515/5992/3873/files/VantageSerialProtocolDocs_v261.pdf` |
| WeeWX `vantage.py` | Cross-check for wakeup, LOOP/LPS, DMPAFT, archive pages, EEPROM, rain bucket handling, and operational quirks. |
| WeeWX `crc16.py` | Cross-check for Davis CRC-CCITT behavior and example. |
| CumulusMX `DavisStation.cs` | Independent cross-check for command flow, archive download, CRC, TCP/serial behavior, retries, and rain conversion. |

## Communication Summary

| Field | Value |
|-------|-------|
| Transport | Davis serial command protocol; current HVO hardware uses TCP via WeatherLink IP adapter. |
| Authentication | None at Davis protocol layer. |
| Command terminator | ASCII commands are newline-terminated. |
| Wakeup response | `LF CR` (`0x0A 0x0D`). |
| ACK | `0x06`. |
| NAK | `0x21` (`!`). |
| CANCEL | `0x18` is defined by the protocol; active LOOP interruption can also be transport/implementation specific. |
| CRC | CRC-CCITT-16, polynomial `0x1021`, initial value `0x0000`. |

## Framing And CRC

| Topic | Protocol detail | Confidence |
|-------|-----------------|------------|
| Wakeup | Send newline wake sequence, then expect `LF CR`. Exact timing/retry strategy is implementation-specific. | High |
| Command framing | Send ASCII command plus newline. | High |
| Binary handshakes | Successful requests use ACK `0x06`; failed or retry flows can use NAK `0x21`. | High |
| CRC validation | A valid packet computes to `0x0000` when CRC is computed over data plus appended CRC bytes. | High |
| CRC append order | Big-endian: high byte followed by low byte. | High |
| CRC example | Needs re-check before documenting a numeric vector; current HVO packet validation is covered by append/validate tests. | Needs validation |
| LOOP packet size | 99 total bytes: 95 data bytes, 2 CRC bytes, 2 end bytes. | High |
| Archive page size | 267 bytes: 1 page byte, 5 records x 52 bytes, 4 unused bytes, 2 CRC bytes. | High |
| DMPAFT response size | 6 bytes: 2 pages, 2 start index, 2 CRC. | High |

## Unit Handling

Protocol/raw units are not the same thing as console display-unit settings.

| Measurement area | Protocol/raw encoding | Console display/config setting | Notes |
|------------------|-----------------------|--------------------------------|-------|
| Temperature | Fahrenheit-oriented encodings, commonly signed tenths deg F for main temperatures and byte-minus-90 deg F for some extra sensors. | EEPROM unit bits. | Display settings are not expected to change LOOP/archive encodings, but HVO still needs live validation on its console. |
| Wind | mph-oriented encodings; LOOP2 average wind speed fields use tenths mph. | EEPROM unit bits. | Direction is degrees in LOOP and 16 compass points in archive records. |
| Barometer | inHg-oriented encodings, commonly thousandths inHg. | EEPROM unit bits. | Station pressure, raw pressure, and altimeter pressure are separate values where available. |
| Rain | Bucket tip counts. | Rain display unit and rain bucket type are separate settings. | Bucket type determines conversion: `0=0.01in`, `1=0.2mm`, `2=0.1mm`. |
| Solar radiation | Direct numeric value in W/m2. | Not known. | Sensor-dependent. |
| UV index | Encoded value divided by 10 where applicable. | Not known. | Sensor-dependent. |

## Core Commands

| Operation | Command/framing | Response | Side effects | Notes |
|-----------|-----------------|----------|--------------|-------|
| LOOP stream | `LPS 2 <count>` | LOOP2 packets | None | Primary live weather stream. |
| LOOP1 single packet | `LPS 1 1` | LOOP1 packet | None | Provides status and fields not present in LOOP2. |
| Alternating LOOP | `LPS 3 <count>` | Alternating LOOP1/LOOP2 packets | None | Useful for a merged current-conditions snapshot. |
| Archive after time | `DMPAFT`, then timestamp payload with CRC | Page count/start index, then archive pages | None | Used for catchup. |
| Archive dump | `DMP` | Archive pages | None | Not the primary HVO path. |
| Console time read | `GETTIME` | Time fields | None | Console time drives archive timestamps. |
| Console time write | `SETTIME`, then time payload with CRC | ACK/status | Changes console clock | High caution. |
| Barometer data | `BARDATA` | Text lines | None | Includes current pressure, altitude, dew point, virtual temp, correction values. |
| Reception diagnostics | `RXCHECK`, `RECEIVERS` | Text/binary diagnostics | None | Receiver health and heard transmitter IDs. |
| Firmware/version | `NVER`, `VER` | Text lines | None | Firmware version/date. |
| Hardware discriminator | `WRD 12 4D` | Hardware-type byte | None | Used to identify Vantage Pro/Vue family. |
| EEPROM read | `EEBRD` | Bytes | None | Reads settings and calibration. |
| EEPROM write | `EEBWR` | ACK/status | Changes console settings | High caution. |
| Setup refresh | `NEWSETUP` | ACK/status | Applies setup changes | Usually follows some EEPROM writes. |
| Archive interval | `SETPER <minutes>` | Command response | Changes logging interval | Valid values require official-protocol confirmation; HVO currently accepts `1`, `5`, `10`, `15`, `30`, `60`, `120`. |
| Barometer set | `BAR=<pressure*1000> <altitudeFt>` | Command response | Changes barometer calibration | High caution. |
| Alarm clear | `CLRALM` | `OK` then `DONE` style completion | Clears thresholds | High caution. |
| Active alarm bits clear | `CLRBITS` | ACK/status | Clears active alarm bits | High caution. |
| Lamp control | `LAMPS <0|1>` | Command response | Toggles console lamp | Low risk. |
| Archive clear | `CLRLOG` | ACK/status | Clears archive memory | Destructive. |

## Command Coverage Table

This table is the working checklist for determining whether HVO has full Davis protocol coverage. It is based on the Rev 2.6.1 command summary extracted from the local PDF plus current HVO code coverage.

| Command | Vendor purpose | Access/safety | HVO support | HVO method/class | Unit test | Simulator test | Live test | Notes |
|---------|----------------|---------------|-------------|------------------|-----------|----------------|-----------|-------|
| Wake sequence | Put console in command mode. | Read/control primitive | Implemented | `DavisConsoleClient.WakeAsync` | N/A | Covered | Attempted | Includes retry and `LF CR` response handling. Live repeated-connect stress is adapter-sensitive. |
| `TEST` | Echo `TEST` for connection testing. | Read/test | Not implemented | None | N/A | Not planned | Not planned | Low value for HVO; can be added if needed for diagnostics. |
| `LPS 1 1` | Read one LOOP1 packet. | Read-only | Implemented | `VantageStation.GetLoop1Async`, `Loop2Packet.Parse` | Covered for current fields | Covered | Optional | Used once per worker batch. |
| `LPS 2 <count>` | Stream LOOP2 packets. | Read-only | Implemented | `VantageStation.StreamLoop2Async`, `Loop2Packet.Parse` | Covered for current fields | Covered | Optional | Main live weather stream. |
| `LPS 3 2` | Return alternating LOOP1/LOOP2 packets. | Read-only | Implemented | `VantageStation.GetCurrentConditionsAsync` | Covered via parser tests | Covered | Optional | Used for merged current conditions. |
| `LOOP <count>` | Stream legacy LOOP packets. | Read-only | Deferred | None; HVO uses `LPS` | Parser partially covered via LOOP1 packet type | Not implemented as command | Not planned | `LPS` supersedes this for HVO's LOOP1/LOOP2 needs. |
| `HILOWS` | Read current high/low block. | Read-only | Not implemented | None | N/A | Needed if supported | Optional | Out of current HVO scope; current/archive payloads do not use this 436-byte block. |
| `PUTRAIN` | Set yearly rainfall. | Write/high caution | Not implemented | None | N/A | Needed if supported | Needed with safety | High-risk historical counter write; defer unless operational need appears. |
| `PUTET` | Set yearly ET. | Write/high caution | Not implemented | None | N/A | Needed if supported | Needed with safety | High-risk historical counter write; defer unless operational need appears. |
| `DMPAFT` | Download archive records after timestamp. | Read-only | Implemented with v1 safety guard | `VantageStation.GetArchiveSinceAsync` | Covered for archive record parser | Covered | Measured | This console returns the documented 513-page full buffer for cursor requests. Production catch-up is disabled; bounded recovery is deferred to #346. |
| `DMP` | Archive dump. | Read-only | Not primary HVO path | None known | N/A | Needed if supported | Needed if supported | Decide whether out of scope or implement. |
| `GETTIME` | Read console clock. | Read-only | Implemented | `VantageStation.GetConsoleTimeAsync`, `GetStationInfoAsync` | N/A | Covered | Covered for basic live read | Console time affects archive UTC conversion; CRC retry is covered. |
| `SETTIME` | Set console clock. | Write/high caution | Implemented | `VantageStation.SetConsoleTimeAsync` | Payload covered | Covered | Needed with safety | Requires local-only confirmation/audit before UI exposure. |
| `BARDATA` | Read barometer and correction data. | Read-only | Implemented | `VantageStation.GetBarometerDataAsync` | N/A | Covered | Optional | Text response parsing covers decimal and thousandths-inHg pressure forms. |
| `RXCHECK` | Read reception counters. | Read-only | Implemented | `VantageStation.GetReceptionStatsAsync` | N/A | Covered | Optional | Text response parsing. |
| `RXTEST` | Exit `Receiving from` screen and clear RXCHECK CRC count. | Control/diagnostic | Not implemented | None | N/A | Needed if supported | Optional/manual | Potential recovery command after console power loss; not used by current worker. |
| `RECEIVERS` | Read heard transmitters. | Read-only | Implemented | `VantageStation.GetHeardTransmitterIdsAsync` | N/A | Covered | Optional | Binary payload after `OK` prefix. |
| `NVER` | Read firmware version. | Read-only | Implemented | `VantageStation.GetStationInfoAsync` | N/A | Covered | Optional | Text response parsing. |
| `VER` | Read firmware date/version string. | Read-only | Implemented | `VantageStation.GetStationInfoAsync` | N/A | Covered | Optional | Text response parsing. |
| `WRD 12 4D` | Read hardware discriminator. | Read-only | Implemented | `VantageStation.GetStationInfoAsync` | N/A | Covered | Optional | Detects Vantage Pro/Vue family. |
| `EEBRD` | Read EEPROM bytes. | Read-only | Implemented | `VantageStation.GetStationSettingsAsync`, related read helpers | N/A | Covered | Optional | Used for settings, calibration, alarms, transmitters; command ACK and data CRC retries are covered. |
| `EEBWR` | Write EEPROM bytes. | Write/high caution | Implemented for selected settings | `VantageStation` write helpers | Payload covered | Covered | Needed with safety | Payload shape and CRC retry are covered; live writes need read-back validation plan. |
| `GETEE` | Read full 4K EEPROM block. | Read-only | Not implemented | None | N/A | Needed if supported | Optional | Current HVO reads targeted EEPROM fields only. |
| `EERD` | Read EEPROM bytes as text hex lines. | Read-only | Not implemented | None | N/A | Needed if supported | Optional | HVO uses binary `EEBRD` instead. |
| `EEWR` | Write one EEPROM byte as text hex. | Write/high caution | Not implemented | None | N/A | Needed if supported | Needed with safety | HVO uses binary `EEBWR` for selected fields. |
| `NEWSETUP` | Apply changed setup. | Command/high caution | Implemented where needed | `VantageStation.RunNewSetupAsync` | N/A | Covered | Needed with safety | Usually follows selected EEPROM writes. |
| `SETPER` | Set archive interval. | Write/high caution | Implemented | `VantageStation.SetArchiveIntervalAsync`, `UpdateRainArchiveSettingsAsync` | N/A | Covered | Needed with safety | HVO accepts `1`, `5`, `10`, `15`, `30`, `60`, `120`. |
| `BAR=` | Set barometer calibration. | Write/high caution | Implemented | `VantageStation.SetBarometerAsync` | N/A | Covered | Needed with safety | Requires careful UI and read-back. |
| `CALED` | Read calibrated temperature/humidity values for calibration workflow. | Read-only/calibration | Not implemented | None | N/A | Needed if supported | Optional | HVO reads calibration offsets directly from EEPROM instead. |
| `CALFIX` | Update display after calibration number changes using raw sensor values. | Write/high caution | Not implemented | None | N/A | Needed if supported | Needed with safety | HVO writes calibration offsets but does not implement this display-refresh workflow. |
| `CLRALM` | Clear configured alarm thresholds. | Command/high caution | Implemented | `VantageStation.ClearAlarmThresholdsAsync` | N/A | Covered | Needed with safety | Waits for `DONE`. |
| `CLRCAL` | Clear all temperature/humidity calibration offsets. | Write/high caution | Not implemented | None | N/A | Needed if supported | Needed with safety | Safer to expose individual reviewed calibration writes first. |
| `CLRGRA` | Clear all console graph points. | Destructive/local display | Not implemented | None | N/A | Needed if supported | Only with explicit manual approval | Not needed for telemetry. |
| `CLRVAR` | Clear selected rain or ET variable. | Destructive/data | Not implemented | None | N/A | Needed if supported | Only with explicit manual approval | Could alter rain/ET history; defer. |
| `CLRHIGHS` | Clear daily/monthly/yearly high values. | Destructive/data | Not implemented | None | N/A | Needed if supported | Only with explicit manual approval | Not needed for telemetry. |
| `CLRLOWS` | Clear daily/monthly/yearly low values. | Destructive/data | Not implemented | None | N/A | Needed if supported | Only with explicit manual approval | Not needed for telemetry. |
| `CLRBITS` | Clear active alarm bits. | Command/high caution | Implemented | `VantageStation.ClearActiveAlarmBitsAsync` | N/A | Covered | Needed with safety | Active alarm state command. |
| `CLRDATA` | Clear all current data values to dashes. | Destructive/data | Not implemented | None | N/A | Needed if supported | Only with explicit manual approval | Not needed for telemetry; could disrupt current readings. |
| Alarm threshold EEPROM block | Configure alarm thresholds. | Write/high caution | Implemented | `VantageStation.SetAlarmThresholdsAsync` | Payload covered | Covered | Needed with safety | HVO validates and writes alarm block. |
| `LAMPS` | Toggle console lamp. | Command/low risk | Implemented | `VantageStation.SetLampAsync` | N/A | Covered | Optional/gated | Low-risk local action; opt-in live test exists. |
| `CLRLOG` | Clear archive memory. | Destructive | Implemented | `VantageStation.ClearArchiveAsync` | N/A | Covered for command ACK | Only with explicit manual approval | Destructive; no routine live validation. |
| `BAUD` | Change console serial baud rate. | Transport/configuration | Not implemented | None | N/A | Not planned | Not planned | Not applicable to current TCP/WeatherLink IP deployment; risky for serial access. |
| `GAIN` | Set radio receiver gain on Vantage Pro. | Configuration/diagnostic | Not implemented | None | N/A | Not planned | Not planned | Official PDF says not implemented on Vantage Pro2/Vue. |
| `STOP` | Disable archive record creation. | Write/high caution | Not implemented | None | N/A | Needed if supported | Needed with safety | Not needed for HVO and risks archive gaps. |
| `START` | Enable archive record creation after `STOP`. | Write/high caution | Not implemented | None | N/A | Needed if supported | Needed with safety | Only useful if `STOP` is supported. |

## Deferred Command Rationale

These commands are intentionally not implemented unless a concrete HVO operational need appears.

| Command/group | Potential HVO value | Why deferred |
|---------------|---------------------|--------------|
| `TEST` | Manual connectivity smoke test. | Wake/ACK and real command flows already validate connectivity more directly. |
| `LOOP` | Legacy current-condition stream. | `LPS` covers HVO's LOOP1, LOOP2, and alternating-packet needs. |
| `HILOWS` | Richer local dashboard with console high/low values. | Not required for current live/archive telemetry; would need parser/model/API design for the 436-byte block. |
| `PUTRAIN`, `PUTET` | Maintenance correction of yearly rain/ET counters. | Mutates historical counters; only appropriate behind maintenance UI with confirmation, audit, and read-back. |
| `GETEE` | Full EEPROM backup/diagnostics. | Current HVO reads targeted EEPROM fields; full dump needs redaction/export policy before exposing. |
| `EERD`, `EEWR` | Alternate text EEPROM access. | Binary `EEBRD`/`EEBWR` are already implemented and better suited to structured reads/writes with CRC. |
| `CALED`, `CALFIX` | Guided calibration workflow and display refresh after calibration edits. | Calibration writes need a full safety/read-back workflow before expanding live calibration operations. |
| `CLRCAL` | Bulk reset of temperature/humidity calibration offsets. | High-risk bulk reset; individual reviewed calibration writes are safer. |
| `CLRGRA` | Clear console graph points. | Local console display housekeeping only; not useful for HVO telemetry. |
| `CLRVAR`, `CLRHIGHS`, `CLRLOWS` | Maintenance reset of selected rain/ET/high/low values. | Destructive to console history; requires explicit maintenance workflow and audit. |
| `CLRDATA` | Clear current data values to dashes. | Can disrupt telemetry and has no normal HVO operational use. |
| `BAUD` | Serial transport configuration. | Current deployment uses WeatherLink/IP TCP; baud changes are irrelevant and can break serial access. |
| `GAIN` | Receiver gain control on older Vantage Pro models. | Official PDF says it is not implemented on Vantage Pro2/Vue. |
| `STOP` | Pause archive creation. | HVO depends on archive records; stopping archive creation risks data gaps. |
| `START` | Resume archive creation after `STOP`. | Only useful if HVO supports `STOP`; keep as manual recovery unless `STOP` is ever exposed. |

## LOOP Packets

| Packet type | Type byte | Common command | Important fields |
|-------------|-----------|----------------|------------------|
| LOOP1 | `0` | `LPS 1 1` | Battery, transmitter battery bitmask, forecast, sunrise/sunset, monthly/yearly rain/ET, extra/soil/leaf sensors. |
| LOOP2 | `1` | `LPS 2 <count>` | Derived values, raw/altimeter pressure, precision wind averages, 2-minute wind, 10-minute gust, 15-minute/hour/24-hour rain. |

Packet notes:

- Data starts with marker bytes `LOO`.
- Dash/missing values are field-specific sentinels such as `0xFF`, `0x7FFF`, or `0xFFFF`.
- Wind direction `360` represents north in current driver practice; calm/dash handling is field-specific.
- Barometric trend is a signed byte; common values are `-60`, `-20`, `0`, `20`, `60`.
- LOOP1 and LOOP2 are complementary and should not be treated as a single identical schema.

## Archive Records

| Topic | Detail |
|-------|--------|
| Record type | Type B Vantage Pro2 archive records. |
| Record size | 52 bytes. |
| Records per page | 5. |
| Date/time | Date stamp encodes year/month/day; time stamp is HHMM. |
| Rain semantics | Archive rain is interval rain, not daily/storm/rate. |
| Wind direction | Archive direction/gust direction are encoded as 16 compass points and convert by multiplying by 22.5 degrees. |
| Derived weather | Archive records do not contain every live LOOP2 derived value such as dew point, heat index, wind chill, and THSW. |

## EEPROM Settings Referenced By HVO

| Address | Meaning | Notes |
|---------|---------|-------|
| `0x29` | Unit bits | Display unit preferences. |
| `0x2B` | Setup bits | Includes rain bucket type bits `[5:4]`. |
| `0x2C` | Rain year start month | `1=January`. |
| `0x2D` | Archive interval | Minutes. |
| `0x0F` | Altitude | Signed short, feet. |
| `0x0B` | Latitude | Signed short, x10 degrees. |
| `0x0D` | Longitude | Signed short, x10 degrees. |
| `0x11` | Timezone code | Davis timezone table. |
| `0x12` | DST manual/auto | Used with daylight-savings byte. |
| `0x13` | Daylight savings | Manual DST on/off. |
| `0x14` | GMT offset | Signed short, hundredths of hours. |
| `0x16` | GMT-or-zone selector | Chooses timezone code vs manual GMT offset. |
| `0x17` | Use transmitters bitmask | Channels 1-8. |
| `0x18` | Retransmit channel | Channel or off. |
| `0x19` | Transmitter configs | 16 bytes, 2 per channel. |
| `0x32` | Temperature calibration block | 27-byte block in HVO code. |
| `0x44` | Inside humidity calibration | Signed byte. |
| `0x45` | Outside humidity calibration | Signed byte. |
| `0x4D` | Wind direction calibration | Signed short. |
| `0x52` | Alarm threshold block | 94-byte block. |
| `0xFFC` | Temperature logging | `0=AVERAGE`, `1=LAST` in HVO code. |

## Manufacturer Quirks To Preserve In Implementations

- WeatherLink IP/TCP can require pacing between sends.
- WeatherLink IP can prefix an ACK with `LF CR`.
- CRC should be verified before parsing binary payloads.
- Bad CRC/read attempts should use retry behavior compatible with the protocol, including NAK where appropriate.
- `DMPAFT` can have firmware-specific edge cases around timestamps outside the circular buffer.
- Rain fields must keep bucket type and reset/window semantics separate.
