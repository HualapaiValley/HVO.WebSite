# Gateway Operations Hardening

## Health Contract

Gateway Docker health checks call `/health`. For gateway services, ASP.NET health status now maps `Healthy` to HTTP 200 and both `Degraded` and `Unhealthy` to HTTP 503 so Docker health reflects readiness/degradation instead of process liveness only.

Protected local diagnostic endpoints should use `X-Api-Key` with the configured local or outbox API key. Existing endpoint names still vary by gateway; future endpoint migrations should standardize on `/diagnostics/health`, `/diagnostics/status`, `/diagnostics/outbox`, and `/diagnostics/devices` while preserving the same auth behavior.

Required diagnostic response fields for every gateway are:

- gateway id/type/version
- process uptime/runtime
- device/source freshness
- configured, online, degraded, and offline device counts
- outbox pending, sent, and failed counts by failure kind
- last successful forward timestamp
- last forward error and failure kind
- local DB schema/version/maintenance state
- telemetry/exporter configuration state without secrets

## Devcontainer Tooling

The devcontainer image includes `sqlite3`, `jq`, Docker CLI support, SSH, and JSON/search tooling. The `azure-cli` devcontainer feature installs Azure CLI, and `post-create.sh` enables non-interactive Azure extension install and installs/updates the `log-analytics` extension used for App Insights and Log Analytics queries.

## App Insights Queries

Use:

```bash
./scripts/query-gateway-operations.sh --workspace <workspace-id> --hours 2
```

The script summarizes gateway ingest requests, trace severity, outbox/SQLite errors, and health/status signals. It does not print secrets or connection strings.

## Outbox Inspection

Use local Docker context:

```bash
./scripts/outbox-maintenance.sh schema jkbms
```

Use the Pi Docker context:

```bash
./scripts/outbox-maintenance.sh --remote --context devpi5 summary smartshunt
./scripts/outbox-maintenance.sh --remote --context devpi5 summary eg4
```

The retired direct SolarAssistant and TP-Link/Kasa services are not valid maintenance targets. SolarAssistant's old outbox and data-protection volumes remain preserved pending an explicit archive/delete decision; do not mutate or remove them as routine outbox maintenance.

Outboxes are delivery queues, not historical databases. Active vNext gateways
retain delivered rows for one day, retain failed rows for their configured
intervention window, and never age-purge pending rows. `summary` reports SQLite
page/free-page accounting and auto-vacuum mode in addition to queue counts.

Existing databases created before incremental auto-vacuum require a one-time
controlled compaction. Stop only the target gateway, then run:

```bash
./scripts/outbox-maintenance.sh --remote --context devpi5 compact <gateway>
```

The command refuses a running gateway, streams a verified backup to
`artifacts/outbox/`, checks database integrity before and after compaction, and
never deletes or recreates the named Docker volume. Restart the gateway and
verify health, outbox drainage, and central continuity afterward.

## Safe Re-Baselining

Archive only after confirming the central API has current data and the outbox contains old sent/failed records or a legacy incompatible schema. Stop the gateway before archiving so SQLite cannot keep writing to the renamed database or race WAL/SHM moves. The script refuses to archive when the target gateway service appears to be running, renames `outbox.db*` files inside the Docker volume, and never deletes them:

```bash
./scripts/outbox-maintenance.sh --remote --context devpi5 archive jkbms
```

Restart the affected gateway after archive so the app creates a fresh shared outbox schema. Preserve archived DB files for postmortem unless explicitly approved for deletion.

## Local Logs And Core Dumps

Gateway Compose files use Docker's `local` logging driver with a 30 MB compressed rotation budget and 4 MB non-blocking buffer per container. Core dumps are disabled with soft/hard limits of zero. Deployment scripts inspect the created containers and fail when either policy is absent.

```bash
docker --context devpi5 inspect <container> --format '{{json .HostConfig.LogConfig}} {{json .HostConfig.Ulimits}}'
docker --context devpi5 logs --since 30m <container>
```

Do not enter Docker's log storage directory or delete driver files. Do not run broad Docker volume cleanup: `/app/data` volumes contain SQLite outboxes and device registries. A prolonged OTLP outage may exhaust the 5,000-event application queue; newer central log records are then dropped while bounded local Docker logs remain available.

Central recovery and alert handling are documented in `docs/SHARED_INFRASTRUCTURE.md`. `HvoLogExporterSendFailures` indicates retrying, while `HvoLogRecordsDropped` indicates confirmed data loss and requires incident review.

## EG4 6500EX

The EG4 gateway uses stable `/dev/hvo` USB HID mappings and, when explicitly enabled, a stable MPPT `/dev/serial/by-id/...` mapping. It has a dedicated `eg4_eg4-outbox` volume and exposes headless health/diagnostics on port 5600. Its `/health` result includes device connectivity and forwarding state. Detailed `/diagnostics/status`, `/diagnostics/devices`, and `/diagnostics/outbox` endpoints require the configured `X-Api-Key`.

Use `docs/gateways/eg4/deployment-and-shadow-validation.md` for USB identity, secret-safe Compose validation, commissioning, comparison criteria, and volume-preserving rollback. The EG4 stack is read-only: never add PI30 setters or any Modbus function other than the fixed MPPT function-`0x03` inquiry.

## Schema Compatibility

Startup now validates the SQLite `OutboxRecords` table after initialization. Missing shared columns or legacy `NOT NULL` columns without defaults fail startup clearly instead of allowing repeated enqueue failures such as `NOT NULL constraint failed: OutboxRecords.DeviceAddress`.

## Follow-Up Work

Promtail is pinned for the existing `hvo-docker` Docker discovery path but is end-of-life. Migrate that central-host-only path to Grafana Alloy in a focused infrastructure change; do not install a second unbounded shipper on Pi gateways.
