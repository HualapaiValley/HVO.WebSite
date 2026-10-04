# Davis Vantage Pro2 HVO API Contracts

This document describes the current headless collector endpoints, typed outbox payloads and central mappings. [Program.cs](../../../src/HVO.Hardware.DavisVantagePro2/Program.cs) maps shared runtime endpoints only. The former `/api/weather/current` and local status/settings UI are removed; the [May 8 inventory](../../archive/2026-05-08-davis-console-inventory.md) preserves their design evidence. Vendor protocol details are in [manufacturer-protocol.md](manufacturer-protocol.md), reusable field/settings facts in [console-fields-and-settings.md](console-fields-and-settings.md), and runtime ownership in [hvo-implementation.md](hvo-implementation.md).

## Local Configuration

Production requires the non-secret mounted `/app/config/gateway.json`. [Mounted configuration](../../../src/HVO.Edge.Hosting/Configuration/EdgeConfigurationBuilderExtensions.cs) loads it without hot reload, then reapplies environment overrides. `HVO_EDGE_CONFIG_FILE` may select an absolute file under the configured config directory. Credentials are filenames resolved at startup under `/run/secrets`; changes require an approved replacement/restart, not a UI save.

| Setting | Current owner/default | Notes |
|---------|-----------------------|-------|
| `Station:Host`, `Port` | [StationOptions](../../../src/HVO.Hardware.DavisVantagePro2/Configuration/StationOptions.cs); required host, port `22222` | Non-secret WeatherLink IP endpoint. |
| `Station:SocketTimeoutSeconds`, `MaxConsecutiveErrors` | `8`, `5` | Bounded I/O and reconnect thresholds; validated ranges are in options/validator. |
| `Station:StationId` | Required stable identity | Must agree with `Edge:Runtime:SourceId`. |
| `Station:ArchiveCatchupMode` | `Disabled` | `Enabled`/`Force` exist; production recurring recovery remains disabled pending #346. |
| `Station:ArchiveOverlapIntervals` | `2` | Legacy option retained in config; current worker requests the exact cursor, so this is not a promised overlap behavior. #346 owns removal/documentation. |
| `Station:CentralIngestBaseEndpoint`, `AllowInsecureCentralIngest` | Required validated base URI; explicit HTTP opt-in | Sender appends live/archive routes. Follow the reviewed internal-transport configuration in operations. |
| `Station:CentralApiKeySecret` | `central-ingest-api-key` | Secret-file name, never inline credentials in config/output. |
| `Station:LegacyArchiveConsoleUtcOffsetHours` | Explicit required migration offset | Pre-connect migration must not guess from uninitialized console or host timezone. |
| `Station:LocalDatabasePath` | `/app/data/davis-local.db` | Durable settings/info/cursor; separate from outbox retention. |
| `Outbox:DatabasePath`, `PayloadTypes`, `PayloadVersion` | `/app/data/outbox.db`, live/archive v1, `1` | Shared Edge.Outbox owns retries/retention/deduplication; obsolete custom `OutboxOptions.ApiEndpoint/ApiKey` are not current configuration. |
| `Edge:Runtime:DiagnosticsApiKeySecret` | `diagnostics-api-key` | Protected diagnostics credential, distinct from ingest. |
| `HomeAssistant:Mqtt:*`, `WeatherUnderground:*`, `Cwop:*` | Optional projection/publication options | MQTT credentials and publication secrets remain separate; WU/CWOP default disabled. |

The [mounted example](../../../deploy/pi-gateways/davis/gateway.json.example), [project setup](../../../src/HVO.Hardware.DavisVantagePro2/README.md), [operations](../../GATEWAY_OPERATIONS.md) and distinct [WU](weather-underground-deployment.md)/[CWOP](cwop-deployment.md) guides own complete prerequisites and rollout.

## Current local endpoints

| Endpoint | Purpose | Auth | Request | Response | Notes |
|----------|---------|------|---------|----------|-------|
| `GET /health/live` | Process liveness | Public | none | alive status | Does not verify device/ingest connectivity. |
| `GET /health`, `GET /health/ready` | Actual health snapshot | Public | none | `GatewayHealthSnapshot` | Critical returns 503; degraded/noncritical can return 200. |
| `GET /diagnostics/health` | Health snapshot | Diagnostics key | none | Health snapshot | Missing/incorrect credential returns 403. |
| `GET /diagnostics/status` | Runtime, devices, health, outbox and external delivery state | Diagnostics key | none | `GatewayDiagnosticStatusResponse` | Device status is inside this response; no `/diagnostics/devices`. |
| `GET /diagnostics/outbox` | Queue diagnostics | Diagnostics key | none | Outbox snapshot | Not evidence of historical completeness by itself. |
| `PUT /diagnostics/outbox/settings` | Bounded outbox batch/sweep tuning | Diagnostics key | batch/sweep overrides or reset | Effective settings | Runtime-only overrides reset on restart; not a console configuration write. |

