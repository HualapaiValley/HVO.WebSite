# Davis Console And UI Value Inventory

This document inventories the Davis weather station values that matter for UI planning in this repo.

Current status:
- As of May 8, 2026, the Davis protocol surface in this repo is stable enough for UI design and settings-page planning.
- The repo now exposes LOOP values, non-LOOP console command values, EEPROM-backed settings, alarm thresholds, heard-transmitter state, and the application-owned worker/outbox configuration that sits beside the console.

Scope:
- Included: LOOP1 / LOOP2 values, values read through `WRD`, `NVER`, `VER`, `GETTIME`, `EEBRD`, `BARDATA`, `RXCHECK`, `RECEIVERS`, `CLRALM`, and `CLRBITS`, plus EEPROM-backed settings and application-owned configuration that affects the Davis dashboard and settings pages.
- Excluded: archive record field-by-field detail from `DMP` / `DMPAFT`. Archive values are already represented by the archive UI and API models and should be documented separately if needed.
- Grounding: this is based on [VantageStation.cs](../src/HVO.Hardware.DavisVantagePro2/Station/VantageStation.cs), [Loop2Packet.cs](../src/HVO.Hardware.DavisVantagePro2/Protocol/Packets/Loop2Packet.cs), [StationModels.cs](../src/HVO.Hardware.DavisVantagePro2/Station/Models/StationModels.cs), [StationOptions.cs](../src/HVO.Hardware.DavisVantagePro2/Configuration/StationOptions.cs), [WeatherStationWorker.cs](../src/HVO.Hardware.DavisVantagePro2/Workers/WeatherStationWorker.cs), [Status.razor](../src/HVO.Hardware.DavisVantagePro2/Components/Pages/Status.razor), and [VantageSerialProtocolDocs_v261.pdf](./VantageSerialProtocolDocs_v261.pdf).

Application boundary note:
- The Website/Azure data model, the Weather/Davis data model, and the BMS data model should remain three separate application models and stores.
- Shared DTOs or sync/projection paths are acceptable where the website needs current values, settings snapshots, or history, but Weather and BMS should not be collapsed into the Website EF model.
- Any future Davis alarm/history/config tables discussed in this document belong to the Weather application boundary unless a separate website-facing projection table is intentionally introduced.

Editability legend:
- `Read-only`: readable from the console or application, but this repo does not expose a setter.
- `Editable`: this repo has a dedicated setter or action for it.
- `Indirectly editable`: the value itself is read-only telemetry or derived data, but it changes when a related console setting is changed.
- `Raw EEPROM only`: the value is readable through the repo, but there is no dedicated named setter even though raw EEPROM access could likely write it.
- `Application-only`: not stored on the console; owned by the Davis worker app, outbox, or UI layer.

Dashboard legend:
- `Shown on status dashboard`: currently surfaced on the main `/` dashboard.
- `Available but not shown`: available in repo models, worker payloads, or APIs, but not currently surfaced on the main dashboard.

## 1. Main Status Dashboard: LOOP Values Already Displayed

These are LOOP-backed values currently shown on the main status dashboard in [Status.razor](../src/HVO.Hardware.DavisVantagePro2/Components/Pages/Status.razor).

| Value | Source model | LOOP source | Dashboard status | Editable | Notes |
| --- | --- | --- | --- | --- | --- |
| Outside temperature | `Loop2Packet.OutsideTemperatureF` | LOOP2 | Shown on status dashboard | Read-only | Primary hero value. |
| Forecast text | `Loop2Packet.ForecastString` | LOOP1 | Shown on status dashboard | Read-only | Human-readable text derived from forecast rule. |
| Dew point | `Loop2Packet.DewPointF` | LOOP2 | Shown on status dashboard | Read-only | Displayed in hero and persisted in worker payload. |
| Heat index | `Loop2Packet.HeatIndexF` | LOOP2 | Shown on status dashboard | Read-only | Displayed in metric chips. |
| Wind chill | `Loop2Packet.WindChillF` | LOOP2 | Shown on status dashboard | Read-only | Displayed in metric chips. |
| Feels-like summary | `Loop2Packet.HeatIndexF` fallback | LOOP2 | Shown on status dashboard | Read-only | UI-computed presentation, not a distinct console field. |
| Barometric trend text | `Loop2Packet.BarometricTrend` | LOOP2 | Shown on status dashboard | Read-only | UI converts raw Davis trend to text. |
| Outside humidity | `Loop2Packet.OutsideHumidityPercent` | LOOP2 | Shown on status dashboard | Read-only | Displayed in metric chips. |
| Sunrise | `Loop2Packet.SunriseDisplay` | LOOP1 | Shown on status dashboard | Read-only | LOOP1 sunrise `HHMM` decoded to `HH:MM`. |
| Sunset | `Loop2Packet.SunsetDisplay` | LOOP1 | Shown on status dashboard | Read-only | LOOP1 sunset `HHMM` decoded to `HH:MM`. |
| Wind direction | `Loop2Packet.WindDirectionDegrees` | LOOP2 | Shown on status dashboard | Read-only | Compass and direction label. |
| Instant wind speed | `Loop2Packet.WindSpeedMph` | LOOP2 | Shown on status dashboard | Read-only | Wind summary. |
| 10-minute average wind speed | `Loop2Packet.WindSpeed10MinAvgMph` | LOOP2 | Shown on status dashboard | Read-only | Wind detail row. |
| 10-minute gust speed | `Loop2Packet.WindGust10MinMph` | LOOP2 | Shown on status dashboard | Read-only | Wind detail row. |
| 10-minute gust direction | `Loop2Packet.WindGust10MinDirectionDegrees` | LOOP2 | Shown on status dashboard | Read-only | Used indirectly by direction spread text. |
| Station-corrected barometric pressure | `Loop2Packet.BarometricPressureInHg` | LOOP2 | Shown on status dashboard | Indirectly editable | Changes when barometer calibration or altitude changes. |
| Raw station pressure | `Loop2Packet.PressureRawInHg` | LOOP2 | Shown on status dashboard | Read-only | Labeled as inside pressure on dashboard. |
| Rain rate | `Loop2Packet.RainRateInchesPerHour` | LOOP2 | Shown on status dashboard | Read-only | Weather and rain metrics section. |
| Daily rain | `Loop2Packet.DailyRainInches` | LOOP2 | Shown on status dashboard | Read-only | Weather and rain metrics section. |
| Storm rain | `Loop2Packet.StormRainInches` | LOOP2 | Shown on status dashboard | Read-only | Weather and rain metrics section. |
| Monthly rain | `Loop2Packet.MonthlyRainInches` | LOOP1 | Shown on status dashboard | Read-only | LOOP1-only extended rain total. |
| Yearly rain | `Loop2Packet.YearlyRainInches` | LOOP1 | Shown on status dashboard | Read-only | LOOP1-only extended rain total. |
| Daily ET | `Loop2Packet.DailyEtInches` | LOOP2 | Shown on status dashboard | Read-only | Weather and rain metrics section. |
| Solar radiation | `Loop2Packet.SolarRadiationWm2` | LOOP2 | Shown on status dashboard | Read-only | Current solar plus 24-hour chart. |
| UV index | `Loop2Packet.UvIndex` | LOOP2 | Shown on status dashboard | Read-only | Current UV value. |
| Inside temperature | `Loop2Packet.InsideTemperatureF` | LOOP2 | Shown on status dashboard | Read-only | Inside conditions card. |
| Inside humidity | `Loop2Packet.InsideHumidityPercent` | LOOP2 | Shown on status dashboard | Read-only | Inside conditions card. |
| Console battery voltage | `Loop2Packet.ConsoleBatteryVoltage` | LOOP1 | Shown on status dashboard | Read-only | Footer pill. |
| Transmitter battery status summary | `Loop2Packet.TransmitterLowBatteryChannels` / `TransmitterBatteryStatus` | LOOP1 | Shown on status dashboard | Read-only | UI shows a summary, not the raw bitmask value. |
| Archive interval | `VantageStation.ArchiveIntervalSeconds` | EEPROM-backed cached state | Shown on status dashboard | Editable | Console setting shown alongside live LOOP values. |
| Pending outbox queue count | `OutboxForwarder.PendingCount` | Application-only | Shown on status dashboard | Application-only | Not from the console. Included here because it shares dashboard space. |

