# Davis Vantage Pro2 HVO API Contracts

This document describes HVO local APIs, outbox payloads, and central/cloud contract decisions. Vendor protocol details are in [manufacturer-protocol.md](manufacturer-protocol.md). Code structure is in [hvo-implementation.md](hvo-implementation.md).

## Local Configuration

| Setting | Type | Required | Secret | Runtime editable | Default | Notes |
|---------|------|----------|--------|------------------|---------|-------|
| `StationOptions.Host` | string | Yes | No | App config | none | Console/IP adapter host. |
| `StationOptions.Port` | int | Yes | No | App config | `22222` | TCP port. |
| `StationOptions.SocketTimeoutSeconds` | int | Yes | No | App config | code-defined | Network timeout. |
| `StationOptions.ArchiveCatchupMode` | enum | Yes | No | App config/UI candidate | code-defined | Disabled/Enabled/Force. |
| `StationOptions.StationId` | string | Yes | No | App config | code-defined | Sent downstream. |
| `OutboxOptions.ApiEndpoint` | string | Yes for forwarding | No | App config | placeholder | Website weather endpoint. |
| `OutboxOptions.ApiKey` | string | Yes for forwarding | Yes | Secret only | placeholder | Do not display/log. |

## Local API Plan

| Endpoint | Purpose | Auth | Request | Response | Notes |
|----------|---------|------|---------|----------|-------|
| `/api/weather/current` | Current curated conditions | Gateway API key | none | `CurrentConditionsResponse` | Existing endpoint. |
| `/health` | Container health | none/internal | none | health status | Existing endpoint. |

## Contract Model Principles

Future local/cloud contracts should preserve these distinctions. Current payloads do not fully enforce them yet.

| Concept | Meaning | Current state | Target contract behavior |
|---------|---------|---------------|--------------------------|
| Davis protocol/raw value | Value exactly as encoded by the Davis command or packet after only mechanical decode. | Mostly internal to packet parsers. | Expose only when consumers need protocol fidelity; name with `Davis` or `Raw` context. |
| HVO-normalized value | Value converted into HVO's preferred engineering units, currently deg F, mph, inches, inHg, UTC timestamps. | Most current `*F`, `*Mph`, `*Inches`, `*InHg` fields are HVO-normalized. | Keep unit suffixes explicit and avoid implying console display settings changed the raw protocol. |
| Console display setting | User preference stored in EEPROM unit bits, such as temperature, wind, rain, or barometer display units. | Read and cached by `VantageStation`; local UI and `/api/weather/current` display fields use these settings for presentation conversion. | Publish in station-config/status streams and display sub-objects; do not mutate protocol-normalized observation fields. |
| HVO-derived value | Value calculated by HVO from Davis fields rather than reported by the console. | Candidate only for dew/dew-risk style future values. | Include provenance such as `source=DavisConsole` or `source=HvoCalculated` if added. |
| Live LOOP observation | Instantaneous/current console values plus LOOP1 status cached near the LOOP2 sample time. | Current live outbox is one payload shape. | Keep separate from archive interval records. |
| Archive interval observation | Davis archive record with interval/high/low/aggregate semantics. | Current archive outbox is separate from live outbox and maps interval rain to `RainfallInches`. | Preserve interval semantics and avoid merging blindly with live current readings. |

## Live Outbox Payload

Current live outbox payload fields from `WeatherStationWorker.WriteToOutboxAsync`:

| HVO payload name | Source | Unit/shape | Notes |
|------------------|--------|------------|-------|
| `StationId` | `StationOptions.StationId` | string | HVO metadata, not Davis protocol. |
| `RecordedAt` | `Loop2Packet.RecordedAtUtc` | UTC timestamp | Gateway receive/parse time. |
| `TemperatureF` | `OutsideTemperatureF` | deg F | HVO alias for outside temperature. |
| `InsideTemperatureF` | LOOP1/LOOP2 | deg F | Console/inside reading. |
| `DewPointF` | LOOP2 | deg F | Console-derived. |
| `HeatIndexF` | LOOP2 | deg F | Console-derived. |
| `WindChillF` | LOOP2 | deg F | Console-derived. |
| `ThswF` | LOOP2 | deg F | Console-derived THSW index. |
| `HumidityPercent` | `OutsideHumidityPercent` | % RH | HVO alias for outside humidity. |
| `InsideHumidityPercent` | LOOP1/LOOP2 | % RH | Console/inside humidity. |
| `BarometricPressureInHg` | LOOP1/LOOP2 | inHg | Station-corrected pressure. |
| `PressureRawInHg` | LOOP2 | inHg | Raw station pressure. |
| `AltimeterInHg` | LOOP2 | inHg | Altimeter pressure. |
| `BarometricTrend` | LOOP1/LOOP2 | signed integer | Davis trend code. |
| `WindSpeedMph` | LOOP1/LOOP2 | mph | Instantaneous wind. |
| `WindDirectionDegrees` | LOOP1/LOOP2 | integer degrees | HVO casts nullable double to integer. |
| `WindSpeed10MinAvgMph` | LOOP1/LOOP2 | mph | LOOP2 has tenths precision. |
| `WindSpeed2MinAvgMph` | LOOP2 | mph | LOOP2-only. |
| `WindGust10MinMph` | LOOP2 | mph | Central ingest also accepts this alias as `WindGustMph`. |
| `WindGust10MinDirectionDegrees` | LOOP2 | integer degrees | HVO casts nullable double to integer. |
| `RainRateInchesPerHour` | LOOP1/LOOP2 | in/hr | Bucket-click converted. Do not treat as rainfall total. |
| `DailyRainInches` | LOOP1/LOOP2 | inches | Since midnight at console day boundary. |
| `Rain15MinInches` | LOOP2 | inches | Recent 15-minute amount. |
| `HourRainInches` | LOOP2 | inches | Recent 60-minute amount. |
| `Rain24HourInches` | LOOP2 | inches | Recent 24-hour amount. |
| `StormRainInches` | LOOP1/LOOP2 | inches | Since `StormStartDate`; null when no active storm. |
| `StormStartDate` | LOOP1/LOOP2 | local date shape | Encoded in LOOP; HVO currently creates `DateTimeKind.Local`. |
| `MonthlyRainInches` | LOOP1 | inches | LOOP1-only. |
| `YearlyRainInches` | LOOP1 | inches | LOOP1-only; rain year start is EEPROM-configured. |
| `SolarRadiationWm2` | LOOP1/LOOP2 | W/m2 | Sensor-dependent. |
| `UvIndex` | LOOP1/LOOP2 | index | Raw byte divided by 10. |
| `DailyEtInches` | LOOP1/LOOP2 | inches | Daily evapotranspiration. |
| `MonthlyEtInches` | LOOP1 | inches | LOOP1-only. |
| `YearlyEtInches` | LOOP1 | inches | LOOP1-only. |
| `ConsoleBatteryVoltage` | LOOP1 | volts | Console battery status. |
| `TransmitterBatteryStatus` | LOOP1 | bitmask | Bit N means channel N+1 low battery. |
| `ForecastRule` | LOOP1/archive | integer | Forecast table maps rule to string. |
| `ForecastString` | HVO table | string | HVO derived from forecast rule. |
| `SunriseTime` | LOOP1 | `HH:MM` string | Decoded from HHMM integer. |
| `SunsetTime` | LOOP1 | `HH:MM` string | Decoded from HHMM integer. |

## Archive Outbox Payload

Current archive outbox payload fields from `WeatherStationWorker.WriteArchiveToOutboxAsync`:

| HVO payload name | Source | Unit/shape | Notes |
|------------------|--------|------------|-------|
| `StationId` | `StationOptions.StationId` | string | HVO metadata. |
| `RecordedAt` | archive timestamp plus console UTC offset | UTC timestamp | Console-local archive timestamp converted to UTC. |
| `ArchiveIntervalMinutes` | EEPROM archive interval | minutes | Values accepted by current write path are `1`, `5`, `10`, `15`, `30`, `60`, `120`. |
| `TemperatureF` | archive outside temp | deg F | Interval outside temperature. |
| `HighTemperatureF` | archive high outside temp | deg F | Interval high. |
| `LowTemperatureF` | archive low outside temp | deg F | Interval low. |
| `InsideTemperatureF` | archive inside temp | deg F | Interval inside temperature. |
| `HumidityPercent` | archive outside humidity | % RH | HVO alias for outside humidity. |
| `InsideHumidityPercent` | archive inside humidity | % RH | Inside humidity. |
| `BarometricPressureInHg` | archive barometer | inHg | Station pressure. |
| `WindSpeedMph` | archive wind | mph | Archive average/interval wind per Davis record semantics. |
| `WindGustMph` | archive gust | mph | Interval gust. |
| `WindDirectionDegrees` | archive direction | degrees | Encoded as 16 compass points in record, HVO multiplies by 22.5. |
| `WindGustDirectionDegrees` | archive gust direction | degrees | Encoded as 16 compass points in record, HVO multiplies by 22.5. |
| `WindSamples` | archive samples | integer | Number of wind samples in interval. |
| `RainfallInches` | archive interval rain | inches | This is interval rainfall, not daily/storm/rate. |
| `RainRateInchesPerHour` | archive high rain rate | in/hr | Highest rain rate during interval. |
| `SolarRadiationWm2` | archive solar | W/m2 | Interval solar value. |
| `HighSolarRadiationWm2` | archive high solar | W/m2 | Interval high. |
| `UvIndex` | archive UV | index | Byte divided by 10. |
| `HighUvIndex` | archive high UV | index | Byte divided by 10. |
| `EtInches` | archive ET | inches | Interval ET. |
| `ForecastRule` | archive forecast rule | integer | Forecast rule stored in record. |
| `ForecastString` | HVO table | string | HVO derived from forecast rule. |
| `LeafTemp1F`, `LeafTemp2F` | archive extra sensors | deg F | Byte-minus-90; null when absent. |
| `LeafWetnessScaled` | archive extra sensors | 0-15 scale | Array from record bytes. |
| `SoilTemperaturesF` | archive extra sensors | deg F | Byte-minus-90 array. |
| `ExtraHumiditiesPercent` | archive extra sensors | % RH | Array. |
| `ExtraTemperaturesF` | archive extra sensors | deg F | Byte-minus-90 array. |
| `SoilMoisturesCb` | archive extra sensors | centibars | Array. |

## Cloud Mapping Decisions

| Topic | Decision/status | Rationale |
|-------|-----------------|-----------|
| `WindGust10MinMph` | Central ingest accepts this alias as `WindGustMph`. | Clear Davis LOOP-shaped name mismatch. |
| Live rain fields | Not mapped into generic `RainfallInches`. | Daily, storm, rate, rolling-window, and archive interval rain are different semantics. |
| Archive rain | Currently mapped to `RainfallInches`. | Archive record rain is interval rainfall. |
| Console battery/status | Candidate gateway status/config stream. | Not central weather raw today. |
| Derived weather values | Prefer Davis console-derived live values; mark HVO calculations separately. | Avoid mixing vendor-derived and HVO-calculated values. |
| Raw vs normalized naming | Current payload names are HVO-normalized by suffix. | Future contracts should explicitly separate protocol/raw values, normalized values, and display settings. |
| Display unit settings | Current weather API includes normalized fields plus a display-converted sub-object. | Keeps existing unit-suffixed fields stable while allowing UI/API consumers to follow console display preferences. |

## Current Weather Display Sub-Object

`/api/weather/current` keeps existing HVO-normalized fields such as `OutsideTemperatureF`, `WindSpeedMph`, `DailyRainInches`, and `BarometricPressureInHg`. It also returns a `Display` object converted from those normalized values using cached console display settings.

