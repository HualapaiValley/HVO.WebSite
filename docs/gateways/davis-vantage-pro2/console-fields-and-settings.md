# Davis console fields and settings

This reusable reference extracts protocol/field facts from the complete [May 8, 2026 inventory](../../archive/2026-05-08-davis-console-inventory.md) and reconciles them with the current [station implementation](../../../src/HVO.Hardware.DavisVantagePro2/Station/VantageStation.cs), [models](../../../src/HVO.Hardware.DavisVantagePro2/Station/Models/StationModels.cs) and [vendor protocol reference](manufacturer-protocol.md). It does not restore the former UI, approve its alarm/storage proposals or report new hardware tests. [Current headless contracts](hvo-api-contracts.md) own routes/configuration and [validation notes](validation-notes.md) retain unproven live behavior.

## Observation units and missing values

LOOP encodings are independent of console EEPROM display preferences. Main temperatures decode from signed tenths of degrees Fahrenheit; many extra/soil/leaf temperatures use byte-minus-90 °F. Wind is mph, with LOOP2 averages in tenths mph; LOOP directions are degrees, while archive direction uses 16 points multiplied by 22.5°. Pressure values use inHg (often thousandths); corrected pressure, raw station pressure and altimeter pressure are separate measurements. Solar radiation is W/m² and UV encodings divide by ten.

Rain tip counts use the configured bucket: `0 = 0.01 inch`, `1 = 0.2 mm`, `2 = 0.1 mm`, independently of rain display units. Rate, daily, 15-minute, hourly, 24-hour, storm, monthly/yearly and archive interval rain have distinct reset/window semantics. Storm start is a console-local date; it is not a UTC event timestamp. Common missing sentinels include `0xFF`, `0x7FFF` and `0xFFFF`, but interpretation is field-specific. Preserve nulls rather than invent a measurement or apply one blanket sentinel rule. Physical display-unit independence and installed bucket/timezone/DST behavior remain live qualification questions.

| Source | Reusable field groups | Distinction |
|--------|-----------------------|-------------|
| LOOP1 and LOOP2 | Inside/outside temperature/humidity, corrected barometer/trend, instantaneous wind/direction, daily/storm/rate rain, solar, UV, daily ET | Parser fields and typed payloads explicitly name engineering units. |
| LOOP2 | Dew point, heat index, wind chill, THSW, raw/altimeter pressure, precision 10-minute/2-minute wind, gust/direction, 15-minute/hour/24-hour rain | These weather indices are console-derived, not HVO fallback calculations. |
| LOOP1 | Console voltage, transmitter battery bitmask, forecast rule/icons/text, sunrise/sunset, monthly/yearly rain/ET | Bit N denotes low battery on channel N+1. Forecast text is the HVO lookup of the vendor rule. Sunrise/sunset decode HHMM. |
| LOOP1 optional sensors | Extra temperatures/humidities (up to seven), soil temperature/moisture and leaf temperature/wetness (up to four) | Soil moisture is centibars and leaf wetness is a 0–15 scale. Availability depends on configured sensors. |
| Archive type B | Interval temperature/high/low, wind average/gust/samples, rain/rate, solar/high solar, UV/high UV, ET, forecast and optional sensors | 52-byte record; preserves console-local time, UTC and archive interval separately. Not every LOOP2 derived field exists in an archive record. |