## 2. LOOP Values Available But Not Currently Shown On The Main Dashboard

These values are already decoded by the repo and are available for future dashboard cards, diagnostics, details drawers, or advanced telemetry views.

| Value | Source model | LOOP source | Dashboard status | Editable | Notes |
| --- | --- | --- | --- | --- | --- |
| Altimeter pressure | `Loop2Packet.AltimeterInHg` | LOOP2 | Available but not shown | Read-only | Distinct from corrected and raw station pressure. |
| THSW index | `Loop2Packet.ThswF` | LOOP2 | Available but not shown | Read-only | Good candidate for advanced conditions or hot-weather detail. |
| 2-minute average wind speed | `Loop2Packet.WindSpeed2MinAvgMph` | LOOP2 | Available but not shown | Read-only | Useful if UI wants a shorter rolling average than 10-minute average. |
| Rain over last 15 minutes | `Loop2Packet.Rain15MinInches` | LOOP2 | Available but not shown | Read-only | Strong candidate for storm / alert cards. |
| Rain over last hour | `Loop2Packet.HourRainInches` | LOOP2 | Available but not shown | Read-only | Strong candidate for storm / alert cards. |
| Rain over last 24 hours | `Loop2Packet.Rain24HourInches` | LOOP2 | Available but not shown | Read-only | Not currently shown even though it is already decoded. |
| Storm start date | `Loop2Packet.StormStartDate` | LOOP2 | Available but not shown | Read-only | Good for storm context and event cards. |
| Monthly ET | `Loop2Packet.MonthlyEtInches` | LOOP1 | Available but not shown | Read-only | Extended ET total not currently surfaced. |
| Yearly ET | `Loop2Packet.YearlyEtInches` | LOOP1 | Available but not shown | Read-only | Extended ET total not currently surfaced. |
| Raw forecast icon bitmask | `Loop2Packet.ForecastIcons` | LOOP1 | Available but not shown | Read-only | Repo currently surfaces forecast text instead. |
| Raw forecast rule number | `Loop2Packet.ForecastRule` | LOOP1 | Available but not shown | Read-only | Repo currently surfaces forecast text instead. |
| Raw transmitter battery bitmask | `Loop2Packet.TransmitterBatteryStatus` | LOOP1 | Available but not shown | Read-only | Dashboard shows summary text, not the raw bitmask. |
| Extra temperatures 1-7 | `Loop2Packet.ExtraTemperaturesF` | LOOP1 | Available but not shown | Read-only | Present if configured sensors exist. |
| Soil temperatures 1-4 | `Loop2Packet.SoilTemperaturesF` | LOOP1 | Available but not shown | Read-only | Present if soil probes exist. |
| Extra humidities 1-7 | `Loop2Packet.ExtraHumiditiesPercent` | LOOP1 | Available but not shown | Read-only | Present if configured sensors exist. |
| Soil moisture 1-4 | `Loop2Packet.SoilMoisturesCb` | LOOP1 | Available but not shown | Read-only | Good candidate for garden / observatory grounds page. |
| Leaf wetness 1-4 | `Loop2Packet.LeafWetnessScaled` | LOOP1 | Available but not shown | Read-only | Good candidate for plant / condensation monitoring. |
| LOOP packet timestamp | `Loop2Packet.RecordedAtUtc` | LOOP1 / LOOP2 | Available but not shown | Read-only | Used indirectly in header timestamps and outbox persistence. |

Notes:
- The worker already persists more LOOP data than the status dashboard shows today. In particular, the live outbox payload includes THSW, altimeter pressure, 2-minute wind average, 15-minute rain, hourly rain, 24-hour rain, storm start date, monthly ET, yearly ET, raw forecast rule, and raw transmitter battery state.
- The lightweight current-conditions API in [CurrentConditionsResponse.cs](../src/HVO.Hardware.DavisVantagePro2/Api/CurrentConditionsResponse.cs) exposes a curated subset of these values, not the entire `Loop2Packet` surface.

## 3. Console Identity, Command Values, And EEPROM-Backed Settings

### 3.1 Station identity and console clock

Source commands:
- `WRD 12 4D`
- `NVER`
- `VER`
- `GETTIME`

| Value | Source in code | Dashboard status | Editable | Possible values / notes |
| --- | --- | --- | --- | --- |
| Hardware name | `StationInfo.HardwareName` | Available outside main dashboard | Read-only | Current mapping: `Vantage Pro`, `Vantage Pro 2`, `Vantage Vue`, or `Unknown`. |
| Hardware type | `StationInfo.HardwareType` | Available outside main dashboard | Read-only | Raw hardware byte. Current code recognizes `16 = Vantage Pro family`, `17 = Vantage Vue`. |
| Model type | `StationInfo.ModelType` | Available outside main dashboard | Read-only | Current mapping: `1 = Vantage Pro`, `2 = Vantage Pro 2`. |
| Firmware version | `StationInfo.FirmwareVersion` | Available outside main dashboard | Read-only | Free-form string returned by `NVER`. |
| Firmware date | `StationInfo.FirmwareDate` | Available outside main dashboard | Read-only | Free-form string returned by `VER`. |
| Console time | `StationInfo.ConsoleTime` / `GetConsoleTimeAsync()` | Available outside main dashboard | Editable | Read through `GETTIME`, written by `SetConsoleTimeAsync()`. |

### 3.2 EEPROM station settings

Source command:
- `EEBRD`

Returned by `GetStationSettingsAsync()`.

