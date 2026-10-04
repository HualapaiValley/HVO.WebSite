# Gateway Operations

Current source-backed runbook for Davis, JK BMS, EG4 and SmartShunt. Commands
against `devpi5`, `hvo-docker`, HA or Azure require applicable operational
authorization; routine repository validation uses disposable fixtures instead.

## Health Contract

The [shared endpoint implementation](../src/HVO.Edge.Hosting/Diagnostics/EdgeDiagnosticsEndpointRouteBuilderExtensions.cs)
has this contract. Successful liveness does not establish acquisition, outbox or
central-delivery readiness.

| Endpoint | Purpose | HTTP / authentication |
|---|---|---|
| `GET /health/live` | Process liveness | 200 while the endpoint can respond; public |
| `GET /health`, `GET /health/ready` | Shared gateway health/readiness | 503 only for `Critical`; `Unknown`, `Healthy` and `Warning` return 200; public |
| `GET /diagnostics/health` | Same health evaluation | Separate diagnostics `X-Api-Key`; same health status mapping |
| `GET /diagnostics/status` | Identity, runtime, health, devices, outbox, telemetry and external-delivery snapshots | Separate diagnostics `X-Api-Key` |
| `GET /diagnostics/outbox` | Queue/failure/schema/maintenance snapshot | Separate diagnostics `X-Api-Key` |
| `PUT /diagnostics/outbox/settings` | Mutates in-memory batch/sweep overrides | Separate diagnostics `X-Api-Key`; explicit tuning authorization |

Missing or incorrect diagnostics credentials return **403**, as implemented by
the [authorization filter](../src/HVO.Edge.Hosting/Diagnostics/EdgeDiagnosticsAuthorizationFilter.cs).
The file named by `Edge:Runtime:DiagnosticsApiKeySecret` is distinct from
`central-ingest-api-key`. Website ingest uses its own authentication/scope/source
rules; do not infer those responses from local diagnostics. There is no shared
`/diagnostics/devices` route: devices are in `/diagnostics/status`.

| Checked-in Pi Compose target | Probe | Interpretation |
|---|---|---|
| [Davis](../deploy/pi-gateways/davis/docker-compose.yml) | `/health` | Shared readiness |
| [EG4](../deploy/pi-gateways/eg4/docker-compose.yml) | `/health` | Shared readiness |
| [JK BMS](../deploy/pi-gateways/jkbms/docker-compose.yml) | `/health/live` | Liveness only |
| [SmartShunt](../deploy/pi-gateways/smartshunt/docker-compose.yml) | `/health/live` | Liveness only |
| [HA exporter template](../deploy/pi-gateways/home-assistant-exporter/docker-compose.yml) | `/health` | Candidate readiness; exporter disabled in production |
| [Website](../deploy/hvo-docker/docker-compose.yml) | `/health/live` | Website liveness; website readiness is its separate database-check contract |

Required diagnostic response fields for every gateway are:

- gateway id/type/version
- process uptime/runtime
- device/source freshness
- configured, online, degraded, and offline device counts
- outbox pending, sent, and failed counts by failure kind
- last successful forward timestamp
- last forward error and failure kind
- local schema compatibility/errors/warnings and maintenance state
- telemetry/exporter configuration state without secrets

## Devcontainer Tooling

The devcontainer image includes `sqlite3`, `jq`, Docker CLI support, SSH, and JSON/search tooling. The `azure-cli` devcontainer feature installs Azure CLI, and `post-create.sh` enables non-interactive Azure extension install and installs/updates the `log-analytics` extension used for App Insights and Log Analytics queries.

## Current Incident Investigation

