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
./scripts/outbox-maintenance.sh summary solarassistant
./scripts/outbox-maintenance.sh schema jkbms
```

Use the Pi Docker context:

```bash
./scripts/outbox-maintenance.sh --remote --context devpi5 summary smartshunt
./scripts/outbox-maintenance.sh --remote --context devpi5 schema tplinkkasa
```

## Safe Re-Baselining

Archive only after confirming the central API has current data and the outbox contains old sent/failed records or a legacy incompatible schema. Stop the gateway before archiving so SQLite cannot keep writing to the renamed database or race WAL/SHM moves. The script refuses to archive when the target gateway service appears to be running, renames `outbox.db*` files inside the Docker volume, and never deletes them:

```bash
./scripts/outbox-maintenance.sh --remote --context devpi5 archive jkbms
```

Restart the affected gateway after archive so the app creates a fresh shared outbox schema. Preserve archived DB files for postmortem unless explicitly approved for deletion.

## Schema Compatibility

Startup now validates the SQLite `OutboxRecords` table after initialization. Missing shared columns or legacy `NOT NULL` columns without defaults fail startup clearly instead of allowing repeated enqueue failures such as `NOT NULL constraint failed: OutboxRecords.DeviceAddress`.

## Follow-Up Work

The remaining hardening work is to migrate all gateway-specific diagnostic endpoints to the standard `/diagnostics/*` routes and consolidate telemetry tags/metric names into shared gateway telemetry helpers.