| Value | EEPROM / model | Dashboard status | Editable | Possible values / notes |
| --- | --- | --- | --- | --- |
| Archive interval | `EepromArchiveInterval` -> `StationSettings.ArchiveIntervalSeconds` | Shown on status dashboard | Editable | Setter allows only `1, 5, 10, 15, 30, 60, 120` minutes. |
| Latitude | `EepromLatitude` -> `StationSettings.LatitudeDegrees` | Available outside main dashboard | Editable | Stored as signed short in `0.1 deg` increments. |
| Longitude | `EepromLongitude` -> `StationSettings.LongitudeDegrees` | Available outside main dashboard | Editable | Stored as signed short in `0.1 deg` increments. |
| Altitude | `EepromAltitude` -> `StationSettings.AltitudeFeet` | Available outside main dashboard | Editable | Stored as signed short feet. Also affects corrected barometer values indirectly. |
| Rain year start month | `EepromRainYearStart` -> `StationSettings.RainYearStartMonth` | Available outside main dashboard | Editable | `1` through `12`. |
| Rain bucket type | `EepromSetupBits[5:4]` -> `StationSettings.RainBucketType` | Available outside main dashboard | Editable | `0 = 0.01 in`, `1 = 0.2 mm`, `2 = 0.1 mm`. |
| DST setting | `EepromManOrAuto` + `EepromDaylightSavings` -> `StationSettings.DstSetting` | Available outside main dashboard | Editable | `AUTO`, `ON`, `OFF`. |
| Timezone mode | `EepromGmtOrZone` -> `StationSettings.UseTimezoneCode` | Available outside main dashboard | Editable | `true = timezone code`, `false = GMT offset`. |
| Timezone code | `EepromTimezoneCode` -> `StationSettings.TimezoneCode` | Available outside main dashboard | Editable | Setter now validates `0` through `31`. |
| GMT offset | `EepromGmtOffset` -> `StationSettings.GmtOffsetHours` | Available outside main dashboard | Editable | Stored in hundredths of hours. |
| Temperature logging mode | `EepromTempLogging` -> `StationSettings.TemperatureLogging` | Available outside main dashboard | Editable | `AVERAGE` or `LAST`. |
| Barometer display units | `EepromUnitBits[1:0]` -> `StationSettings.BarometerUnits` | Available outside main dashboard | Raw EEPROM only | `inHg`, `mmHg`, `hPa`, `mbar`. |
| Temperature display units | `EepromUnitBits[3:2]` -> `StationSettings.TemperatureUnits` | Available outside main dashboard | Raw EEPROM only | `F`, `F x10`, `C`, `C x10`. |
| Rain display units | `EepromUnitBits[5]` -> `StationSettings.RainUnits` | Available outside main dashboard | Raw EEPROM only | `inch` or `mm`. |
| Wind display units | `EepromUnitBits[7:6]` -> `StationSettings.WindUnits` | Available outside main dashboard | Raw EEPROM only | `mph`, `m/s`, `km/h`, `knots`. |

Notes:
- `UseTimezoneCode` and `GmtOffsetHours` are a mutually exclusive pair; the console uses one mode or the other.
- The repo reads the display unit bits today but does not expose dedicated setters for them yet.

### 3.3 Transmitter configuration and heard-transmitter state

Source commands:
- `EEBRD`
- `RECEIVERS`

| Value | Model / source | Dashboard status | Editable | Possible values / notes |
| --- | --- | --- | --- | --- |
| Channel | `TransmitterConfig.Channel` | Available outside main dashboard | Read-only | `1` through `8`. |
| Transmitter type | `TransmitterConfig.TransmitterType` | Available outside main dashboard | Editable | `iss`, `temp`, `hum`, `temp_hum`, `wind`, `rain`, `leaf`, `soil`, `leaf_soil`, `sensorlink`, `none`. |
| Repeater ID | `TransmitterConfig.RepeaterId` | Available outside main dashboard | Editable | `null` or `A` through `H`. |
| Active flag | `TransmitterConfig.IsActive` | Available outside main dashboard | Editable | Derived from configured receive mask. |
| Retransmit flag | `TransmitterConfig.IsRetransmitting` | Available outside main dashboard | Editable | Controlled by `SetRetransmitAsync()`. |
| Extra temperature sensor ID | `TransmitterConfig.ExtraTemperatureId` | Available outside main dashboard | Editable | Current setter validates `1` through `7` when supplied. |
| Extra humidity sensor ID | `TransmitterConfig.ExtraHumidityId` | Available outside main dashboard | Editable | Current setter validates `1` through `7` when supplied. |
| Heard transmitter IDs | `GetHeardTransmitterIdsAsync()` | Available outside main dashboard | Read-only | Derived from `RECEIVERS` bitmap; reflects what the console can hear now, not just configured channels. |

### 3.4 Calibration values

Source command:
- `EEBRD`

Returned by `GetCalibrationAsync()`.

| Value | Model | Dashboard status | Editable | Possible values / notes |
| --- | --- | --- | --- | --- |
| Inside temperature offset | `CalibrationData.InsideTempOffsetF` | Available outside main dashboard | Editable | `-12.8` to `+12.7 F` in `0.1 F` steps. Variable name `inTemp`. |
| Outside temperature offset | `CalibrationData.OutsideTempOffsetF` | Available outside main dashboard | Editable | Variable name `outTemp`. |
| Extra temperature offsets 1-7 | `CalibrationData.ExtraTempOffsets` | Available outside main dashboard | Editable | Variable names `extraTemp1` through `extraTemp7`. |
| Soil temperature offsets 1-4 | `CalibrationData.SoilTempOffsets` | Available outside main dashboard | Editable | Variable names `soilTemp1` through `soilTemp4`. |
| Leaf temperature offsets 1-4 | `CalibrationData.LeafTempOffsets` | Available outside main dashboard | Editable | Variable names `leafTemp1` through `leafTemp4`. |
| Inside humidity offset | `CalibrationData.InsideHumidOffsetPct` | Available outside main dashboard | Editable | Variable name `inHumid`. |
| Outside humidity offset | `CalibrationData.OutsideHumidOffsetPct` | Available outside main dashboard | Editable | Variable name `outHumid`. |
| Extra humidity offsets 1-7 | `CalibrationData.ExtraHumidOffsets` | Available outside main dashboard | Editable | Variable names `extraHumid1` through `extraHumid7`. |
| Wind direction offset | `CalibrationData.WindDirOffsetDegrees` | Available outside main dashboard | Editable | `-359` to `+359` degrees. |

Notes:
- Invalid calibration variable names now fail fast with `ArgumentException` before any console I/O.

### 3.5 Barometer service data

Source command:
- `BARDATA`

Returned by `GetBarometerDataAsync()`.

| Value | Model | Dashboard status | Editable | Possible values / notes |
| --- | --- | --- | --- | --- |
| Current pressure | `BarometerData.CurrentPressureInHg` | Available outside main dashboard | Indirectly editable | Changes when barometer calibration is updated through `SetBarometerAsync()`. |
| Altitude | `BarometerData.AltitudeFeet` | Available outside main dashboard | Indirectly editable | Also writable through `SetBarometerAsync()` and `SetAltitudeAsync()`. |
| Dew point | `BarometerData.DewPointF` | Available outside main dashboard | Read-only | Diagnostic / service value from `BARDATA`. |
| Virtual temperature | `BarometerData.VirtualTemperatureF` | Available outside main dashboard | Read-only | Diagnostic / service value from `BARDATA`. |
| Correction factor | `BarometerData.CorrectionFactor` | Available outside main dashboard | Read-only | Diagnostic / service value from `BARDATA`. |
| Correction ratio | `BarometerData.CorrectionRatio` | Available outside main dashboard | Read-only | Diagnostic / service value from `BARDATA`. |
| Correction constant | `BarometerData.CorrectionConstantInHg` | Available outside main dashboard | Read-only | Diagnostic / service value from `BARDATA`. |
| Gain | `BarometerData.Gain` | Available outside main dashboard | Read-only | Diagnostic / service value from `BARDATA`. |
| Error offset | `BarometerData.ErrorOffset` | Available outside main dashboard | Read-only | Diagnostic / service value from `BARDATA`. |