Start with bounded Docker logs and the protected status/outbox snapshot. Correlate
UTC time, service and source identity, observation freshness, queue age and last
forward result. The [sender/retry matrix](gateways/common-gateway-standards.md#sender-http-outcome-and-recovery-matrix)
determines whether fixing credentials can recover rows automatically.

The [checked-in collector](../deploy/hvo-docker/observability/config/otelcol-config.yaml)
routes gateway logs to Loki's `/otlp`, traces to Tempo and metrics to its
Prometheus exporter on port 8889. Grafana datasource UIDs are `loki`, `tempo` and
`prometheus`; Prometheus scrapes `otel-collector:8889` as `hvo-edge-metrics`.
These are configuration facts, not a new observation of live telemetry.

In Grafana Explore, select Loki and start with a bounded selector such as
`{service_name="hvo-jkbms"}` for direct OTLP logs. `service.name` is supplied by
the gateway runtime; [Loki's default OTLP mapping](https://grafana.com/docs/loki/latest/send-data/otel/)
exposes `service_name`. For
central-host stdout collected by Promtail, use its configured `container` label
instead. Use Tempo for the same service/time range and Prometheus for
`up{job="hvo-edge-metrics"}` before selecting actual exported instrument names.
Resource labels and metric normalization depend on the sink; inspect available
values instead of assuming source-level names are literal PromQL names. See
[OTEL integration](OTEL_COLLECTOR_INTEGRATION.md) for signal configuration and
[shared infrastructure](SHARED_INFRASTRUCTURE.md) for alert/storage recovery.

### Historical or optional Azure sink

Only when the incident's actual deployment exports to the specified Azure
workspace, the retained query helper can inspect that sink:

```bash
./scripts/query-gateway-operations.sh --workspace <workspace-id> --hours 2
```

The script summarizes gateway ingest requests, trace severity, outbox/SQLite
errors and health/status signals. Azure CLI/workspace access is a prerequisite.
It is not the current Pi gateway central-log path.

## Outbox Inspection

Before any maintenance helper, inspect the intended existing volume and verify
its Compose ownership; a mistyped Docker volume mount can create an empty volume.
Use the local Docker context only for local resources:

```bash
./scripts/outbox-maintenance.sh schema jkbms
```

Use the Pi Docker context:

```bash
./scripts/outbox-maintenance.sh --remote --context devpi5 summary smartshunt
./scripts/outbox-maintenance.sh --remote --context devpi5 summary eg4
```

The retired direct SolarAssistant and TP-Link/Kasa services are not valid maintenance targets. Their Docker volumes have been removed. The checksum-verified SolarAssistant archive under `/home/roys/backups/hvo-issue-330` on `devPi5` is historical recovery evidence, not an active outbox maintenance target.

`summary` and `schema` inspect the selected SQLite store; `archive` and `compact`
mutate it and require a stopped target plus an approved checkpoint. There is no
maintenance `requeue` command. Failed-row handling follows the
[canonical sender/recovery contract](gateways/common-gateway-standards.md#requeue-rules).

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

New cutover, maintenance and rollback checkpoints follow the
[canonical SQLite backup contract](gateways/sqlite-backup-and-rollback.md): prove
quiescence, preserve the current volume, stream a full archive to a named durable
operator-workstation path, verify checksum/database integrity and demonstrate an
isolated disposable restore. A helper's tar listing or compaction backup alone
does not qualify the full rollback contract. Preserve gateway metadata/companion
volumes and post-checkpoint observations, and reconcile central history before
enabling a restored older writer. Remote bind paths belong to the Docker daemon
host; the canonical recipe uses streams so backup paths belong to the workstation.

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

Central recovery, alerts and missing disaster-recovery procedures are documented
in [shared infrastructure](SHARED_INFRASTRUCTURE.md#recovery-ownership-and-evidence-gaps).
`HvoLogExporterSendFailures` indicates retrying, while `HvoLogRecordsDropped`
indicates confirmed data loss and requires incident review.

## EG4 6500EX

The EG4 gateway uses stable `/dev/hvo` USB HID mappings and, when explicitly enabled, a stable MPPT `/dev/serial/by-id/...` mapping. It has a dedicated `eg4_eg4-outbox` volume and exposes headless health/diagnostics on port 5600. Its `/health` result includes device connectivity and forwarding state. Detailed `/diagnostics/status` (including devices) and `/diagnostics/outbox` require the separate diagnostics `X-Api-Key`.

Use `docs/gateways/eg4/deployment-and-shadow-validation.md` for USB identity, secret-safe Compose validation, commissioning, comparison criteria, and volume-preserving rollback. The EG4 stack is read-only: never add PI30 setters or any Modbus function other than the fixed MPPT function-`0x03` inquiry.

## Davis Weather Underground

Weather Underground publication is an independent, best-effort current-state projection from the Davis collector's latest merged LOOP reading. It does not poll the console, use the SQLite outbox, replay failures, or affect canonical HVO history. Production enablement uses station `KAZKINGM12`, a five-second rapid-fire cadence, and a two-second request timeout so two bounded attempts plus backoff fit inside the cadence.

Keep it disabled for the first deployment. After the disabled collector is healthy, set `WeatherUnderground.Enabled=true` only in the ignored local Davis `gateway.json`, establish the [existing root-bootstrap and global sync prerequisites](development/key-vault-materialization.md), then run the separately approved `./scripts/sync-secrets-from-keyvault.sh --apply`, perform the Davis dry run, deploy, and verify diagnostics plus Weather Underground freshness. For Weather Underground, the sync script retrieves only `WeatherUnderground--StationKey`; it never retrieves the unrelated `WeatherUnderground--ApiKey`. It also processes its other global materializations. Both the local config and `secrets/` are ignored, and the existing `/run/secrets` mount stays read-only.

Diagnostics must expose only enabled state, last observation/attempt/success UTC, consecutive failures, and sanitized last error. Never log the full PWS URI because the station key is carried as the `PASSWORD` query parameter. On failure or rollback, disable the section and redeploy; LOOP collection, MQTT, and outbox forwarding must continue unchanged. See `docs/gateways/davis-vantage-pro2/weather-underground-deployment.md` for mapping, protocol/rate evidence, validation commands, rollout, verification, and key-rotation response.

## Schema Compatibility

Startup now validates the SQLite `OutboxRecords` table after initialization. Missing shared columns or legacy `NOT NULL` columns without defaults fail startup clearly instead of allowing repeated enqueue failures such as `NOT NULL constraint failed: OutboxRecords.DeviceAddress`.

## Follow-Up Work

Promtail is pinned for the existing `hvo-docker` Docker discovery path but is end-of-life. Migrate that central-host-only path to Grafana Alloy in a focused infrastructure change; do not install a second unbounded shipper on Pi gateways.
