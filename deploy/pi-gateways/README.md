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
- `Seeding--PowerReadApiKey`

Reason:

- The website loads Azure Key Vault into configuration at startup.
- ASP.NET Core configuration maps `--` in Key Vault secret names to `:` in configuration keys.
- `ApiKeySeedService` reads `Seeding:DavisApiKey`, `Seeding:BmsApiKey`, `Seeding:PowerApiKey`, and `Seeding:PowerReadApiKey`.

Current local raw keys already present in the dev environment:

- `DAVIS_API_KEY` for `ingest:weather`
- `BMS_API_KEY` for `ingest:bms`

New power keys generated for rollout:

- `POWER_API_KEY` for `ingest:power`
- `POWER_READ_API_KEY` for `read:power`

Important:

- The database stores only key hashes, not the raw values.
- After a raw key is lost, it cannot be recovered from SQL.
- Keep the raw values only in approved secret stores and deployment env files.

## Current targets

- `deploy/pi-gateways/davis`
- `deploy/pi-gateways/jkbms`
- `deploy/pi-gateways/solarassistant`

Each gateway is deployed independently so Pi rollouts do not depend on the main repo-level compose stack.

## Common workflow

1. Copy `.env.example` to `.env` in the gateway folder.
2. Fill the required host, credential, and API key values.
3. Deploy with the Pi Docker context:

```bash
docker --context devpi5 compose --env-file deploy/pi-gateways/davis/.env -f deploy/pi-gateways/davis/docker-compose.yml up -d --build
docker --context devpi5 compose --env-file deploy/pi-gateways/jkbms/.env -f deploy/pi-gateways/jkbms/docker-compose.yml up -d --build
docker --context devpi5 compose --env-file deploy/pi-gateways/solarassistant/.env -f deploy/pi-gateways/solarassistant/docker-compose.yml up -d --build
```

## Current telemetry status

- Davis supports OTLP export, but the Pi-reachable collector endpoint is not resolved yet. Leave `OTEL_COLLECTOR_ENDPOINT` blank for the first Pi rollout.
- JK BMS supports OTLP export with the same conditional endpoint wiring used by Davis. Leave `OTEL_COLLECTOR_ENDPOINT` blank until the Pi should emit to a reachable collector.
- SolarAssistant does not currently wire OpenTelemetry exporters in the app, so OTEL environment variables are intentionally not included here yet.

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