### 3.6 Reception statistics

Source command:
- `RXCHECK`

Returned by `GetReceptionStatsAsync()`.

| Value | Model | Dashboard status | Editable | Possible values / notes |
| --- | --- | --- | --- | --- |
| Total packets received | `ReceptionStats.TotalPacketsReceived` | Available outside main dashboard | Read-only | Non-negative integer. |
| Total packets missed | `ReceptionStats.TotalPacketsMissed` | Available outside main dashboard | Read-only | Non-negative integer. |
| Number of resynchronizations | `ReceptionStats.NumberOfResynchronizations` | Available outside main dashboard | Read-only | Non-negative integer. |
| Longest good stretch | `ReceptionStats.LongestGoodStretch` | Available outside main dashboard | Read-only | Non-negative integer. |
| Number of CRC errors | `ReceptionStats.NumberOfCrcErrors` | Available outside main dashboard | Read-only | Non-negative integer. |
| Reception percent | `ReceptionStats.ReceptionPercent` | Available outside main dashboard | Read-only | Derived percentage. |

### 3.7 Alarm thresholds and alarm actions

Source commands / storage:
- EEPROM alarm threshold block starting at `0x52`
- `CLRALM`
- `CLRBITS`

| Value group | Model / command | Dashboard status | Editable | Notes |
| --- | --- | --- | --- | --- |
| Rising and falling bar trend alarm thresholds | `AlarmThresholds.RisingBarTrendInHg`, `FallingBarTrendInHg` | Available outside main dashboard | Editable | EEPROM-backed alarm values. |
| Time alarm | `AlarmThresholds.TimeAlarm` | Available outside main dashboard | Editable | Console-side time alarm. |
| Temperature alarm thresholds | `AlarmThresholds.LowInsideTemperatureF`, `HighInsideTemperatureF`, `LowOutsideTemperatureF`, `HighOutsideTemperatureF`, extra / soil / leaf arrays | Available outside main dashboard | Editable | Full EEPROM-backed threshold block is implemented. |
| Humidity alarm thresholds | Inside / outside / extra humidity properties | Available outside main dashboard | Editable | EEPROM-backed threshold block. |
| Dew point, wind chill, heat index, THSW alarm thresholds | `AlarmThresholds.LowDewPointF`, `HighDewPointF`, `LowWindChillF`, `HighHeatIndexF`, `HighThswF` | Available outside main dashboard | Editable | EEPROM-backed threshold block. |
| Wind alarm thresholds | `AlarmThresholds.WindSpeedMph`, `WindSpeed10MinuteMph` | Available outside main dashboard | Editable | EEPROM-backed threshold block. |
| UV and solar alarm thresholds | `AlarmThresholds.UvIndex`, `UvDoseMeds`, `SolarRadiationWm2` | Available outside main dashboard | Editable | EEPROM-backed threshold block. |
| Rain and ET alarm thresholds | `RainRateInchesPerHour`, `Rain15MinuteInches`, `Rain24HourInches`, `RainStormInches`, `DailyEtInches` | Available outside main dashboard | Editable | EEPROM-backed threshold block. |
| Soil moisture and leaf wetness alarm thresholds | Soil / leaf arrays | Available outside main dashboard | Editable | EEPROM-backed threshold block. |
| Clear all configured alarm thresholds | `ClearAlarmThresholdsAsync()` via `CLRALM` | Available outside main dashboard | Editable | Action, not a value. |
| Clear currently active alarm bits | `ClearActiveAlarmBitsAsync()` via `CLRBITS` | Available outside main dashboard | Editable | Action, not a value. |

### 3.8 Console actions that are write-only today

These actions exist in the repo but do not currently have a dedicated read-back model.

| Capability | Write path | Dashboard status | Notes |
| --- | --- | --- | --- |
| Console lamp on/off | `SetLampAsync(bool)` via `LAMPS` | Available outside main dashboard | Write-only action today. |
| Archive clear | `ClearArchiveAsync()` via `CLRLOG` | Available outside main dashboard | Destructive action, not a setting. |

## 4. Application-Owned Settings And Runtime Values

These values are not stored on the Davis console, but they matter for configuration pages and should be organized separately from console settings.

### 4.1 Station worker configuration (`StationOptions`)

Source:
- [StationOptions.cs](../src/HVO.Hardware.DavisVantagePro2/Configuration/StationOptions.cs)

| Value | Source in code | Editable | Notes |
| --- | --- | --- | --- |
| Station host | `StationOptions.Host` | Application-only | IP or hostname of the WeatherLink IP adapter / console endpoint. |
| Station TCP port | `StationOptions.Port` | Application-only | Defaults to `22222`. |
| Socket timeout | `StationOptions.SocketTimeoutSeconds` | Application-only | Controls network read/write timeout behavior. |
| Archive catchup mode | `StationOptions.ArchiveCatchupMode` | Application-only | `Disabled`, `Enabled`, or `Force`. |
| Archive catchup lookback hours | `StationOptions.ArchiveCatchupLookbackHours` | Application-only | Used when mode is `Enabled`. |
| Max consecutive errors | `StationOptions.MaxConsecutiveErrors` | Application-only | Worker health / reconnect behavior. |
| Station ID sent to downstream API | `StationOptions.StationId` | Application-only | Included with outbox payloads. |

### 4.2 Outbox forwarder configuration (`OutboxOptions`)

Source:
- [StationOptions.cs](../src/HVO.Hardware.DavisVantagePro2/Configuration/StationOptions.cs)

| Value | Source in code | Editable | Notes |
| --- | --- | --- | --- |
| API endpoint | `OutboxOptions.ApiEndpoint` | Application-only | Destination for forwarded weather records. |
| API key | `OutboxOptions.ApiKey` | Application-only | Security-sensitive application config. Not a console value. |
| Max retry attempts | `OutboxOptions.MaxRetryAttempts` | Application-only | Outbox retry policy. |
| Max backoff seconds | `OutboxOptions.MaxBackoffSeconds` | Application-only | Retry throttling cap. |
| Sweep interval seconds | `OutboxOptions.SweepIntervalSeconds` | Application-only | How often the forwarder checks the outbox queue. |
| Batch size | `OutboxOptions.BatchSize` | Application-only | Number of records POSTed per batch. |
| SQLite DB path | `OutboxOptions.DbPath` | Application-only | Local persistence location for outbox and settings snapshots. |

### 4.3 Cached settings snapshot and archive catchup status