The current [LOOP parser](../../../src/HVO.Hardware.DavisVantagePro2/Protocol/Packets/Loop2Packet.cs), [archive parser](../../../src/HVO.Hardware.DavisVantagePro2/Protocol/Packets/ArchiveRecord.cs) and [typed payload tables](hvo-api-contracts.md#live-outbox-payload) define actual field availability. Parser coverage does not imply every field is projected to HA or persisted in the slim central raw schema. LOOP1 may be older when its refresh fails and a fresh LOOP2 is merged with the retained cache.

## Identity and clock

`WRD 12 4D` identifies hardware (`16` Vantage Pro family, `17` Vue); model mapping distinguishes Vantage Pro/Pro2. `NVER` and `VER` provide firmware version/date text. `GETTIME` returns the console clock; `SETTIME` changes it at the low-level library layer. A clock write changes the archive timestamp basis and needs a separately approved recovery/readback plan.

The archive worker preserves `ConsoleRecordedAtLocal` with Unspecified kind, converts it using the console offset and records UTC independently. Host-local timezone is not a substitute. The migration requires an explicit `Station:LegacyArchiveConsoleUtcOffsetHours` before connection. [#346](https://github.com/HualapaiValley/HVO.WebSite/issues/346) retains date encoding, firmware, WeatherLink translation, clock/DST and bounded recovery questions.

## EEPROM setup and display preferences

Addresses come from [DavisProtocol](../../../src/HVO.Hardware.DavisVantagePro2/Protocol/DavisProtocol.cs); decode/write support comes from `VantageStation`. A low-level setter is not an exposed operator endpoint or live acceptance proof.

| Setting | EEPROM address/encoding | Current named support and limits |
|---------|--------------------------|----------------------------------|
| Latitude / longitude | `0x0B` / `0x0D`, signed 16-bit little-endian tenths of degrees | Named read/set helpers; preserve signs and units. |
| Altitude | `0x0F`, signed 16-bit feet | Named read/set helper; affects corrected barometer. |
| Timezone code | `0x11`, code 0–31 | `SetTimezoneCodeAsync` validates range; use the [complete current lookup](../../../src/HVO.Hardware.DavisVantagePro2/Protocol/DavisTimeZoneTable.cs) or preserved archive Appendix A. |
| DST manual/auto and flag | `0x12` / `0x13` | `AUTO`, `ON`, `OFF`; interpretation is separate from base offset. |
| GMT offset and selector | `0x14`, signed hundredths of hours; `0x16` chooses code (`0`) or explicit offset (`1`) | Modes are mutually exclusive. Example encoding `-700` means UTC−7; some table codes have half-hour offsets. |
| Unit bits | `0x29`: barometer `[1:0]`, temperature `[3:2]`, rain bit `5`, wind `[7:6]` | Decoded/cached; no dedicated named display-unit setter. Raw EEPROM possibility is not approved support. |
| Barometer units | Unit-bit codes 0–3 | inHg, mmHg, hPa, mbar; do not change observation pressure units. |
| Temperature units | Unit-bit codes 0–3 | °F, °F×10, °C, °C×10; preserve raw/presentation distinction. |
| Rain / wind display | inch/mm; mph, m/s, km/h, knots | Rain display does not select physical bucket size. |
| Rain bucket | `0x2B` setup bits `[5:4]` | 0.01in / 0.2mm / 0.1mm; named helper. |
| Rain-year start | `0x2C`, month 1–12 | Named helper; changes yearly-reset interpretation. |
| Archive interval | `0x2D`, minutes | `SETPER` accepts 1, 5, 10, 15, 30, 60, 120; cached seconds are minutes×60. |
| Temperature logging | `0xFFC` | `0 = AVERAGE`, `1 = LAST`; not a host sampling interval. |

Console setup may involve `EEBWR` plus `NEWSETUP`; related settings should be treated as a coherent maintenance change. EEPROM reads/writes and ACK/CRC retries are transport contracts, not transactional rollback or installed readback proof.

## Transmitters and calibration

Eight transmitter channels use receive mask `0x17`, retransmit channel `0x18` and sixteen configuration bytes from `0x19` (two per channel). Decoded fields include channel/type, repeater A–H or none, active/retransmit flags and extra-temperature/humidity sensor IDs. Named setters validate supplied extra IDs 1–7. The type names include `iss`, `temp`, `hum`, `temp_hum`, `wind`, `rain`, `leaf`, `soil`, `leaf_soil`, `sensorlink`, `none`. `RECEIVERS` reports heard-channel bitmap; hearing a transmitter and having it configured are distinct.

| Calibration | Encoding/model | Named write constraint |
|-------------|----------------|------------------------|
| Temperature | Signed 0.1°F offsets in the block from `0x32`; inside, outside, seven extra, four soil and four leaf values | `inTemp`, `outTemp`, `extraTemp1–7`, `soilTemp1–4`, `leafTemp1–4`; −12.8 to +12.7°F. Inside offset writes the adjacent complement too. |
| Humidity | Signed percentage offsets, inside `0x44`, outside `0x45` and extra block | `inHumid`, `outHumid`, `extraHumid1–7`; −100 to +100 percentage points. |
| Wind direction | Signed 16-bit degrees at `0x4D` | −359 to +359 degrees. |

Invalid calibration variable names fail before console I/O. Encoded calibration and measured temperature/humidity are separate values; alarm or parser metadata should not overwrite the source-native observation.

## Service diagnostics and alarms

`BARDATA` returns corrected pressure (inHg), altitude (feet), dew point/virtual temperature (°F), correction factor/ratio/constant, gain and error offset. `RXCHECK` returns received/missed packets, resynchronizations, longest good stretch and CRC errors; reception percentage is derived. These are low-level methods, not dedicated web routes.

The 94-byte EEPROM alarm block starts at `0x52`. [AlarmThresholds](../../../src/HVO.Hardware.DavisVantagePro2/Station/Models/StationModels.cs) covers rising/falling pressure trend, time alarm, inside/outside/extra/soil/leaf temperature, humidity, dew point/wind chill/heat index/THSW, instantaneous/10-minute wind, UV/dose/solar, rain rate/15-minute/24-hour/storm, daily ET, soil moisture and leaf wetness. Preserve their units and null/disabled encodings; a threshold is not an alarm-history event.

`CLRALM` clears configured thresholds; `CLRBITS` clears active bits; they are different actions. `LAMPS` toggles the lamp, with no dedicated readback state in the historical inventory. Programmatic audible-alarm triggering remains unknown. The archived alarm definition/destination/event/delivery schemas, email/SMS/webhook workflows and wireframes are proposals; current headless hosting implements none of that notification/history architecture.

## Archive continuity and write safety

The historical `513`-page/start-index-`1` cursor response is preserved in [#346](https://github.com/HualapaiValley/HVO.WebSite/issues/346). Routine cursor requests cancel a full-buffer response with `<ESC>` (`0x1B`) before page one. An explicitly requested initial full bootstrap (`DateTime.MinValue`) is distinct. Finite batches accept at most 25 archive records; cursor advancement follows durable outbox insertion or confirmed duplicate. Stale leading circular records are skipped; early exit sends ESC, and interrupted in-flight pages may leave the mode Unknown for recovery. These safeguards do not establish efficient circular recovery; recurring production catch-up stays disabled.

Low-level clock, interval, location/timezone/DST, rain, calibration, transmitter, barometer and alarm helpers exist. `CLRLOG` is destructive. There is no generic command passthrough, current local write UI or cloud write lane. Any proposed exposure needs authorization, a reviewed allowlist, explicit side-effect confirmation, safe audit, readback/failure handling and recovery; destructive archive clear needs separate manual approval and an export/backup decision. Simulated ACK is not installed-device readback, and this documentation task runs no commands against a console.

Shared host configuration, outbox queue diagnostics and runtime batch/sweep tuning are application-owned, not EEPROM. Follow [operations](../../GATEWAY_OPERATIONS.md), [typed contracts](hvo-api-contracts.md) and the [quiescent SQLite recovery procedure](../sqlite-backup-and-rollback.md); preserve local settings/cursor and every post-checkpoint observation. Do not revive the old separate Weather/BMS/Website-store proposals as current schema ownership.
