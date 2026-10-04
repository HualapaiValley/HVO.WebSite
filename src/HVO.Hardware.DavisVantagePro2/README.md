# HVO.Hardware.DavisVantagePro2

Headless .NET 10 edge collector for a Davis Vantage Pro 2 console connected through a WeatherLink IP TCP bridge.

[Program.cs](Program.cs) composes [Davis registration](Hosting/DavisServiceCollectionExtensions.cs). [Dependencies](HVO.Hardware.DavisVantagePro2.csproj) are Edge.Contracts (typed observations), Edge.Hosting (config/telemetry/diagnostics), Edge.Outbox (durability), Edge.HomeAssistant.Mqtt (presentation) and Staging (production moon phase/illumination/rise/set calculations). [#372](https://github.com/HualapaiValley/HVO.WebSite/issues/372) must migrate that production consumer after its SDK/package prerequisite; Staging is not test-only.

## Runtime

- Uses `HVO.Edge.Hosting` for mounted configuration, structured telemetry, protected diagnostics, and health endpoints.
- Preserves validated LOOP1/LOOP2 merge and streaming behavior plus DMPAFT parsing, CRC retry, cancellation, and reconnect handling.
- Stores live and complete archive payloads in the shared SQLite outbox before delivery.
- Delivers live records to `/api/v1/weather/raw/batch` and typed archive records to `/api/v1/weather/archive/batch`.
- Stores station settings, station information, and the archive cursor in `/app/data/davis-local.db` independently from outbox retention.
- Projects a bounded current merged LOOP state and availability to Home Assistant MQTT. Archive history remains canonical in the website database.
- Optionally publishes the latest merged LOOP state to CWOP/APRS-IS without polling the console or replaying observations.

## Archive continuity

The cursor is keyed by `Station:StationId` and keeps console-local/UTC timestamps separate. When archive acquisition is enabled, a missing cursor requests the explicit full archive; a retained cursor requests its exact console-local timestamp. `ArchiveOverlapIntervals` is not consumed by the current worker and does not promise overlap. Finite top-off accepts at most 25 records between LOOP batches and advances the cursor only after durable outbox insertion or confirmed duplicate. `LegacyArchiveConsoleUtcOffsetHours` is required so pre-connect migration never guesses an offset from uninitialized station state.

Recurring production catch-up remains disabled: this console's non-full cursor request returns a 513-page/start-index-1 circular buffer. The client cancels that response with ESC before page one, preserving live acquisition and the cursor. Efficient bounded recovery remains [#346](https://github.com/HualapaiValley/HVO.WebSite/issues/346); explicit full bootstrap and tested cancellation do not complete that follow-up.

## Configuration

Production requires non-secret `/app/config/gateway.json` and read-only `/run/secrets`. Environment overrides win; JSON reload is disabled and secrets are read at startup. See the [mounted example](../../deploy/pi-gateways/davis/gateway.json.example), [StationOptions](Configuration/StationOptions.cs) and [operations prerequisites](../../docs/GATEWAY_OPERATIONS.md). Configure a reachable WeatherLink host, stable station/runtime source identity, explicit migration offset and approved central transport before an operational start. Required secret files are `diagnostics-api-key`, `central-ingest-api-key`, and, when MQTT is enabled, `mqtt-username` and `mqtt-password`. Enabled Weather Underground requires `weather-underground-station-key`; the separate query API key is unused. CWOP defaults to `pass -1`; an assigned registered credential can instead be mounted as `cwop-passcode`.

Weather Underground defaults disabled. Its [PWS guide](../../docs/gateways/davis-vantage-pro2/weather-underground-deployment.md) owns mapping, credentials, diagnostics and controlled rollout.

CWOP defaults disabled. Its distinct [APRS-IS guide](../../docs/gateways/davis-vantage-pro2/cwop-deployment.md) owns mapping, credentials and latest-only controlled publication.

The preserved named volume is `davis-outbox`, mounted at `/app/data`.

## Endpoints

- `/health/live`: process liveness
- `/health` and `/health/ready`: actual health snapshot; Critical returns 503, noncritical degraded health can be 200
- `/diagnostics/health`, `/diagnostics/status`, `/diagnostics/outbox`: protected standard diagnostics
- `PUT /diagnostics/outbox/settings`: protected runtime outbox tuning

No local dashboard or static web assets are served.

## Local validation and references

Use the exact SDK in [global.json](../../global.json) and [testing prerequisites](../../docs/development/testing.md). From repository root after locked restore/build, this focused command requires no physical console:

```bash
dotnet test tests/HVO.Hardware.DavisVantagePro2.Tests --filter "TestCategory!=Integration&TestCategory!=Live"
```

Owned simulator/Integration fixtures and physical Live cases have separate boundaries. The [current manual](../../docs/gateways/davis-vantage-pro2/README.md), [typed contracts](../../docs/gateways/davis-vantage-pro2/hvo-api-contracts.md), [Pi setup](../../deploy/pi-gateways/README.md), [cutover](../../docs/gateways/davis-vantage-pro2/cutover-and-rollback.md) and [quiescent SQLite recovery](../../docs/gateways/sqlite-backup-and-rollback.md) own deeper protocol, rollout and recovery contracts. Routine validation performs no deployment, publication or console write.