Sources:
- [StationSettingsSnapshotStore.cs](../src/HVO.Hardware.DavisVantagePro2/Outbox/StationSettingsSnapshotStore.cs)
- [WeatherStationWorker.cs](../src/HVO.Hardware.DavisVantagePro2/Workers/WeatherStationWorker.cs)
- [Settings.razor.cs](../src/HVO.Hardware.DavisVantagePro2/Components/Pages/Settings.razor.cs)

| Value | Source in code | Editable | Notes |
| --- | --- | --- | --- |
| Cached station settings snapshot | `StoredStationSettingsSnapshot.Settings` | Application-only | Local copy of last refreshed EEPROM-backed settings. |
| Snapshot saved timestamp | `StoredStationSettingsSnapshot.SavedAtUtc` | Application-only | Useful for UI freshness indicators. |
| Startup archive catchup mode | `ArchiveCatchupStatus.StartupMode` | Application-only | Mirrors configured startup behavior. |
| Archive catchup lookback hours | `ArchiveCatchupStatus.LookbackHours` | Application-only | Exposed for settings/status UI. |
| Latest persisted archive record UTC | `ArchiveCatchupStatus.LatestPersistedAtUtc` | Application-only | Outbox / archive health. |
| Latest persisted archive record local time | `ArchiveCatchupStatus.LatestPersistedAtLocal` | Application-only | UI-friendly local display. |
| Archive sync lag | `ArchiveCatchupStatus.SyncLag` | Application-only | Derived operational health metric. |
| Should run on startup | `ArchiveCatchupStatus.ShouldRunOnStartup` | Application-only | Derived from mode and lag. |
| Manual archive top-off action | `RunArchiveTopOffAsync()` | Application-only | Worker-driven maintenance action. |

### 4.4 Operational runtime values surfaced in the dashboard or API

| Value | Source in code | Editable | Notes |
| --- | --- | --- | --- |
| Connection status | `VantageStation.IsConnected` / worker state | Application-only | Dashboard footer and current-conditions API. |
| Pending outbox count | `OutboxForwarder.PendingCount` | Application-only | Dashboard footer and API response. |
| Failed outbox count | `OutboxForwarder.FailedCount` | Application-only | API response and operational diagnostics. |
| Last reading timestamp | `WeatherStationWorker.LastReadingAt` | Application-only | Used for dashboard timestamps. |
| Consecutive worker errors | `WeatherStationWorker.ConsecutiveErrors` | Application-only | Operational health value. |
| Last worker error text | `WeatherStationWorker.LastError` | Application-only | Operational diagnostics. |

## 5. Recommended UI Organization

If the goal is to organize pages around how data is sourced and saved, the repo naturally breaks down like this:

| UI group | Values | Source |
| --- | --- | --- |
| Main status dashboard | High-signal LOOP values already shown today | LOOP1 / LOOP2 plus a few application runtime values |
| Advanced live conditions | LOOP values we already decode but do not currently show: altimeter, THSW, 2-minute wind average, 15-minute / hourly / 24-hour rain, storm start, monthly / yearly ET, extra sensors | LOOP1 / LOOP2 |
| Station identity and diagnostics | Hardware type, model, firmware, console time, reception stats, heard transmitters | Command responses |
| Console setup | Latitude, longitude, altitude, rain year start, rain bucket type, DST mode, timezone code / GMT offset, temperature logging, unit bits | EEPROM-backed settings |
| Calibration | Barometer calibration, temperature offsets, humidity offsets, wind direction offset | EEPROM-backed settings + `BARDATA` |
| Transmitters | Channel type, repeater, extra sensor mapping, retransmit channel, active state | EEPROM-backed settings + `RECEIVERS` |
| Console alarm thresholds | All EEPROM-backed alarm thresholds plus clear actions | Alarm EEPROM block + commands |
| Archive and rain maintenance | Archive interval, archive clear, manual archive top-off, archive lag / sync status | EEPROM + application worker state |
| Application integration | Station host/port, API endpoint/key, retry policy, DB path, queue behavior | Application config only |

## 6. Current UI Coverage Notes

Current Blazor pages in the Davis app cover most of the console-oriented read/write surface, but not all of it.

Pages already present:
- `Status`: live dashboard for a curated subset of LOOP values plus connection/outbox state.
- `StationInfo`: hardware, firmware, and console time.
- `Settings`: cached station settings snapshot and archive catchup status.
- `RainSettings`: rain bucket type and rain year start.
- `Archive`: archive interval and archive controls.
- `Barometer`: barometer service data and calibration write path.
- `Calibration`: sensor offset editing.
- `Transmitters`: transmitter configuration editing.
- `Reception`: reception statistics.
- `Clock`: console time sync.

Not fully surfaced today:
- Advanced LOOP values such as altimeter pressure, THSW, 2-minute wind average, 15-minute rain, hourly rain, 24-hour rain, storm start date, monthly ET, yearly ET, and extra sensor arrays.
- Heard-transmitter state from `RECEIVERS`.
- Alarm threshold editing and alarm clear actions.
- Raw display-unit settings from `EepromUnitBits`.
- Application-owned worker and outbox configuration (`StationOptions` and `OutboxOptions`).

## 7. ASCII Page Layout Diagrams

These diagrams are planning wireframes, not final visual design. The goal is to separate:
- static non-editable, non-LOOP console data
- editable console data grouped by command and functionality
- application-specific settings
- alarms and alarm history
- archive browsing

### 7.1 Proposed navigation map

```text
+--------------------------------------------------------------------------------+
| Davis Settings Area                                                            |
+--------------------------------------------------------------------------------+
| Sidebar                                                                        |
|                                                                                |
|  - Status Dashboard                                                            |
|  - Station Info                                                                |
|  - Console Settings                                                            |
|  - Calibration                                                                 |
|  - Transmitters                                                                |
|  - Reception                                                                   |
|  - Alarms                                                                      |
|  - Archive                                                                     |
|  - Application Settings                                                        |
+--------------------------------------------------------------------------------+
```

### 7.2 Static non-editable, non-LOOP data page

Purpose:
- show console identity, firmware, clock, reception, and other non-LOOP read-only data in one place
- avoid mixing read-only diagnostics with setters

```text
+--------------------------------------------------------------------------------+
| Page: Station Info / Diagnostics                                               |
+--------------------------------------------------------------------------------+
| Header: Station name | last refresh | Refresh button                           |
+--------------------------------------------------------------------------------+
| Identity Card                     | Console Clock Card                         |
|-----------------------------------|--------------------------------------------|
| Hardware name                     | Console time                              |
| Hardware type                     | System time                               |
| Model type                        | Drift                                      |
| Firmware version                  | Time zone label                            |
| Firmware date                     | Sync action link to Clock page             |
+-----------------------------------+--------------------------------------------+
| Reception Card                    | Heard Transmitters Card                    |
|-----------------------------------|--------------------------------------------|
| Packets received                  | Heard channel list                         |
| Packets missed                    | Last refreshed                             |
| Resync count                      | Notes on configured vs heard               |
| CRC errors                        |                                            |
| Reception percent                 |                                            |
+-----------------------------------+--------------------------------------------+
| Barometer Service Data Card                                                    |
|-------------------------------------------------------------------------------|
| Current pressure | altitude | dew point | virtual temp                        |
| correction factor | ratio | constant | gain | error offset                    |
| [Open Barometer Calibration Page]                                              |
+--------------------------------------------------------------------------------+
```