| Display field group | Source normalized fields | Conversion setting |
|---------------------|--------------------------|--------------------|
| Temperatures | `*TemperatureF`, `DewPointF`, `HeatIndexF`, `WindChillF` | `VantageStation.TemperatureUnits` (`°F`, `°F×10`, `°C`, `°C×10`) |
| Pressure | `BarometricPressureInHg`, `PressureRawInHg`, `AltimeterInHg` | `VantageStation.BarometerUnits` (`inHg`, `mmHg`, `hPa`, `mbar`) |
| Wind | `WindSpeedMph`, `WindSpeed10MinAvgMph`, `WindGust10MinMph` | `VantageStation.WindUnits` (`mph`, `m/s`, `km/h`, `knots`) |
| Rain/ET | `*RainInches`, `DailyEtInches` | `VantageStation.RainUnits` (`inch`, `mm`) |

The local status dashboard uses the same conversion layer. Parser and outbox payload units remain protocol/HVO-normalized until a deliberate versioned schema change is made.

## Write Safety And Read-Back Policy

These rules apply before exposing any configuration-changing Davis command through a broad local UI or any cloud path. Low-risk local diagnostics can be more permissive, but destructive/configuration writes require this policy.

| Requirement | Policy |
|-------------|--------|
| Scope | Writes remain local-only. No central/cloud command path. |
| Authorization | Require local operator authorization distinct from read-only dashboard access. |
| Confirmation | Require explicit confirmation describing the exact side effect, especially for clock, archive interval, calibration, alarms, EEPROM, and archive clear. |
| Allowlist | Expose only reviewed commands. Do not add generic command passthrough. |
| Audit | Record operator, command, requested values, previous read-back values where available, result, timestamp, and error details. |
| Read-back | After a write, re-read the affected setting or status and compare expected values where the protocol supports it. |
| Failure handling | If ACK/write succeeds but read-back fails or differs, mark the operation degraded and require manual verification. |
| Live testing | High-risk writes require explicit opt-in and a pre-written rollback/manual recovery plan. |
| Destructive commands | `CLRLOG` must require a separate manual approval step and should never run in automated validation. |

| Write area | Read-back verification |
|------------|------------------------|
| Console time | Re-run `GETTIME`; allow a small clock drift tolerance. |
| Archive interval | Re-read EEPROM archive interval and confirm worker cache updates. |
| Rain bucket/year start | Re-read setup bits/rain year start and confirm rain conversion assumptions. |
| Location/timezone/DST/temp logging | Re-read corresponding EEPROM fields and confirm cached station settings. |
| Calibration | Re-read calibration EEPROM block/field. |
| Alarm thresholds | Re-read alarm threshold block and compare encoded values. |
| Active alarm bits clear | Re-read relevant status if exposed by protocol; otherwise mark as command-ACK-only. |
| Barometer calibration | Re-run `BARDATA` and compare pressure/altitude fields where applicable. |
| Transmitter/retransmit config | Re-read transmitter EEPROM fields. |
| Lamp | Low-risk command; ACK or text response is sufficient unless UI needs state. |
| Archive clear | No automated read-back is sufficient; require manual verification and backup/export decision before execution. |

## Candidate Streams

| Stream | Payload type | Cadence | Idempotency key | Cloud treatment | Notes |
|--------|--------------|---------|-----------------|-----------------|-------|
| `weather.raw.live` | Weather observation | LOOP cadence | station + recordedAt | Central weather raw/current | Needs expanded schema for rain semantics. |
| `weather.archive` | Archive observation | Archive interval | station + archive recordedAt | Central historical weather | Should likely be separate from live LOOP records. |
| `gateway.status` | Gateway runtime/health | Low frequency | gateway + recordedAt | Central gateway cards | Include source freshness/outbox state. |
| `weather.station-config` | Station settings snapshot | On change/manual | station + recordedAt/hash | Optional config history | Keep console writes local-only until safety design. |
