# Pi Gateway Deployments

Minimal per-gateway Docker Compose deployments for Pi-class edge hosts.

## Website upstream policy

Deployed Pi gateways should normally post to the Azure-hosted `HVO.WebSite` API, not to `hvo-docker`.

Use `hvo-docker` as the upstream only when you are intentionally validating unpublished website/API changes, local infrastructure behavior, or end-to-end development flows before Azure is updated.

Practical default:

- Pi deployment: `HVO_WEBSITE_PUBLIC_BASE_URL=https://hvo-website.calmsand-72a6c5ac.westus.azurecontainerapps.io`
- Local development/integration override: `HVO_WEBSITE_PUBLIC_BASE_URL=http://<hvo-docker-or-dev-host>`

## API key source of truth

Gateway API keys should be stored in Azure Key Vault and loaded by the website at startup through its configured Key Vault provider.

Recommended website secret names in `hvoobs-kv`:

- `Seeding--DavisApiKey`
- `Seeding--BmsApiKey`
- `Seeding--PowerApiKey`
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
- `deploy/pi-gateways/jkbms`
- `deploy/pi-gateways/solarassistant`
- `deploy/pi-gateways/smartshunt`
- `deploy/pi-gateways/tplink-kasa`

Each gateway is deployed independently so Pi rollouts do not depend on the main repo-level compose stack.

## Common workflow

1. Copy `.env.example` to `.env` in the gateway folder.
2. Fill the required host, credential, and API key values.
3. Deploy with the Pi Docker context:

```bash
./scripts/deploy-pi-gateway.sh --context devpi5 davis
./scripts/deploy-pi-gateway.sh --context devpi5 jkbms
./scripts/deploy-pi-gateway.sh --context devpi5 solarassistant
./scripts/deploy-pi-gateway.sh --context devpi5 smartshunt
./scripts/deploy-pi-gateway.sh --context devpi5 tplinkkasa
```

To deploy all gateway stacks from the current checkout:

```bash
./scripts/deploy-pi-gateway.sh --context devpi5 all
```

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
- The Pi deployment passes the configured JK device list through environment variables using `JkBms__Devices__<index>__...` keys.
- Use `JKBMS_HCI_ADAPTER=hci0` as the default unless a specific Pi host proves another adapter is more stable.

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
- JK BMS UI: `http://<pi-host>:5200`
- SolarAssistant UI: `http://<pi-host>:5300`
- SmartShunt UI: `http://<pi-host>:5400`
- TP-Link/Kasa local API: `http://<pi-host>:5500`