Notes:
- This page is intentionally read-heavy and action-light.
- It should link out to edit pages instead of embedding many forms.

### 7.3 Editable console data page organized by command and functionality

Purpose:
- group writable console settings by the command or EEPROM save boundary that owns them
- keep related controls together so save actions match how the station is actually written

```text
+--------------------------------------------------------------------------------+
| Page: Console Settings                                                         |
+--------------------------------------------------------------------------------+
| Header: Save status | last console snapshot | Refresh from console             |
+--------------------------------------------------------------------------------+
| Left rail: section links                                                       |
|                                                                                |
|  - Clock                                                                       |
|  - EEPROM Settings                                                             |
|  - Rain and Archive                                                            |
|  - Barometer                                                                   |
|  - Calibration                                                                 |
|  - Transmitters                                                                |
|  - Console Actions                                                             |
+--------------------------------------------------------------------------------+
| Main workspace                                                                 |
|                                                                                |
|  [Clock / GETTIME / SETTIME]                                                   |
|  ---------------------------------------------------------------------------   |
|  Console time | host time | drift | [Sync now]                                |
|                                                                                |
|  [EEPROM Settings / EEBRD + EEBWR + NEWSETUP]                                 |
|  ---------------------------------------------------------------------------   |
|  Latitude | Longitude | Altitude                                               |
|  DST mode | Timezone mode | Timezone code or GMT offset                        |
|  Temperature logging                                                           |
|  Display units (read-only for now, future editable if exposed)                 |
|  [Save location/timezone settings]                                             |
|                                                                                |
|  [Rain and Archive / EEBWR + SETPER + CLRLOG]                                 |
|  ---------------------------------------------------------------------------   |
|  Archive interval | Rain bucket type | Rain year start month                   |
|  [Save rain/archive settings]   [Clear archive]                                |
|                                                                                |
|  [Barometer / BARDATA + BAR=]                                                  |
|  ---------------------------------------------------------------------------   |
|  Current service values on top                                                 |
|  Editable reference pressure | altitude                                        |
|  [Write barometer calibration]                                                 |
|                                                                                |
|  [Calibration / EEBWR]                                                         |
|  ---------------------------------------------------------------------------   |
|  Temperature offsets                                                           |
|  Humidity offsets                                                              |
|  Wind direction offset                                                         |
|  [Save calibration changes]                                                    |
|                                                                                |
|  [Transmitters / EEBWR + RECEIVERS + NEWSETUP]                                |
|  ---------------------------------------------------------------------------   |
|  Channel grid: type | repeater | active | retransmit | sensor ids             |
|  Heard transmitters summary                                                    |
|  [Save transmitter changes]                                                    |
|                                                                                |
|  [Console Actions]                                                             |
|  ---------------------------------------------------------------------------   |
|  Lamp on/off | Clear active alarm bits                                         |
|  Destructive actions visually separated                                        |
+--------------------------------------------------------------------------------+
```

Notes:
- Each block should have its own save button and dirty-state indicator.
- `NEWSETUP`-dependent settings should be visually grouped so the operator understands they apply together.

### 7.4 Application-specific settings page

Purpose:
- keep app-owned worker, outbox, persistence, and integration configuration separate from console settings
- make it clear these values do not live on the Davis console

```text
+--------------------------------------------------------------------------------+
| Page: Application Settings                                                     |
+--------------------------------------------------------------------------------+
| Header: environment name | config source | restart required badges             |
+--------------------------------------------------------------------------------+
| Station Worker Card                 | Archive Catch-up Card                    |
|-------------------------------------|-----------------------------------------|
| Host                                | Startup mode                            |
| Port                                | Lookback hours                          |
| Socket timeout                      | Latest persisted archive timestamp      |
| Max consecutive errors              | Sync lag                                |
| Station ID                          | [Run top-off now]                       |
| [Save worker settings]              |                                         |
+-------------------------------------+-----------------------------------------+
| Outbox / API Forwarder Card                                                     |
|-------------------------------------------------------------------------------|
| API endpoint                                                                   |
| API key                                                                        |
| Batch size                                                                     |
| Sweep interval                                                                 |
| Max retry attempts                                                             |
| Max backoff seconds                                                            |
| SQLite DB path                                                                 |
| Pending count | failed count                                                   |
| [Save outbox settings]                                                         |
+--------------------------------------------------------------------------------+
| Local Cache / Snapshot Card                                                    |
|-------------------------------------------------------------------------------|
| Last station snapshot saved at                                                 |
| Snapshot freshness                                                             |
| [Refresh cached settings]                                                      |
+--------------------------------------------------------------------------------+
```

Notes:
- This page should use app/config terminology instead of console terminology.
- Secrets such as API keys need masked display and explicit edit flow.

### 7.5 Alarms page

Purpose:
- keep alarm threshold editing separate from the main console settings page
- show both current configured thresholds and recent alarm activity

Design direction:
- split alarm management into separate surfaces for `Definitions`, `Destinations`, and `History`
- do not overload one page with threshold editing, delivery routing, and event review all at once
- treat console-native alarms and application-owned alarms as related but distinct concepts

Suggested alarm navigation:

```text
Alarms
	- Definitions
	- Destinations
	- History
	- Active Now
```

#### 7.5.1 Alarm definitions page

Purpose:
- create and edit alarm rules
- separate `what triggers` an alarm from `who receives it` and `what happened historically`

```text
+--------------------------------------------------------------------------------+
| Page: Alarms / Definitions                                                     |
+--------------------------------------------------------------------------------+
| Header: Refresh | Save definitions | Create rule                               |
+--------------------------------------------------------------------------------+
| Left column: rule list                                                         |
|                                                                                |
|  [Console thresholds]                                                          |
|  [System alarms]                                                               |
|  [Software-derived alarms]                                                     |
+--------------------------------------------------------------------------------+
| Rule editor                                                                    |
|                                                                                |
|  Rule type         [ Console threshold |v ]                                    |
|  Source group      [ Wind |v ]                                                 |
|  Source field      [ Wind speed mph |v ]                                       |
|  Trigger mode      [ Above threshold |v ]                                      |
|  Threshold         [ 32 ] [mph]                                                |
|  Clear behavior    [ Auto clear when normal |v ]                               |
|  Severity          [ Warning | Critical | Info ]                               |
|  Enabled           [ toggle ]                                                  |
|  Destinations      [ Email Ops, SMS Night, Webhook Discord ]                  |
|                                                                                |
|  ---------------------------------------------------------------------------   |
|  Console-native threshold editor                                               |
|  Tabs: [Barometer] [Temperature] [Humidity] [Wind] [Rain/ET] [Solar/UV]       |
|        [Soil/Leaf]                                                             |
|  Current console threshold values                                              |
|  [Save threshold block] [Clear thresholds] [Clear active bits]                 |
+--------------------------------------------------------------------------------+
| Rule examples                                                                  |
|--------------------------------------------------------------------------------|
|  - Console threshold: Wind speed above 32 mph                                 |
|  - Console threshold: Rain 15-minute total above 0.15 in                      |
|  - System alarm: No packets received for 120 seconds                          |
|  - Software alarm: Pending outbox count above 500                             |
|  - Software alarm: Archive sync lag above 6 hours                             |
+--------------------------------------------------------------------------------+
```

