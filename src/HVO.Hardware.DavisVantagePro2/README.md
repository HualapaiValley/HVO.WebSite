# HVO.Hardware.DavisVantagePro2

Headless .NET 10 edge collector for a Davis Vantage Pro 2 console connected through a WeatherLink IP TCP bridge.

## Runtime

- Uses `HVO.Edge.Hosting` for mounted configuration, structured telemetry, protected diagnostics, and health endpoints.
- Preserves validated LOOP1/LOOP2 merge and streaming behavior plus DMPAFT parsing, CRC retry, cancellation, and reconnect handling.
- Stores live and complete archive payloads in the shared SQLite outbox before delivery.
- Delivers live records to `/api/v1/weather/raw/batch` and typed archive records to `/api/v1/weather/archive/batch`.
- Stores station settings, station information, and the archive cursor in `/app/data/davis-local.db` independently from outbox retention.
- Projects a bounded current merged LOOP state and availability to Home Assistant MQTT. Archive history remains canonical in the website database.
- Optionally publishes the latest merged LOOP state to CWOP/APRS-IS without polling the console or replaying observations.

## Archive continuity

The cursor is keyed by `Station:StationId` and stores the console-local timestamp separately from its UTC equivalent. A missing cursor deliberately requests the full archive currently available from the console. Every startup, reconnect, and periodic top-off requests an overlap of configured archive intervals. Periodic top-off runs between finite LOOP batches no more frequently than the station archive interval. The cursor advances only after an archive record is inserted into the outbox or confirmed as an existing duplicate. `LegacyArchiveConsoleUtcOffsetHours` is required so pre-connect migration never guesses an offset from uninitialized station state.

## Configuration

Production uses `/app/config/gateway.json` and the read-only `/run/secrets` mount. See `deploy/pi-gateways/davis/gateway.json.example`. Required secret files are `diagnostics-api-key`, `central-ingest-api-key`, and, when MQTT is enabled, `mqtt-username` and `mqtt-password`. When Weather Underground publication is enabled, `weather-underground-station-key` is also required; the separate Weather Underground query API key is not used. CWOP defaults to its standard `pass -1` login; an assigned registered credential can instead be mounted as `cwop-passcode`.

Weather Underground publication is disabled by default. See `docs/gateways/davis-vantage-pro2/weather-underground-deployment.md` for the five-second PWS contract, field mapping, diagnostics, secret sync, enablement, and rollback.

CWOP publication is disabled by default. See `docs/gateways/davis-vantage-pro2/cwop-deployment.md` for APRS-IS configuration, mapping, credential handling, diagnostics, and controlled rollout.

The preserved named volume is `davis-outbox`, mounted at `/app/data`.

## Endpoints

- `/health/live`: process liveness
- `/health` and `/health/ready`: collector readiness
- `/diagnostics/health`, `/diagnostics/status`, `/diagnostics/outbox`: protected standard diagnostics
- `PUT /diagnostics/outbox/settings`: protected runtime outbox tuning

No local dashboard or static web assets are served.
