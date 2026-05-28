# Davis Vantage Pro2 HVO API Contracts

This document describes HVO local APIs, outbox payloads, and central/cloud contract decisions. Vendor protocol details are in [manufacturer-protocol.md](manufacturer-protocol.md). Code structure is in [hvo-implementation.md](hvo-implementation.md).

## Local Configuration

| Setting | Type | Required | Secret | Runtime editable | Default | Notes |
|---------|------|----------|--------|------------------|---------|-------|
| `StationOptions.Host` | string | Yes | No | App config | none | Console/IP adapter host. |
| `StationOptions.Port` | int | Yes | No | App config | `22222` | TCP port. |
| `StationOptions.SocketTimeoutSeconds` | int | Yes | No | App config | code-defined | Network timeout. |
| `StationOptions.ArchiveCatchupMode` | enum | Yes | No | App config/UI candidate | code-defined | Disabled/Enabled/Force. |
| `StationOptions.ArchiveCatchupLookbackHours` | int | Yes | No | App config/UI candidate | code-defined | Startup catchup range. |
| `StationOptions.StationId` | string | Yes | No | App config | code-defined | Sent downstream. |
| `OutboxOptions.ApiEndpoint` | string | Yes for forwarding | No | App config | placeholder | Website weather endpoint. |
| `OutboxOptions.ApiKey` | string | Yes for forwarding | Yes | Secret only | placeholder | Do not display/log. |

## Local API Plan

| Endpoint | Purpose | Auth | Request | Response | Notes |
|----------|---------|------|---------|----------|-------|
| `/api/weather/current` | Current curated conditions | Gateway API key | none | `CurrentConditionsResponse` | Existing endpoint. |
| `/health` | Container health | none/internal | none | health status | Existing endpoint. |

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

## Candidate Streams

| Stream | Payload type | Cadence | Idempotency key | Cloud treatment | Notes |
|--------|--------------|---------|-----------------|-----------------|-------|
| `weather.raw.live` | Weather observation | LOOP cadence | station + recordedAt | Central weather raw/current | Needs expanded schema for rain semantics. |
| `weather.archive` | Archive observation | Archive interval | station + archive recordedAt | Central historical weather | Should likely be separate from live LOOP records. |
| `gateway.status` | Gateway runtime/health | Low frequency | gateway + recordedAt | Central gateway cards | Include source freshness/outbox state. |
| `weather.station-config` | Station settings snapshot | On change/manual | station + recordedAt/hash | Optional config history | Keep console writes local-only until safety design. |