#### 7.5.2 Alarm destination page

Purpose:
- manage reusable notification receivers and routing endpoints
- let multiple alarms reuse the same delivery targets

```text
+--------------------------------------------------------------------------------+
| Page: Alarms / Destinations                                                    |
+--------------------------------------------------------------------------------+
| Header: Create destination | Test delivery | Save changes                      |
+--------------------------------------------------------------------------------+
| Destination grid                                                               |
|--------------------------------------------------------------------------------|
| Name           | Type     | Address/Target             | Enabled | Last test    |
|----------------+----------+----------------------------+---------+--------------|
| Email Ops      | Email    | ops@example.com            | Yes     | Success       |
| SMS Night      | SMS      | +1-555-...                 | Yes     | Not tested    |
| Discord Hook   | Webhook  | https://...                | Yes     | Success       |
| Console Lamp   | Console  | Lamp toggle                | Yes     | N/A           |
| Console Sound? | Console  | Unsupported / unknown      | No      | N/A           |
+--------------------------------------------------------------------------------+
| Destination editor                                                             |
|-------------------------------------------------------------------------------|
| Type [ Email | SMS | Webhook | Console action ]                               |
| Display name [........................]                                        |
| Address / endpoint [......................................................]    |
| Credential or secret ref [...............................................]     |
| Enabled [toggle]                                                              |
| Quiet hours / escalation rules [optional]                                     |
| [Save destination] [Send test]                                                |
+--------------------------------------------------------------------------------+
```

Suggested destination types:
- `Email`
- `SMS/Text`
- `Webhook`
- `In-app UI notification`
- `Console lamp toggle`
- `Future console audible alarm`, only if protocol support is later verified

#### 7.5.3 Alarm history page

Purpose:
- keep the alarm activations grid separate from rule editing
- optimize this page for filtering, triage, and operator review

```text
+--------------------------------------------------------------------------------+
| Page: Alarms / History                                                         |
+--------------------------------------------------------------------------------+
| Header: date range | state filter | severity filter | source filter | export   |
+--------------------------------------------------------------------------------+
| Alarm activation history grid                                                  |
|--------------------------------------------------------------------------------|
| Time UTC       | Alarm          | Source         | Severity | State    | Route  |
|----------------+----------------+----------------+----------+----------+--------|
| 2026-05-08 ... | High wind      | ISS            | Critical | Open     | Email  |
| 2026-05-07 ... | No packets     | Station worker | Warning  | Cleared  | SMS    |
| 2026-05-07 ... | Outbox backlog | Forwarder      | Warning  | Ack      | Hook   |
| 2026-05-07 ... | Rain 15 min    | Console        | Warning  | Cleared  | Email  |
+--------------------------------------------------------------------------------+
| Detail drawer                                                                  |
|-------------------------------------------------------------------------------|
| Activation time | cleared time | acknowledged time                            |
| Rule definition snapshot                                                       |
| Observed value and threshold at activation                                     |
| Delivery attempts and statuses                                                 |
| Related LOOP/worker snapshot                                                   |
+--------------------------------------------------------------------------------+
```

#### 7.5.4 Active alarms page or panel

Purpose:
- give operators a focused “what is open right now?” view without paging through history

```text
+--------------------------------------------------------------------------------+
| Page: Alarms / Active Now                                                      |
+--------------------------------------------------------------------------------+
| Open alarms list                                                               |
|--------------------------------------------------------------------------------|
| Alarm          | Source         | Open since      | Current value | Actions     |
|----------------+----------------+-----------------+---------------+-------------|
| High wind      | ISS            | 2026-05-08 ...  | 37 mph        | Ack / View  |
| No packets     | Station worker | 2026-05-08 ...  | 145 sec       | Ack / Retry |
+--------------------------------------------------------------------------------+
```

#### 7.5.5 Alarm source model

Suggested source categories:
- `Console threshold alarms`: backed by Davis EEPROM threshold block and active alarm bits
- `System alarms`: application/runtime health conditions such as no packets received, connection down, archive sync stalled, outbox failures, repeated worker exceptions
- `Software-derived alarms`: app-calculated conditions based on telemetry or trends, such as rainfall accumulation over custom windows or “wind gust persisted above X for Y minutes”

Examples:
- Console threshold: `Wind speed above 32 mph`
- Console threshold: `Outside temperature below 34 F`
- System alarm: `No packets received in 120 seconds`
- System alarm: `Consecutive errors above 5`
- Software-derived alarm: `Pending outbox count above 500 for 10 minutes`
- Software-derived alarm: `Archive lag above 6 hours`

#### 7.5.6 Delivery/action model

Separate `trigger` from `delivery`.

Suggested flow:

```text
Alarm Definition
		-> matches incoming condition
		-> opens/updates Alarm Event
		-> routes to one or more Alarm Destinations
		-> stores Delivery Attempts
```

Suggested delivery behaviors:
- notify once on activation
- remind while still active at a configured interval
- optionally notify again on clear
- support severity-based routing, for example critical alarms also go to SMS

#### 7.5.7 Davis console capability note

Current known Davis alarm-related capabilities in this repo:
- read and write the console alarm threshold block
- clear configured thresholds with `CLRALM`
- clear active alarm bits with `CLRBITS`
- toggle the lamp with `LAMPS`

Current unknown or unverified capability:
- whether the Davis console audible alarm can be triggered programmatically by protocol command

Design implication:
- model `Console audible alarm` as `unsupported / unknown` unless protocol documentation or device testing proves a command exists
- do not block the alarm architecture on that feature; treat it as an optional future destination type
- `Clear active bits` is supported, but it is not the same as “play alarm now”

#### 7.5.8 Suggested data model for Davis alarms

The current repo exposes alarm thresholds and clear actions, but not a persisted Davis alarm history. A deeper design should separate definitions, events, and destinations.

Boundary note:
- These Davis alarm entities should live in the Weather application/data model, not the Website/Azure EF model.
- If the website needs alarm history or active alarm summaries, it should receive that through shared DTOs, API reads, or explicit projection/sync tables rather than by merging the Weather and Website application models.

Suggested entities:

