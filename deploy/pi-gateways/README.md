# Pi Gateway Deployments

Minimal per-gateway Docker Compose deployments for Pi-class edge hosts.

## Website upstream policy

Deployed Pi gateways post directly to the internally hosted `HVO.WebSite` API on `hvo-docker`. The public Azure address is only a proxy path into the local environment and is not a gateway ingest dependency.

Practical default:

- Pi deployment: `HVO_WEBSITE_PUBLIC_BASE_URL=http://hvo-docker.hvo.lan`
- Local development/integration override: `HVO_WEBSITE_PUBLIC_BASE_URL=http://<dev-host>`

## API key source of truth

Gateway API keys should be stored in Azure Key Vault and loaded by the website at startup through its configured Key Vault provider.

Required website secret names in `hvo-central-kv`:

- `Seeding--DavisApiKey`
- `Seeding--BmsApiKey`
- `Seeding--PowerApiKey`
- `Seeding--SmartShuntApiKey`
- `Seeding--SmartShuntSourceId`
- `Seeding--WeatherReadApiKey`
- `Seeding--PowerReadApiKey`

Reason:

- The website loads Azure Key Vault into configuration at startup.
- ASP.NET Core configuration maps `--` in Key Vault secret names to `:` in configuration keys.
- `ApiKeySeedService` reads `Seeding:DavisApiKey`, `Seeding:BmsApiKey`, `Seeding:PowerApiKey`, `Seeding:WeatherReadApiKey`, and `Seeding:PowerReadApiKey`.

Current local raw keys already present in the dev environment:

- `DAVIS_API_KEY` for `ingest:weather`
- `BMS_API_KEY` for `ingest:bms`

New power keys generated for rollout:

- `POWER_API_KEY` for `ingest:power`
- `POWER_READ_API_KEY` for `read:power`

Weather read clients:

- `WEATHER_READ_API_KEY` for `read:weather`

Important:

- The database stores only key hashes, not the raw values.
- After a raw key is lost, it cannot be recovered from SQL.
- Keep the raw values only in approved secret stores and deployment env files.

## Current targets

- `deploy/pi-gateways/davis`
- `deploy/pi-gateways/eg4`
- `deploy/pi-gateways/jkbms`
- `deploy/pi-gateways/smartshunt`

Each gateway is deployed independently so Pi rollouts do not depend on the main repo-level compose stack.

`deploy/pi-gateways/home-assistant-exporter` retains the implemented exporter deployment template, but the exporter is intentionally disabled in production with no mappings or source claims. Do not deploy or enable it without a separately approved source-authority change. The retired direct SolarAssistant and TP-Link/Kasa deployment stacks, containers, images, and Docker volumes have been removed. A checksum-verified SolarAssistant archive is retained outside Docker storage.

## Davis deployment notes

- Weather Underground publication is disabled by default in `davis/gateway.json.example` and targets station `KAZKINGM12` on a five-second cadence with a two-second request timeout.
- The upload station key is stored in Key Vault as `WeatherUnderground--StationKey`. It is materialized as the ignored `davis/secrets/weather-underground-station-key` file only when the ignored local Davis `gateway.json` enables publication.
- `WeatherUnderground--ApiKey` is a separate query credential and is never retrieved or mounted for PWS upload. No Weather Underground credential belongs in `.env` or Compose environment entries.
- The existing secrets-directory bind remains read-only at `/run/secrets`. Davis preflight validates the complete section and requires a non-empty station-key file only when enabled, before SSH synchronization or container changes.
- Run `bash tools/validate-davis-weather-underground-deployment.sh` for deterministic disabled/enabled, secret, Compose, ignore, and API-key-isolation checks.
- Follow `docs/gateways/davis-vantage-pro2/weather-underground-deployment.md` for protocol mapping, diagnostics, enablement, verification, and rollback.

## Common workflow

1. Copy `.env.example` to `.env` and `gateway.json.example` to `gateway.json` where provided.
2. Keep device endpoints such as `DAVIS_STATION_HOST` in `.env`; fill mounted gateway configuration and create the required files under the configured local secrets directory.
3. Set the absolute `*_REMOTE_CONFIG_FILE` and `*_REMOTE_SECRETS_DIRECTORY` paths from `.env.example`. For SSH Docker contexts, the deploy script copies the local files to those daemon-host paths with owner-only permissions before Compose starts the container.
4. Deploy with the Pi Docker context:

```bash
./scripts/deploy-pi-gateway.sh --context devpi5 davis
./scripts/deploy-pi-gateway.sh --context devpi5 eg4
./scripts/deploy-pi-gateway.sh --context devpi5 jkbms
./scripts/deploy-pi-gateway.sh --context devpi5 smartshunt
```

Deploy only the named active collector being changed. The HA exporter is not a production target.

To verify deployed endpoints after rollout:

```bash
./scripts/check-deployments.sh
```

## Current telemetry status

