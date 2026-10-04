# Shared Infrastructure Deployment

The repository defines two reusable deployment candidates for `hvo-docker`,
independent of website/gateway application deployments. Checked-in configuration
is not proof that every live service has migrated to these stacks. The
[dated August snapshot](#historical-live-snapshot-2026-08-09) records different
service ownership and an unresolved SQL disk identity. No live state was
re-attested for #415.

| Stack | Services | Compose file |
|---|---|---|
| Shared infrastructure | SQL Server, Redis, MinIO, Docker Registry, RabbitMQ | `deploy/hvo-docker/shared-infrastructure/compose.yaml` |
| Observability | OpenTelemetry Collector, Grafana, Prometheus, Loki, Tempo, Promtail, node exporter | `deploy/hvo-docker/observability/compose.yaml` |

Each candidate has its own project, network, env file and persistent storage.
Launching it with empty stores beside an existing service is not an upgrade.
Follow [migration rules](#migration-rules) and the
[recovery inventory](#recovery-ownership-and-evidence-gaps) before an authorized
service-specific transition. The [website entry](../deploy/hvo-docker/README.md)
owns the current website's separate network/identity/key-ring prerequisites.

## Configuration Files

The deploy helper selects each stack's colocated `.env`; exported shell values
can override Compose interpolation. Initialize only on **first install**. Refuse
existing files/symlinks, preserve operators' credentials, and use restricted modes:

```bash
set -euo pipefail
for stack in shared-infrastructure observability; do
  candidate_env="deploy/hvo-docker/$stack/.env"
  test ! -e "$candidate_env" && test ! -L "$candidate_env"
  (umask 077; set -o noclobber; cat "$candidate_env.example" > "$candidate_env")
done
```

Do not commit `.env` files. They contain the credentials and host-specific addresses for a deployment. The `.env.example` files are the publishable parameter contract: they contain every supported setting with non-secret placeholders.

## Storage Policy

The candidates use separate storage: Loki/collector queues have fail-closed
binds below `HVO_OBSERVABILITY_DATA_ROOT`; other state uses service-specific named
volumes. The recorded `hvo-docker` layout requires an XFS mount at
`/var/lib/docker`, backed by a dedicated 100 GB Proxmox `tank` virtual disk,
without `nofail`. This is a required preflight invariant, not a newly observed
mount. Verify the actual device/UUID/mount before any migration; do not infer SQL's
device from the contradictory historical snapshot.

Set `HVO_OBSERVABILITY_DATA_ROOT=/var/lib/docker/hvo-observability`. The deploy script verifies that the resolved path is below Docker's data root, resolves to the `/var/lib/docker` XFS mount, exists, and is owned by runtime UID/GID `10001:10001`. Compose uses `bind.create_host_path: false`; missing paths fail rather than silently creating storage on another filesystem.

## Deploying A Stack

After first-install initialization, reviewed credentials/storage/network values,
service-specific backups and separate rollout authorization:

```bash
./scripts/deploy-shared-stack.sh --context hvo-docker infrastructure
./scripts/deploy-shared-stack.sh --context hvo-docker observability
```

Use `all` only when both stacks are intended:

```bash
./scripts/deploy-shared-stack.sh --context hvo-docker all
```

`HVO_INFRA_BIND_ADDRESS` and `HVO_OBSERVABILITY_BIND_ADDRESS` default to `127.0.0.1`. Set them to the hvo-docker LAN address only for services that must be reachable by gateways or other hosts. In particular, gateways that export telemetry need the collector OTLP port exposed on the LAN address.

Applications can attach to `hvo-infrastructure` or `hvo-observability` when
in-container discovery is needed. Unrelated projects need explicit connectivity.
`config --quiet` establishes syntax/interpolation validity only; for example,
after choosing a restricted env file, assert expected non-secret infrastructure
binding without printing its environment:

```bash
set -euo pipefail
docker compose --env-file deploy/hvo-docker/shared-infrastructure/.env \
  -f deploy/hvo-docker/shared-infrastructure/compose.yaml config --quiet
docker compose --env-file deploy/hvo-docker/shared-infrastructure/.env \
  -f deploy/hvo-docker/shared-infrastructure/compose.yaml config --format json |
  jq -e '.services.mssql.ports | any(.host_ip == "127.0.0.1" and .target == 1433)' >/dev/null
```

This example asserts the default loopback policy. For an approved LAN exposure,
replace the expected address deliberately and verify firewall/TLS/auth separately.
Do not dump the rendered JSON: service environments contain credentials.

## Shared Infrastructure Parameters

All shared-infrastructure settings are defined in `deploy/hvo-docker/shared-infrastructure/.env.example`.

| Service | Required parameters | Optional parameters | Internal hostname |
|---|---|---|---|
| Shared port binding | None | `HVO_INFRA_BIND_ADDRESS` | N/A |
| SQL Server | `MSSQL_SA_PASSWORD` | `MSSQL_IMAGE`, `MSSQL_PID`, `MSSQL_MEMORY_LIMIT_MB`, `MSSQL_PORT` | `mssql` |
| Redis | `REDIS_PASSWORD` | `REDIS_IMAGE`, `REDIS_PORT` | `redis` |
| MinIO | `MINIO_ROOT_USER`, `MINIO_ROOT_PASSWORD` | `MINIO_IMAGE`, `MINIO_API_PORT`, `MINIO_CONSOLE_PORT` | `minio` |
| Docker Registry | None | `REGISTRY_IMAGE`, `REGISTRY_PORT` | `registry` |
| RabbitMQ | `RABBITMQ_DEFAULT_USER`, `RABBITMQ_DEFAULT_PASS`, `RABBITMQ_ERLANG_COOKIE` | `RABBITMQ_IMAGE`, `RABBITMQ_AMQP_PORT`, `RABBITMQ_MANAGEMENT_PORT`, `RABBITMQ_MQTT_PORT` | `rabbitmq` |

`HVO_INFRA_BIND_ADDRESS` defaults to `127.0.0.1`. Change it to the target host's LAN address only when a client outside Docker must reach a service. The published ports are SQL Server `1433`, Redis `6379`, MinIO API `9000`, MinIO Console `9001`, registry `5000`, RabbitMQ AMQP `5672`, RabbitMQ management `15672`, and RabbitMQ MQTT `1883` unless overridden.

The registry is intentionally configured without authentication or TLS to match the current trusted-network deployment. A public or untrusted-network deployment must add registry authentication and TLS before exposing port `5000`.

## Observability Parameters

All observability settings are defined in `deploy/hvo-docker/observability/.env.example`.

| Service | Required parameters | Optional parameters | Internal hostname |
|---|---|---|---|
| Shared port binding | None | `HVO_OBSERVABILITY_BIND_ADDRESS` | N/A |
| Tank-backed storage | `HVO_OBSERVABILITY_DATA_ROOT` | None | N/A |
| OpenTelemetry Collector | None | `OTEL_COLLECTOR_IMAGE`, `OTEL_GRPC_PORT`, `OTEL_HTTP_PORT`, `OTEL_HEALTH_PORT` | `otel-collector` |
| Prometheus | None | `PROMETHEUS_IMAGE`, `PROMETHEUS_PORT`, `PROMETHEUS_RETENTION` | `prometheus` |
| Loki | None | `LOKI_IMAGE`, `LOKI_PORT` | `loki` |
| Promtail | None | `PROMTAIL_IMAGE` | `promtail` |
| Tempo | None | `TEMPO_IMAGE`, `TEMPO_PORT` | `tempo` |
| Node exporter | None | `NODE_EXPORTER_IMAGE` | `node-exporter` |
| Grafana | `GRAFANA_ADMIN_USER`, `GRAFANA_ADMIN_PASSWORD` | `GRAFANA_IMAGE`, `GRAFANA_PORT` | `grafana` |

`HVO_OBSERVABILITY_BIND_ADDRESS` defaults to `127.0.0.1`. To receive telemetry from remote gateway hosts, bind the collector to the host's LAN address and configure the gateway with an endpoint such as:

```dotenv
OTEL_COLLECTOR_ENDPOINT=http://hvo-docker.example.internal:4318
OTEL_EXPORTER_OTLP_PROTOCOL=http/protobuf
```

The collector accepts OTLP/gRPC on `4317` and OTLP/HTTP on `4318`. Grafana, Prometheus, Loki, and Tempo have no external authentication configured by these base files beyond Grafana's administrator credentials. Put any externally reachable user interface behind an authenticated reverse proxy and TLS.

Gateway applications write structured logs to stdout/stderr and export directly
to the collector when configured. They do not write application-owned files
under `/app/logs`. Promtail observes only containers on the `hvo-docker` Docker
host; it does not scrape remote Pi Docker hosts. Consequently, direct OTLP is
the central log path for Pi gateways, while `docker logs` remains the local
diagnostic path. Docker log rotation, OTLP outage buffering, Loki retention, and
durable Loki storage are deployment responsibilities documented and validated
with the observability stack rather than application sink settings. The website
uses bounded Docker stdout logs plus Promtail on `hvo-docker`; it does not also
export logs directly, which prevents duplicate Loki records.

### Log Budgets And Retention

Checked-in services use Docker's `local` driver with 10 MB per file, three files,
compression, non-blocking delivery and a 4 MB memory buffer: a nominal 34 MB
retained/transient budget per instantiated container. Sum only the services
actually running on the target. Four active direct Pi collectors give a nominal
136 MB; the disabled HA exporter template is not a fifth production service.
This configured bound is not a measured compressed disk footprint. When the
non-blocking buffer fills, new stdout is dropped rather than blocking the app.
Use `docker logs`; do not manipulate the driver's storage files directly.

Pi gateway OTLP sinks hold at most 5,000 events, send batches of at most 256 every two seconds, and retry for at most ten minutes. Events beyond the queue limit or retry window are dropped; the bounded Docker log remains the local diagnostic record. The central collector uses a persistent 64 MiB log queue, retries Loki for up to six hours, and rejects new records when full. Collector enqueue/refusal counters drive the dropped-log alerts.

Loki retains central logs for 720 hours (30 days). Prometheus retains metrics according to `PROMETHEUS_RETENTION`. SQLite outboxes and gateway history volumes are not log storage and must never be included in log cleanup.

### Loki Migration And Recovery

Before the first deployment of the bind-backed layout, use a maintenance window. Discover the existing named volumes by their Compose labels and fail if either source is missing; do not type a volume name directly because Docker creates a new empty volume when a nonexistent name is mounted:

```bash
set -euo pipefail
docker --context hvo-docker stop loki otel-collector
ssh roys@hvo-docker 'sudo install -d -o 10001 -g 10001 /var/lib/docker/hvo-observability/loki /var/lib/docker/hvo-observability/otelcol'
LOKI_SOURCE_VOLUME="$(docker --context hvo-docker volume ls --filter label=com.docker.compose.project=otel-collector --filter label=com.docker.compose.volume=loki_data -q)"
COLLECTOR_SOURCE_VOLUME="$(docker --context hvo-docker volume ls --filter label=com.docker.compose.project=otel-collector --filter label=com.docker.compose.volume=otelcol-storage -q)"
test "$(wc -w <<<"${LOKI_SOURCE_VOLUME}")" -eq 1
test "$(wc -w <<<"${COLLECTOR_SOURCE_VOLUME}")" -eq 1
docker --context hvo-docker volume inspect "${LOKI_SOURCE_VOLUME}" "${COLLECTOR_SOURCE_VOLUME}" >/dev/null
docker --context hvo-docker run --rm -v "${LOKI_SOURCE_VOLUME}:/from:ro" -v /var/lib/docker/hvo-observability/loki:/to alpine:3.22 sh -ceu 'test -n "$(ls -A /from)"; test -z "$(ls -A /to)"; cp -a /from/. /to/'
docker --context hvo-docker run --rm -v "${COLLECTOR_SOURCE_VOLUME}:/from:ro" -v /var/lib/docker/hvo-observability/otelcol:/to alpine:3.22 sh -ceu 'test -z "$(ls -A /to)"; cp -a /from/. /to/'
docker --context hvo-docker run --rm -v "${LOKI_SOURCE_VOLUME}:/from:ro" -v /var/lib/docker/hvo-observability/loki:/to:ro alpine:3.22 sh -ceu 'test "$(find /from -type f | wc -l)" -eq "$(find /to -type f | wc -l)"'
./scripts/deploy-shared-stack.sh --context hvo-docker observability
```

The Loki source must contain data, the destination must be empty before copying, and the post-copy file count must match. Do not delete old volumes until historical Loki queries, collector queue metrics, and a backup are verified. If deployment preflight reports the wrong mount, source device, missing directory, or wrong owner, stop and correct storage; do not bypass the check.

Authorized read-only remote inspection (actual address/access required):

```bash
docker --context hvo-docker logs --since 30m loki
docker --context hvo-docker logs --since 30m otel-collector
curl -fsS http://192.168.1.238:9090/api/v1/alerts
```

Non-live configuration/log-buffer verification uses test-owned local resources:

```bash
bash tools/verify-local-log-budget.sh
bash tools/verify-log-outage-recovery.sh
```

The latter demonstrates bounded log-outage/queue recovery under its local test
conditions. It does not prove a full production host or service restore.

Safe cleanup is limited to normal Docker rotation and Loki retention. Never remove files below `/var/lib/docker` manually. If emergency space recovery is required, stop the observability stack, back it up, and remove only confirmed expired Loki data through Loki-supported retention/deletion procedures. Never target `/app/data`, outbox volumes, Prometheus, Grafana, or Tempo storage.

Prometheus exposes these alert states to Grafana while the observability stack is running. Because Prometheus and node exporter are colocated on `hvo-docker`, they cannot notify during a complete VM, Docker daemon, or required-mount startup failure. Keep the Proxmox host/storage alert for the `tank`-backed VM disk enabled as the out-of-band signal for that failure class.

### Logging And Storage References

The deployment was validated against Docker Engine 29.5.2 and Docker Compose 5.3.1 on `hvo-docker`, OpenTelemetry Collector Contrib 0.152.0, and Loki 3.7.2. The applicable official semantics are:

- [Docker local logging driver](https://docs.docker.com/engine/logging/drivers/local/): `max-size`, `max-file`, compression, rotation, and the warning not to manipulate driver files directly.
- [Docker logging delivery modes](https://docs.docker.com/engine/logging/configure/#configure-the-delivery-mode-of-log-messages-from-container-to-log-driver): `mode=non-blocking`, `max-buffer-size`, and new-record drops when the buffer is full.
- [Docker Compose `up`](https://docs.docker.com/reference/cli/docker/compose/up/): `--wait` waits for services to be running or healthy and fails deployment on timeout.
- [Docker Compose service volume syntax](https://docs.docker.com/reference/compose-file/services/#volumes): long bind syntax with `create_host_path: false` prevents silent host-directory creation.
- [Collector 0.152.0 exporter helper](https://github.com/open-telemetry/opentelemetry-collector/blob/v0.152.0/exporter/exporterhelper/README.md): byte-sized queues, non-blocking overflow rejection, enqueue-failure metrics, persistent queue restart behavior, and bounded retry duration.
- [Collector resilience](https://opentelemetry.io/docs/collector/resiliency/): persistent `file_storage`, full-queue and retry-timeout loss behavior, and queue monitoring metrics.
- [Loki retention](https://grafana.com/docs/loki/latest/operations/storage/retention/): Compactor retention requires `retention_enabled`, a 24-hour TSDB index period, and persistent marker storage. These semantics were also verified by starting the pinned 3.7.2 image with the checked-in configuration.

## Application Connectivity

Containers in the same stack resolve service names through Docker DNS: for example, `mssql:1433`, `redis:6379`, `minio:9000`, `rabbitmq:5672`, and `otel-collector:4318`.

Unrelated Compose projects are not automatically attached to either stack network. They must either join `hvo-infrastructure` or `hvo-observability`, or connect through a published host address and port. The current website deployment uses its legacy external `mssql_default` network and `Server=mssql`; it must be explicitly moved to `hvo-infrastructure` or changed to use the hvo-docker host address before it can consume a newly deployed shared-infrastructure stack.

## Public Repository Boundary

These two stack directories can be moved to a public infrastructure repository together with this guide and `scripts/deploy-shared-stack.sh`. Before doing so:

1. Keep only `.env.example` files, never real `.env` files or copied host configuration.
2. Replace organization-specific hostnames, IP addresses, registry names, and credentials with placeholders.
3. Keep images version-pinned where reproducibility matters; `latest` is convenient but not a release contract.
4. Document the network exposure decision for every published port.
5. Provide service-specific backup and restore procedures before presenting the repository as a production template.

## Historical Live Snapshot (2026-08-09)

Preserved dated observation from this guide's pre-#415 source, not re-attested
current state. The recorded services did not form a pair of neutral reusable
stacks:

| Service group | Recorded Compose ownership | Recorded data location |
|---|---|---|
| SQL Server | `mssql` | `/data/mssql` bind mount on `/dev/sda1` |
| Registry | `registry` | Docker local volume under `/var/lib/docker` on dedicated XFS `/dev/sdb1` |
| RabbitMQ | `hvo-rabbitmq-poc` | Docker local volume under `/var/lib/docker` on dedicated XFS `/dev/sdb1` |
| OpenTelemetry, Grafana, Prometheus, Loki, Tempo, Promtail | `otel-collector` | Mix of `/opt/otel-collector` configuration and Docker local volumes under `/var/lib/docker` on `/dev/sdb1` |
| MinIO and Redis | `hvo-docker` project owned by SkyMonitor | Docker local volumes under `/var/lib/docker` on dedicated XFS `/dev/sdb1` |

The same record's prose said Docker's data root was `/dev/sdb1` on XFS,
backed by `tank`, with its UUID required at `/var/lib/docker` and no literal
`/tank` guest path. It identified SQL at `/data/mssql` on **`/dev/sdc`**, whereas
the table above identified **`/dev/sda1`**. This contradiction is **unresolved**;
neither value is an accepted current source identity. Resolve it with authorized
mount/UUID/storage evidence before migration, retaining the original conflicting
record. The observability deploy preflight checks its own source device, mount,
paths and ownership; those checks do not resolve SQL's disk identity.

## Migration Rules

Do not run the new stacks beside the live services on the same host ports. They will conflict, and deploying them with empty named volumes will create new empty databases, queues, object storage, registries, and dashboards.

For each service group:

1. Back up the existing persistent data and configuration.
2. Stop only the existing service group during its maintenance window.
3. Restore its persistent data into the matching new named volume, preserving ownership and permissions.
4. Configure the new stack `.env` with newly managed credentials and required LAN bindings.
5. Start the replacement stack, run service-specific health and data checks, then reconnect dependent applications.

Migrate one group at a time. SQL Server, MinIO, RabbitMQ and Grafana require
application-aware backups/exports in addition to filesystem copies. The generic
sequence is not a completed service recovery procedure or permission to rotate
credentials. Retain existing stores through verified restore/data checks.

## Recovery ownership and evidence gaps

Assign a named accountable operator for each role before commissioning or
changing the service. The roles below identify the required responsibility;
this repository does not assign a person, backup schedule or recovery objective
by implication. An env/Compose file and a healthy restart are insufficient
backup/restore evidence.

| State / accountable role | Preserve and validate | Existing reference / remaining evidence gap |
|---|---|---|
| Canonical SQL / database owner | Application-aware full/log backups as selected by recovery objectives, schema/migration state, keys/permissions and continuity across delayed ingest | `HVO.DataModels` owns EF migrations. [Disposable SQL fixtures](development/sql-server-integration-tests.md) prove test behavior, not production DR. Service backup, off-host retention, restored data/query and recovery-point drill records are missing from this repository |
| Website Data Protection / website and identity owners | `hvo-website-data-protection`, application name, dedicated runtime identity and unwrap access for every retained protector version; restricted former plaintext/ACA rollback archives | [Key-ring procedure](WEBSITE_DATA_PROTECTION.md) and container verifier exist. Completed off-host payload-unprotect/auth-continuity restore and host-loss drill evidence is not supplied here. Do not delete old wrapping keys/roles on archival status |
| Gateway SQLite / collector and source-authority owners | Full outbox volume including WAL, device/config registries, Davis station/cursor companions, mounted non-secret config and restricted secret recovery references; later observations and central idempotency | [Canonical checkpoint/isolated restore contract](gateways/sqlite-backup-and-rollback.md) includes #414's disposable proof qualification. It is not a new live gateway drill. Preserve [Davis recovery](gateways/davis-vantage-pro2/cutover-and-rollback.md), JK evidence and retired SolarAssistant archive |
| HA Core/apps / HA owner | Supported HA backup for Core, Mosquitto, ESPHome, integrations, managed tree and registry; recheck retained discovery/entity continuity | [HA recovery notes](../deploy/home-assistant/README.md#recovery-notes) retain restore checks. A source-bound full host-loss restore/drill record is missing here |
| HA off-node backups / host/storage owner | The off-node host mount, credentials, schedule and external archive retention, separately from Core/app backup | HA OS restore does not recreate this mount/schedule. Re-add and test them; the actual current schedule/mount/off-node drill evidence is not recorded here |
| Registry / registry owner | Image manifests/blobs, tag/digest inventory and auth/TLS configuration; restore then verify consumers can obtain pinned images | Candidate volume is defined, but service-specific off-host backup/restore and digest-readback drill are missing |
| Redis / service-data owner | Chosen persistence files/configuration and explicit decision whether its actual consumers permit reconstruction | Candidate storage is defined; consumer inventory, persistence policy and restore/reconstruction drill are missing. Do not assume it is disposable cache |
| MinIO / object-storage owner | Application-aware objects, metadata, bucket policy and credentials; verify representative readback | Candidate volume is defined; supported backup/restore procedure and off-host object readback drill are missing |
| RabbitMQ / messaging owner | Definitions, credentials/cookie and appropriate durable queue/message backup policy; consumer/message continuity | Candidate volume is defined; service-specific export/restore and message-continuity drill are missing |
| Grafana / observability owner | Provisioned plus UI-managed dashboards/datasources/alerts and database; restore authenticated access and dashboard references | Provisioning files exist; UI export inventory, backup/restore and drill evidence are missing. Preserve legacy metric aliases until externally stored dashboards are checked |
| Prometheus / observability owner | Configuration/rules and supported TSDB backup as required by retention/recovery objectives | Candidate volume/rules exist; supported snapshot/restore and historical-query drill are missing |
| Loki and Tempo / observability owner | Loki chunks/index/compactor markers, Tempo trace storage, configuration and retention policy | Loki bind-layout migration/outage checks are documented, but are not full off-host Loki/Tempo restore drills; service-specific archive/restore and historical-query evidence are missing |
| Collector queues / observability and host owners | Persistent `file_storage` queues plus exact pipeline/configuration/ownership; restart delivery and overflow/loss accounting | Checked-in persistent queues and bounded outage test exist; full off-host queue restore and loss-reconciliation drill are missing |

Protect source authority during recovery: direct SmartShunt remains the sole
collector/writer until [#352](https://github.com/HualapaiValley/HVO.WebSite/issues/352)
authorizes a parity/ownership migration; do not invent unvalidated private fields.
[#385](https://github.com/HualapaiValley/HVO.WebSite/issues/385) owns permanent
Govee proxy placement/cutover and temporary-bridge retirement.
[#320](https://github.com/HualapaiValley/HVO.WebSite/issues/320) owns the approved
HA-history/source-claim path. Keep the exporter disabled with no production
mappings/claims. Recovering HA or a gateway does not authorize a second central
writer, enabling the exporter or destroying historical recovery archives.