The [route mapper](../../../src/HVO.Edge.Hosting/Diagnostics/EdgeDiagnosticsEndpointRouteBuilderExtensions.cs) and [authorization filter](../../../src/HVO.Edge.Hosting/Diagnostics/EdgeDiagnosticsAuthorizationFilter.cs) are authoritative. No dashboard, current-weather/display DTO, generic protocol passthrough or console-write HTTP/MQTT endpoint exists.

## Contract Model Principles

Future local/cloud contracts should preserve these distinctions. Current payloads do not fully enforce them yet.

| Concept | Meaning | Current state | Target contract behavior |
|---------|---------|---------------|--------------------------|
| Davis protocol/raw value | Value exactly as encoded by the Davis command or packet after only mechanical decode. | Mostly internal to packet parsers. | Expose only when consumers need protocol fidelity; name with `Davis` or `Raw` context. |
| HVO-normalized value | Value converted into HVO's preferred engineering units, currently deg F, mph, inches, inHg, UTC timestamps. | Most current `*F`, `*Mph`, `*Inches`, `*InHg` fields are HVO-normalized. | Keep unit suffixes explicit and avoid implying console display settings changed the raw protocol. |
| Console display setting | User preference stored in EEPROM unit bits, such as temperature, wind, rain, or barometer display units. | Read/cached by `VantageStation` and local metadata store; no current display API/UI. | If a new presentation contract is approved, publish display settings separately; never change observation units to match a screen preference. |
| HVO-derived value | Value calculated by HVO rather than reported by the console. | Production HA moon phase/illumination/rise/set use Staging calculations; weather fallback/dew-risk calculations remain proposals. | Preserve provenance; do not relabel calculations as vendor measurements. |
| Live LOOP observation | Instantaneous/current console values plus LOOP1 status cached near the LOOP2 sample time. | Current live outbox is one payload shape. | Keep separate from archive interval records. |
| Archive interval observation | Davis archive record with interval/high/low/aggregate semantics. | Current archive outbox is separate from live outbox and maps interval rain to `RainfallInches`. | Preserve interval semantics and avoid merging blindly with live current readings. |

## Live Outbox Payload

Current [DavisWeatherLivePayload](../../../src/HVO.Edge.Contracts/Weather/DavisWeatherLivePayload.cs) is mapped by [WeatherStationWorker.MapLive](../../../src/HVO.Hardware.DavisVantagePro2/Workers/WeatherStationWorker.cs) and written by [DavisOutboxWriter](../../../src/HVO.Hardware.DavisVantagePro2/Outbox/DavisOutboxWriter.cs) as `com.hvo.weather.raw.v1`. C# `RecordedAtUtc` deliberately serializes as JSON `recordedAt`; remaining properties use web camelCase. Complete local payload coverage is broader than the central raw table.

| HVO payload name | Source | Unit/shape | Notes |
|------------------|--------|------------|-------|
| `StationId` | `StationOptions.StationId` | string | HVO metadata, not Davis protocol. |
| `RecordedAtUtc` (JSON `recordedAt`) | `Loop2Packet.RecordedAtUtc` | UTC timestamp | Gateway receive/parse time. |
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

Current [DavisWeatherArchivePayload](../../../src/HVO.Edge.Contracts/Weather/DavisWeatherArchivePayload.cs) is mapped by `WeatherStationWorker.MapArchive` and stored separately as `com.hvo.weather.archive.v1`. It retains both timestamp identities and full interval semantics; web JSON uses `recordedAtUtc` and `consoleRecordedAtLocal`.

| HVO payload name | Source | Unit/shape | Notes |
|------------------|--------|------------|-------|
| `StationId` | `StationOptions.StationId` | string | HVO metadata. |
| `RecordedAtUtc` | archive timestamp plus console UTC offset | UTC timestamp | Console-local archive timestamp converted to UTC. |
| `ConsoleRecordedAtLocal` | archive date/HHMM | Unspecified-kind local timestamp | Durable console cursor and vendor timestamp remain separate from UTC. |
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
| `DownloadRecordType` | archive record | integer | Retains the Davis record discriminator. |
| `LeafTemp1F`, `LeafTemp2F` | archive extra sensors | deg F | Byte-minus-90; null when absent. |
| `LeafWetnessScaled` | archive extra sensors | 0-15 scale | Array from record bytes. |
| `SoilTemperaturesF` | archive extra sensors | deg F | Byte-minus-90 array. |
| `ExtraHumiditiesPercent` | archive extra sensors | % RH | Array. |
| `ExtraTemperaturesF` | archive extra sensors | deg F | Byte-minus-90 array. |
| `SoilMoisturesCb` | archive extra sensors | centibars | Array. |