- All gateway hosts emit compact structured logs to stdout and conditionally export logs, traces, and metrics to the shared collector.
- The collector is Pi-reachable at `http://192.168.1.238:4318` using `http/protobuf` only when the observability stack sets `HVO_OBSERVABILITY_BIND_ADDRESS=192.168.1.238`; verify the resolved bind and connectivity before configuring each stack's `OTEL_COLLECTOR_ENDPOINT` value.
- Use `docker logs <container>` for short-term local diagnostics and Grafana/Loki for centralized logs. Gateway applications do not create `/app/logs` or manage local log files.
- Every gateway has a nominal 34 MB local budget: three compressed 10 MB files plus a 4 MB non-blocking buffer. When full, new stdout records are dropped rather than blocking device polling.
- Core dumps are disabled in every gateway container. The deploy script verifies both logging and core policies after recreation.
- Direct OTLP logging buffers at most 5,000 events and retries for ten minutes. Longer outages can lose central records; use bounded local `docker logs` for incident reconstruction.
- Davis, JK BMS, EG4, and SmartShunt retain independent acquisition, diagnostics, and shared-outbox forwarding when OTLP is unavailable.

## JK BMS deployment notes

- The JK BMS container needs `privileged: true` and the host D-Bus mount `/run/dbus/system_bus_socket` so BlueZ access works from Docker on the Pi.
- Copy `gateway.json.example` to ignored `gateway.json`; mount diagnostics, central-ingest, and optional MQTT credentials as files in the ignored `secrets` directory.
- The Compose project, service name, and `jkbms-outbox` volume are unchanged so the production outbox remains attached during cutover.
- Use `JKBMS_HCI_ADAPTER=hci0` as the default unless a specific Pi host proves another adapter is more stable.
- The deployment preflight validates the mounted vNext identity/outbox contract and required secrets before container recreation.
- Follow `docs/gateways/jkbms/deployment-and-endurance.md`; never run the physical endurance check as part of routine PR validation.

## SmartShunt deployment notes

- The paired direct public-GATT collector is the sole acquisition authority and central writer. Home Assistant receives only the collector's read-only MQTT projection; do not enable an HA/ESPHome acquisition or exporter mapping for this device.
- Copy `gateway.json.example` to ignored `gateway.json`; mount diagnostics, central-ingest, and optional MQTT credentials as separate files in the ignored `secrets` directory.
- Root Compose also requires those mounted files. The API key remains exclusively in `secrets/central-ingest-api-key`.
- The Compose project, service name, and `smartshunt-outbox` volume remain unchanged, preserving queued legacy summaries during migration to the shared outbox schema.
- The deployment preflight validates direct authority, source identity, the public-GATT MAC address, shared outbox path/type, and required secret files before SSH synchronization and recreation.
- Follow `docs/gateways/victron-smartshunt.md` for exactly-one-owner cutover, rollback, and the optional bounded `TestCategory=Live` check.

## Recommended workflow

1. Develop website/API changes in the repo devcontainer and validate them against `hvo-docker`.
2. Deploy the website/API update to `hvo-docker` when the gateway contract is ready.
3. Deploy the gateway container to the Pi with `HVO_WEBSITE_PUBLIC_BASE_URL` pointed at `http://hvo-docker.hvo.lan`.
4. Keep the public Azure proxy path out of gateway ingestion so local telemetry does not depend on external routing.

## Internal rollout sequence

1. Confirm the website container on `hvo-docker` can read secrets from `https://hvo-central-kv.vault.azure.net/`.
2. Store the raw API keys in Key Vault using the secret names listed above.
3. Restart or roll a new website revision so startup seeding runs again.
4. Verify the keys against the live internal website API.
5. Copy only the needed raw gateway key into the Pi gateway `.env` file.

## Current local ports

- Davis headless health and protected diagnostics: `http://<pi-host>:5100`
- JK BMS headless health and protected diagnostics: `http://<pi-host>:5200`
- SmartShunt headless health and protected diagnostics: `http://<pi-host>:5400`
- EG4 headless health and protected diagnostics: `http://<pi-host>:5600`

Ports 5300 and 5500 are no longer assigned to direct SolarAssistant or TP-Link/Kasa services. Port 5700 is reserved by the disabled HA exporter template and has no production service.

## EG4 deployment notes

- Follow `docs/gateways/eg4/deployment-and-shadow-validation.md` before rollout.
- Copy `gateway.json.example` to ignored `gateway.json`; mount diagnostics, central-ingest, and MQTT credentials as files in the ignored `secrets` directory.
- Map only stable `/dev/hvo/eg4-6500ex-*` HID nodes and the verified MPPT `/dev/serial/by-id/...` path; never map enumerated `/dev/hidrawN` or `/dev/ttyUSBN` names.
- The MPPT serial device remains isolated by default. `EG4_MPPT_0_ENABLED=true` selects `docker-compose.mppt.yml`, which maps only that stable path and permits only the fixed unit-1 function-`0x03` read of registers 200-217.
- The second-inverter overlay remains selected only when `EG4_DEVICE_1_ENABLED=true` and both stable inverter HID nodes exist.
- Production simulation and all command/write paths are disabled. The gateway publishes battery branches, independent MPPT detail, and read-only inverter AC/load/temperature/status detail.
- Direct current state is projected to Home Assistant through MQTT Discovery. MQTT is not the canonical historical-delivery path.
- Set `HVO_CHECK_EG4=true` when `check-deployments.sh` should require EG4 health.