```text
AlarmDefinition
---------------
Id
Name
SourceType                // ConsoleThreshold | System | SoftwareDerived
SourceGroup               // Wind | Rain | Worker | Outbox | Archive | etc.
SourceField               // WindSpeedMph | NoPacketsDuration | PendingOutboxCount
ConditionType             // Above | Below | Equals | DurationExceeded | StateChange
ThresholdNumber
ThresholdText
DurationSeconds
Severity
Enabled
AutoClear
NotifyOnActivate
NotifyOnClear
ReminderIntervalSeconds
CreatedAtUtc
UpdatedAtUtc

AlarmDestination
----------------
Id
Name
DestinationType           // Email | SMS | Webhook | InApp | ConsoleLamp
Target
SecretReference
Enabled
LastTestedAtUtc
LastTestStatus

AlarmDefinitionDestination
--------------------------
AlarmDefinitionId
AlarmDestinationId

AlarmEvent
----------
Id
AlarmDefinitionId
TriggeredAtUtc
ClearedAtUtc
AcknowledgedAtUtc
LastObservedAtUtc
SourceType
SourceGroup
SourceField
AlarmNameSnapshot
SourceChannelOrSensor
ThresholdDisplaySnapshot
ObservedValueDisplay
SeveritySnapshot
State                     // Open | Cleared | Acknowledged
RelatedLoopTimestampUtc
RelatedArchiveTimestampUtc
Notes

AlarmDeliveryAttempt
--------------------
Id
AlarmEventId
AlarmDestinationId
AttemptedAtUtc
DeliveryState             // Pending | Sent | Failed
DeliveryReason            // Activated | Reminder | Cleared
ProviderMessage
```

Implementation note:
- the existing BMS pattern already shows how this repo handles open/clear lifecycle rows in the V9 database
- Davis alarms should likely use the same lifecycle idea, but with richer definition and delivery tables because Davis needs configurable routes and mixed trigger types

#### 7.5.9 Evaluation service split

Recommended split:
- `Console threshold sync`: reads/writes Davis threshold configuration
- `Alarm evaluation service`: evaluates software/system alarms and optionally watches Davis active alarm bits
- `Alarm delivery service`: sends email, SMS, webhook, or in-app notifications
- `Alarm history repository`: stores open/clear/ack and delivery attempts

This keeps protocol I/O, business rules, and notification transport from collapsing into one component.

### 7.6 Archive page

Purpose:
- browse archive records as a pageable, filterable grid
- keep this focused on historical data retrieval rather than station configuration

```text
+--------------------------------------------------------------------------------+
| Page: Archive                                                                  |
+--------------------------------------------------------------------------------+
| Header: date range | station id | export | refresh                             |
+--------------------------------------------------------------------------------+
| Filter bar                                                                      |
|                                                                                |
|  Start date   [ ............ ]   End date [ ............ ]                     |
|  Page size    [ 100 v ]          Sort     [ newest first v ]                   |
|  Search / jump to timestamp [ ................................. ] [Apply]      |
+--------------------------------------------------------------------------------+
| Archive grid                                                                    |
|--------------------------------------------------------------------------------|
| Timestamp local | Outside F | Humidity | Wind mph | Gust | Rain | Pressure | ET|
|----------------+-----------+----------+----------+------+------|----------|---|
| 2026-05-08 ... | 72.4      | 34       | 12       | 18   | 0.00 | 29.87    |...|
| 2026-05-08 ... | 72.1      | 35       | 11       | 17   | 0.00 | 29.87    |...|
| 2026-05-08 ... | 71.8      | 35       | 10       | 16   | 0.00 | 29.88    |...|
| ...                                                                            |
+--------------------------------------------------------------------------------+
| Footer                                                                          |
|                                                                                |
|  Showing 101-200 of 18,452 records                                             |
|  [First] [Prev] [Page 2 of 185] [Next] [Last]                                  |
+--------------------------------------------------------------------------------+
| Optional row expansion                                                          |
|-------------------------------------------------------------------------------|
| Full archive record fields, including extra sensors and derived values         |
+--------------------------------------------------------------------------------+
```

Notes:
- The default visible columns should be the most useful weather fields, with column picker support for the rest.
- Export should operate on the current filtered result set, not just the current page.

### 7.7 Suggested implementation order before mock HTML

```text
1. Station Info / Diagnostics page
2. Console Settings page
3. Application Settings page
4. Alarms page with history storage design
5. Archive pageable grid
```

Reasoning:
- The first three pages align directly with the existing value inventory and current code surface.
- The alarms page needs one design decision first: where alarm trigger history is evaluated and persisted.
- The archive page mostly depends on data shaping and paging UX rather than new protocol work.

### 7.8 Editable data implementation note

When implementing editable data, validation should happen wherever practical, not only after submit.

Guidance:
- Validate against known acceptable ranges, enum values, and protocol constraints in the UI when possible.
- Validate again on the server or component action path; UI validation is helpful, but it is not sufficient by itself.
- Use controls that match the data shape so invalid input is harder to enter in the first place.

Preferred control patterns:
- `DateTime` and console time values: date picker, time picker, or combined date-time picker.
- Fixed Davis option sets such as DST mode, rain bucket type, timezone code, transmitter type, and temperature logging mode: dropdown or combo box.
- Numeric values with a well-defined range such as altitude, archive interval, wind direction offset, humidity offsets, calibration offsets, and alarm thresholds: numeric edit controls with min/max/step and unit labels.
- Small bounded ranges where visual position helps, such as some alarm thresholds or calibration tuning: slider plus numeric input when the exact value still matters.
- Boolean values such as active flags, lamp state, or enable/disable application behaviors: checkbox or toggle.
- Destructive actions such as archive clear, alarm clear, or bulk reset operations: action button with confirmation.

Implementation expectations:
- Show units next to editable values, not only in surrounding labels.
- Prefer constrained inputs over free-text entry.
- Disable or hide incompatible inputs when one mode makes another invalid, for example timezone code versus GMT offset mode.
- Keep raw read-only protocol values visible when they help the operator understand what will change.
- Surface validation messages in operator language, not protocol language alone.

## Appendix A. Davis Timezone Codes

These are the codes currently defined in [DavisTimeZoneTable.cs](../src/HVO.Hardware.DavisVantagePro2/Protocol/DavisTimeZoneTable.cs).

| Code | Label |
| --- | --- |
| 0 | Dateline (UTC-12) |
| 1 | Samoa (UTC-11) |
| 2 | Hawaii (UTC-10) |
| 3 | Alaska (UTC-9) |
| 4 | Pacific (UTC-8) |
| 5 | Mountain (UTC-7) |
| 6 | Central (UTC-6) |
| 7 | Eastern (UTC-5) |
| 8 | Atlantic (UTC-4) |
| 9 | Newfoundland (UTC-3:30) |
| 10 | E. South America (UTC-3) |
| 11 | Mid-Atlantic (UTC-2) |
| 12 | Azores (UTC-1) |
| 13 | GMT (UTC+0) |
| 14 | Central Europe (UTC+1) |
| 15 | South Africa (UTC+2) |
| 16 | Arab (UTC+3) |
| 17 | Iran (UTC+3:30) |
| 18 | Arabian (UTC+4) |
| 19 | Afghanistan (UTC+4:30) |
| 20 | West Asia (UTC+5) |
| 21 | India (UTC+5:30) |
| 22 | Central Asia (UTC+6) |
| 23 | Myanmar (UTC+6:30) |
| 24 | SE Asia (UTC+7) |
| 25 | China (UTC+8) |
| 26 | Tokyo (UTC+9) |
| 27 | Cen. Australia (UTC+9:30) |
| 28 | AUS Eastern (UTC+10) |
| 29 | Central Pacific (UTC+11) |
| 30 | New Zealand (UTC+12) |
| 31 | Tonga (UTC+13) |