## Central delivery and mapping

[DavisOutboxBatchSender](../../../src/HVO.Hardware.DavisVantagePro2/Outbox/DavisOutboxBatchSender.cs) partitions live/archive rows to `POST /api/v1/weather/raw/batch` and `POST /api/v1/weather/archive/batch` using the ingest key. Live storage is [WeatherRaw](../../../src/HVO.DataModels/Models/V9/WeatherRaw.cs); archive storage/validation is owned by [WeatherArchiveIngestController](../../../src/HVO.WebSite.v9/Controllers/WeatherArchiveIngestController.cs) and [WeatherArchive](../../../src/HVO.DataModels/Models/V9/WeatherArchive.cs). These are website routes, not local collector routes. Idempotency preserves station/timestamp identity; the sender requires complete inserted/skipped/failed accounting and treats malformed accounting as retryable. See [ingest trust boundaries](../../development/ingest-trust-boundaries.md) and the [HTTP outcome/recovery matrix](../common-gateway-standards.md#sender-http-outcome-and-recovery-matrix).

| Topic | Decision/status | Rationale |
|-------|-----------------|-----------|
| `WindGust10MinMph` | Central ingest accepts this alias as `WindGustMph`. | Clear Davis LOOP-shaped name mismatch. |
| Live rain fields | Not mapped into generic `RainfallInches`. | Daily, storm, rate, rolling-window, and archive interval rain are different semantics. |
| Archive rain | `RainfallInches` on the separate typed archive entity/endpoint. | Archive record rain is interval rainfall, not a live total. |
| Console battery/status | Candidate gateway status/config stream. | Not central weather raw today. |
| Derived weather values | Prefer Davis console-derived live values; mark HVO calculations separately. | Avoid mixing vendor-derived and HVO-calculated values. |
| Raw vs normalized naming | Current payload names are HVO-normalized by suffix. | Future contracts should explicitly separate protocol/raw values, normalized values, and display settings. |
| Display unit settings | No current local weather/display API. | The former conversion design is preserved below as historical proposal; parser/outbox units remain explicit. |

## Historical display design

The former `/api/weather/current` design paired normalized fields with a converted `Display` object. It is preserved here to retain the reusable conversion distinction, not as an active endpoint. [DisplayUnitConverter](../../../src/HVO.Hardware.DavisVantagePro2/Station/DisplayUnitConverter.cs) still contains conversion helpers; current collector hosting does not expose that DTO or dashboard.

| Display field group | Source normalized fields | Conversion setting |
|---------------------|--------------------------|--------------------|
| Temperatures | `*TemperatureF`, `DewPointF`, `HeatIndexF`, `WindChillF` | `VantageStation.TemperatureUnits` (`°F`, `°F×10`, `°C`, `°C×10`) |
| Pressure | `BarometricPressureInHg`, `PressureRawInHg`, `AltimeterInHg` | `VantageStation.BarometerUnits` (`inHg`, `mmHg`, `hPa`, `mbar`) |
| Wind | `WindSpeedMph`, `WindSpeed10MinAvgMph`, `WindGust10MinMph` | `VantageStation.WindUnits` (`mph`, `m/s`, `km/h`, `knots`) |
| Rain/ET | `*RainInches`, `DailyEtInches` | `VantageStation.RainUnits` (`inch`, `mm`) |

Any future presentation must preserve explicit units and source/freshness. The old dashboard is removed; approving a new public API or UI needs separate scope and review.

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

## Current payload lanes and proposed streams

| Stream | Payload type | Cadence | Idempotency key | Cloud treatment | Notes |
|--------|--------------|---------|-----------------|-----------------|-------|
| `com.hvo.weather.raw.v1` | `DavisWeatherLivePayload` | LOOP cadence | station + UTC time + payload type | Selected central raw fields | Implemented; rain windows remain distinct locally. |
| `com.hvo.weather.archive.v1` | `DavisWeatherArchivePayload` | Archive interval when catch-up enabled | station + UTC archive time + payload type | Separate typed archive storage | Implemented; recurring acquisition disabled pending #346. |
| `gateway.status` | Gateway runtime/health | Low frequency | gateway + recordedAt | Central gateway cards | Include source freshness/outbox state. |
| `weather.station-config` | Station settings snapshot | On change/manual | station + recordedAt/hash | Optional config history | Keep console writes local-only until safety design. |
