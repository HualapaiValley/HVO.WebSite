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
| CRC example | WeeWX confirms `0xCEC6 0x03A2 -> 0xE2B4`. | High |
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

This table is the working checklist for determining whether HVO has full Davis protocol coverage. It is not yet a complete command-by-command transcription of the official PDF.

| Command | Vendor purpose | Access/safety | HVO support | HVO method/class | Unit test | Simulator test | Live test | Notes |
|---------|----------------|---------------|-------------|------------------|-----------|----------------|-----------|-------|
| Wake sequence | Put console in command mode. | Read/control primitive | Implemented | `DavisConsoleClient.WakeAsync` | Needed | Needed | Needed | Includes retry and `LF CR` response handling. |
| `LPS 1 1` | Read one LOOP1 packet. | Read-only | Implemented | `VantageStation.GetLoop1Async`, `Loop2Packet.Parse` | Needed | Needed | Optional | Used once per worker batch. |
| `LPS 2 <count>` | Stream LOOP2 packets. | Read-only | Implemented | `VantageStation.StreamLoop2Async`, `Loop2Packet.Parse` | Needed | Needed | Optional | Main live weather stream. |
| `LPS 3 2` | Return alternating LOOP1/LOOP2 packets. | Read-only | Implemented | `VantageStation.GetCurrentConditionsAsync` | Needed | Needed | Optional | Used for merged current conditions. |
| `DMPAFT` | Download archive records after timestamp. | Read-only | Implemented | `VantageStation.GetArchiveSinceAsync` | Needed | Needed | Needed | Startup catchup does not currently enable `fallbackOnEmpty`. |
| `DMP` | Archive dump. | Read-only | Not primary HVO path | None known | N/A | Needed if supported | Needed if supported | Decide whether out of scope or implement. |
| `GETTIME` | Read console clock. | Read-only | Implemented | `VantageStation.GetConsoleTimeAsync`, `GetStationInfoAsync` | Needed | Needed | Needed | Console time affects archive UTC conversion. |
| `SETTIME` | Set console clock. | Write/high caution | Implemented | `VantageStation.SetConsoleTimeAsync` | Needed | Needed | Needed with safety | Requires local-only confirmation/audit before UI exposure. |
| `BARDATA` | Read barometer and correction data. | Read-only | Implemented | `VantageStation.GetBarometerDataAsync` | Needed | Needed | Optional | Text response parsing. |
| `RXCHECK` | Read reception counters. | Read-only | Implemented | `VantageStation.GetReceptionStatsAsync` | Needed | Needed | Optional | Text response parsing. |
| `RECEIVERS` | Read heard transmitters. | Read-only | Implemented | `VantageStation.GetHeardTransmitterIdsAsync` | Needed | Needed | Optional | Binary payload after `OK` prefix. |
| `NVER` | Read firmware version. | Read-only | Implemented | `VantageStation.GetStationInfoAsync` | Needed | Needed | Optional | Text response parsing. |
| `VER` | Read firmware date/version string. | Read-only | Implemented | `VantageStation.GetStationInfoAsync` | Needed | Needed | Optional | Text response parsing. |
| `WRD 12 4D` | Read hardware discriminator. | Read-only | Implemented | `VantageStation.GetStationInfoAsync` | Needed | Needed | Optional | Detects Vantage Pro/Vue family. |
| `EEBRD` | Read EEPROM bytes. | Read-only | Implemented | `VantageStation.GetStationSettingsAsync`, related read helpers | Needed | Needed | Optional | Used for settings, calibration, alarms, transmitters. |
| `EEBWR` | Write EEPROM bytes. | Write/high caution | Implemented for selected settings | `VantageStation` write helpers | Needed | Needed | Needed with safety | Must use read-back validation plan. |
| `NEWSETUP` | Apply changed setup. | Command/high caution | Implemented where needed | `VantageStation.RunNewSetupAsync` | Needed | Needed | Needed with safety | Usually follows selected EEPROM writes. |
| `SETPER` | Set archive interval. | Write/high caution | Implemented | `VantageStation.SetArchiveIntervalAsync`, `UpdateRainArchiveSettingsAsync` | Needed | Needed | Needed with safety | HVO accepts `1`, `5`, `10`, `15`, `30`, `60`, `120`. |
| `BAR=` | Set barometer calibration. | Write/high caution | Implemented | `VantageStation.SetBarometerAsync` | Needed | Needed | Needed with safety | Requires careful UI and read-back. |
| `CLRALM` | Clear configured alarm thresholds. | Command/high caution | Implemented | `VantageStation.ClearAlarmThresholdsAsync` | Needed | Needed | Needed with safety | Waits for `DONE`. |
| `CLRBITS` | Clear active alarm bits. | Command/high caution | Implemented | `VantageStation.ClearActiveAlarmBitsAsync` | Needed | Needed | Needed with safety | Active alarm state command. |
| Alarm threshold EEPROM block | Configure alarm thresholds. | Write/high caution | Implemented | `VantageStation.SetAlarmThresholdsAsync` | Needed | Needed | Needed with safety | HVO validates and writes alarm block. |
| `LAMPS` | Toggle console lamp. | Command/low risk | Implemented | `VantageStation.SetLampAsync` | Needed | Needed | Optional | Low-risk local action. |
| `CLRLOG` | Clear archive memory. | Destructive | Implemented | `VantageStation.ClearArchiveAsync` | Optional | Needed | Only with explicit manual approval | Destructive; simulator-first. |
| Other official Davis commands | TBD from official PDF. | TBD | Not documented yet | TBD | TBD | TBD | TBD | Complete this table from official protocol manual before claiming full coverage. |

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
