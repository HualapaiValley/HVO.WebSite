# Shared Infrastructure Deployment

`hvo-docker` hosts two reusable infrastructure stacks that are independent of the website and gateway application deployments.

| Stack | Services | Compose file |
|---|---|---|
| Shared infrastructure | SQL Server, Redis, MinIO, Docker Registry, RabbitMQ | `deploy/hvo-docker/shared-infrastructure/compose.yaml` |
| Observability | OpenTelemetry Collector, Grafana, Prometheus, Loki, Tempo, Promtail, node exporter | `deploy/hvo-docker/observability/compose.yaml` |

Each stack has its own Compose project, network, environment file, and persistent storage. Deploy one stack without deploying the other or any HVO application.

## Configuration Files

Each stack reads only its colocated `.env` file. Create it from the checked-in template before deployment:

```bash
cp deploy/hvo-docker/shared-infrastructure/.env.example deploy/hvo-docker/shared-infrastructure/.env
cp deploy/hvo-docker/observability/.env.example deploy/hvo-docker/observability/.env
```

Do not commit `.env` files. They contain the credentials and host-specific addresses for a deployment. The `.env.example` files are the publishable parameter contract: they contain every supported setting with non-secret placeholders.

## Storage Policy

Stateful services use separate storage locations. Loki and the collector queue use fail-closed bind mounts below `HVO_OBSERVABILITY_DATA_ROOT`; all other stack data uses service-specific named volumes. On `hvo-docker`, `/var/lib/docker` is a required XFS mount on the dedicated 100 GB virtual disk backed by the Proxmox `tank` pool. It is not the root filesystem and has no `nofail` option, so `local-fs.target` and Docker do not start successfully when the disk is unavailable.

Set `HVO_OBSERVABILITY_DATA_ROOT=/var/lib/docker/hvo-observability`. The deploy script verifies that the resolved path is below Docker's data root, resolves to the `/var/lib/docker` XFS mount, exists, and is owned by runtime UID/GID `10001:10001`. Compose uses `bind.create_host_path: false`; missing paths fail rather than silently creating storage on another filesystem.

## Deploying A Stack

```bash
cp deploy/hvo-docker/shared-infrastructure/.env.example deploy/hvo-docker/shared-infrastructure/.env
cp deploy/hvo-docker/observability/.env.example deploy/hvo-docker/observability/.env

./scripts/deploy-shared-stack.sh --context hvo-docker infrastructure
./scripts/deploy-shared-stack.sh --context hvo-docker observability
```

Use `all` only when both stacks are intended:

```bash
./scripts/deploy-shared-stack.sh --context hvo-docker all
```

`HVO_INFRA_BIND_ADDRESS` and `HVO_OBSERVABILITY_BIND_ADDRESS` default to `127.0.0.1`. Set them to the hvo-docker LAN address only for services that must be reachable by gateways or other hosts. In particular, gateways that export telemetry need the collector OTLP port exposed on the LAN address.

Applications can attach to the explicitly named `hvo-infrastructure` or `hvo-observability` Docker network when in-container service discovery is needed. Prefer published host endpoints for services deployed from unrelated Compose projects.

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

Every checked-in service uses Docker's `local` driver with 10 MB per file, three files, compression, non-blocking delivery, and a 4 MB memory buffer. The nominal retained plus transient budget is 34 MB per container. This is 170 MB for the five-service Pi host, 204 MB for the six-service root development stack, and 442 MB for `hvo-docker` when the website, seven observability services, and five shared-infrastructure services all run there. When the 4 MB non-blocking buffer fills, Docker drops new stdout records rather than blocking the application. `docker logs` remains supported; do not read or delete the driver's internal files directly.

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

Safe inspection and validation:

```bash
docker --context hvo-docker logs --since 30m loki
docker --context hvo-docker logs --since 30m otel-collector
curl -fsS http://192.168.1.238:9090/api/v1/alerts
bash tools/verify-local-log-budget.sh
bash tools/verify-log-outage-recovery.sh
```

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

## Verified Live State (2026-08-09)

The existing services are persistent, but they are not currently a pair of neutral reusable stacks:

| Service group | Current Compose ownership | Current data location |
|---|---|---|
| SQL Server | `mssql` | `/data/mssql` bind mount on `/dev/sda1` |
| Registry | `registry` | Docker local volume under `/var/lib/docker` on dedicated XFS `/dev/sdb1` |
| RabbitMQ | `hvo-rabbitmq-poc` | Docker local volume under `/var/lib/docker` on dedicated XFS `/dev/sdb1` |
| OpenTelemetry, Grafana, Prometheus, Loki, Tempo, Promtail | `otel-collector` | Mix of `/opt/otel-collector` configuration and Docker local volumes under `/var/lib/docker` on `/dev/sdb1` |
| MinIO and Redis | `hvo-docker` project owned by SkyMonitor | Docker local volumes under `/var/lib/docker` on dedicated XFS `/dev/sdb1` |

The VM has no literal `/tank` path. Its Docker data root is `/dev/sdb1`, a dedicated XFS virtual disk backed by the Proxmox `tank` pool. `/etc/fstab` requires its UUID at `/var/lib/docker` without `nofail`, and systemd orders the generated mount before `local-fs.target`. SQL Server is separate at `/data/mssql` on `/dev/sdc`. The observability deployment additionally verifies the configured source device, mount, paths, and ownership before starting Loki.

## Migration Rules

Do not run the new stacks beside the live services on the same host ports. They will conflict, and deploying them with empty named volumes will create new empty databases, queues, object storage, registries, and dashboards.

For each service group:

1. Back up the existing persistent data and configuration.
2. Stop only the existing service group during its maintenance window.
3. Restore its persistent data into the matching new named volume, preserving ownership and permissions.
4. Configure the new stack `.env` with newly managed credentials and required LAN bindings.
5. Start the replacement stack, run service-specific health and data checks, then reconnect dependent applications.

Migrate one service group at a time. SQL Server, MinIO, RabbitMQ, and Grafana require application-aware backup/restore or export procedures in addition to filesystem copies.
