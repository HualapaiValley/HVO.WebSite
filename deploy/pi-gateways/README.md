# Pi Gateway Deployments

Minimal per-gateway Docker Compose deployments for Pi-class edge hosts.

## Website upstream policy

Deployed Pi gateways should normally post to the public `HVO.WebSite` API, not directly to `hvo-docker`.

Use `hvo-docker` as the upstream only when you are intentionally validating unpublished website/API changes, local infrastructure behavior, or end-to-end development flows before Azure is updated.

Practical default:

- Pi deployment: `HVO_WEBSITE_PUBLIC_BASE_URL=https://www.hualapaivalleyobservatory.org`
- Local development/integration override: `HVO_WEBSITE_PUBLIC_BASE_URL=http://<hvo-docker-or-dev-host>`

## API key source of truth

Gateway API keys should be stored in Azure Key Vault and loaded by the website at startup through its configured Key Vault provider.

Recommended website secret names in `hvoobs-kv`:

- `Seeding--DavisApiKey`
- `Seeding--BmsApiKey`
- `Seeding--PowerApiKey`
- `Seeding--SmartShuntApiKey`
- `Seeding--SmartShuntSourceId`
- `Seeding--WeatherReadApiKey`
- `Seeding--PowerReadApiKey`
- `Seeding--HomeAssistantExporterApiKey`
- `Seeding--HomeAssistantExporterSources--0` (repeat the numeric suffix for each reserved `kasa:`/`govee:` source)

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
- `deploy/pi-gateways/home-assistant-exporter`
- `deploy/pi-gateways/jkbms`
- `deploy/pi-gateways/solarassistant`
- `deploy/pi-gateways/smartshunt`
- `deploy/pi-gateways/tplink-kasa`

Each gateway is deployed independently so Pi rollouts do not depend on the main repo-level compose stack.

## Common workflow

1. Copy `.env.example` to `.env` and `gateway.json.example` to `gateway.json` where provided.
2. Keep device endpoints such as `DAVIS_STATION_HOST` in `.env`; fill mounted gateway configuration and create the required files under the configured local secrets directory.
3. Set the absolute `*_REMOTE_CONFIG_FILE` and `*_REMOTE_SECRETS_DIRECTORY` paths from `.env.example`. For SSH Docker contexts, the deploy script copies the local files to those daemon-host paths with owner-only permissions before Compose starts the container.
4. Deploy with the Pi Docker context:

```bash
./scripts/deploy-pi-gateway.sh --context devpi5 davis
./scripts/deploy-pi-gateway.sh --context devpi5 eg4
./scripts/deploy-pi-gateway.sh --context devpi5 ha-exporter
./scripts/deploy-pi-gateway.sh --context devpi5 jkbms
./scripts/deploy-pi-gateway.sh --context devpi5 solarassistant
./scripts/deploy-pi-gateway.sh --context devpi5 smartshunt
./scripts/deploy-pi-gateway.sh --context devpi5 tplinkkasa
```

To deploy all gateway stacks from the current checkout:

```bash
./scripts/deploy-pi-gateway.sh --context devpi5 all
```

The commissioning EG4 and Home Assistant exporter stacks are intentionally excluded from `all`; deploy them explicitly after source-authority preflight.

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
- SolarAssistant and TP-Link/Kasa retain their independent local polling, diagnostics, and shared-outbox forwarding behavior when OTLP is unavailable.

## TP-Link/Kasa deployment notes

- The gateway polls only configured devices with allowlisted read-only commands during normal operation.
- It does not scan continuously and does not execute device commands. It does enqueue and forward energy/inventory payloads through the shared outbox when `KASA_OUTBOX_*` settings are configured.
- Start with one enabled non-critical pilot device in `deploy/pi-gateways/tplink-kasa/.env`.
- Configure vendor `DeviceId` as the primary identity; configure `Host` only as the current locator and `MacAddress` as a secondary validation hint.
- Keep all `KASA_DEVICE_<n>_ENABLED=false` until the device has been explicitly selected for the pilot.
- `/health` and `/gateway-health` are unauthenticated health endpoints; `/inventory` and `/status` require `X-Api-Key: <KASA_LOCAL_API_KEY>`.

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
- Root Compose also requires those mounted files. It overrides only `SmartShunt:CentralIngestBaseEndpoint`, defaulting safely to `http://hvo-website:8080/`; the API key remains exclusively in `secrets/central-ingest-api-key`.
- The Compose project, service name, and `smartshunt-outbox` volume remain unchanged, preserving queued legacy summaries during migration to the shared outbox schema.
- The deployment preflight validates direct authority, source identity, the public-GATT MAC address, shared outbox path/type, and required secret files before SSH synchronization and recreation.
- Follow `docs/gateways/victron-smartshunt.md` for exactly-one-owner cutover, rollback, and the optional bounded `TestCategory=Live` check.

## Recommended workflow

1. Develop website/API changes in the repo devcontainer on `hvo-dev` and validate them against `hvo-docker` when needed.
2. Publish or deploy the website/API update to Azure when the gateway contract is ready.
3. Deploy the gateway container to the Pi with `HVO_WEBSITE_PUBLIC_BASE_URL` pointed at Azure.
4. Use a non-Azure upstream only as a temporary, explicit test configuration.

## Azure rollout sequence

1. Confirm the website Container App can read secrets from `https://hvoobs-kv.vault.azure.net/`.
2. Store the raw API keys in Key Vault using the secret names listed above.
3. Restart or roll a new website revision so startup seeding runs again.
4. Verify the keys against the live Azure website API.
5. Copy only the needed raw gateway key into the Pi gateway `.env` file.

## Current local ports

- Davis UI: `http://<pi-host>:5100`
- JK BMS headless health and protected diagnostics: `http://<pi-host>:5200`
- SolarAssistant UI: `http://<pi-host>:5300`
- SmartShunt headless health and protected diagnostics: `http://<pi-host>:5400`
- TP-Link/Kasa local API: `http://<pi-host>:5500`
- EG4 headless health and protected diagnostics: `http://<pi-host>:5600`
- Home Assistant exporter diagnostics: `http://<pi-host>:5700`

## EG4 deployment notes

- Follow `docs/gateways/eg4/deployment-and-shadow-validation.md` before rollout.
- Copy `gateway.json.example` to ignored `gateway.json`; mount diagnostics, central-ingest, and MQTT credentials as files in the ignored `secrets` directory.
- Map only stable `/dev/hvo/eg4-6500ex-*` HID nodes and the verified MPPT `/dev/serial/by-id/...` path; never map enumerated `/dev/hidrawN` or `/dev/ttyUSBN` names.
- The MPPT serial device remains isolated by default. `EG4_MPPT_0_ENABLED=true` selects `docker-compose.mppt.yml`, which maps only that stable path and permits only the fixed unit-1 function-`0x03` read of registers 200-217.
- The second-inverter overlay remains selected only when `EG4_DEVICE_1_ENABLED=true` and both stable inverter HID nodes exist.
- Production simulation and all command/write paths are disabled. The gateway publishes battery branches, independent MPPT detail, and read-only inverter AC/load/temperature/status detail.
- Direct current state is projected to Home Assistant through MQTT Discovery. MQTT is not the canonical historical-delivery path.
- Set `HVO_CHECK_EG4=true` when `check-deployments.sh` should require EG4 health.
