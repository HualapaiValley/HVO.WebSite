# Shared Infrastructure Deployment

`hvo-docker` hosts two reusable infrastructure stacks that are independent of the website and gateway application deployments.

| Stack | Services | Compose file |
|---|---|---|
| Shared infrastructure | SQL Server, Redis, MinIO, Docker Registry, RabbitMQ | `deploy/hvo-docker/shared-infrastructure/compose.yaml` |
| Observability | OpenTelemetry Collector, Grafana, Prometheus, Loki, Tempo, Promtail | `deploy/hvo-docker/observability/compose.yaml` |

Each stack has its own Compose project, network, environment file, and persistent Docker volumes. Deploy one stack without deploying the other or any HVO application.

## Configuration Files

Each stack reads only its colocated `.env` file. Create it from the checked-in template before deployment:

```bash
cp deploy/hvo-docker/shared-infrastructure/.env.example deploy/hvo-docker/shared-infrastructure/.env
cp deploy/hvo-docker/observability/.env.example deploy/hvo-docker/observability/.env
```

Do not commit `.env` files. They contain the credentials and host-specific addresses for a deployment. The `.env.example` files are the publishable parameter contract: they contain every supported setting with non-secret placeholders.

## Storage Policy

Both Compose files declare separate named volumes for each stateful service. Docker stores those volumes under `/var/lib/docker/volumes`, so their durability follows the filesystem hosting Docker's data root. Keep the `.env` files off version control; they contain service credentials.

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
| OpenTelemetry Collector | None | `OTEL_COLLECTOR_IMAGE`, `OTEL_GRPC_PORT`, `OTEL_HTTP_PORT`, `OTEL_HEALTH_PORT` | `otel-collector` |
| Prometheus | None | `PROMETHEUS_IMAGE`, `PROMETHEUS_PORT`, `PROMETHEUS_RETENTION` | `prometheus` |
| Loki | None | `LOKI_IMAGE`, `LOKI_PORT` | `loki` |
| Promtail | None | `PROMTAIL_IMAGE` | `promtail` |
| Tempo | None | `TEMPO_IMAGE`, `TEMPO_PORT` | `tempo` |
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
with the observability stack rather than application sink settings.

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

## Verified Live State (2026-07-17)

The existing services are persistent, but they are not currently a pair of neutral reusable stacks:

| Service group | Current Compose ownership | Current data location |
|---|---|---|
| SQL Server | `mssql` | `/data/mssql` bind mount on `/dev/sda1` |
| Registry | `registry` | Docker local volume under `/var/lib/docker` on dedicated XFS `/dev/sdc1` |
| RabbitMQ | `hvo-rabbitmq-poc` | Docker local volume under `/var/lib/docker` on dedicated XFS `/dev/sdc1` |
| OpenTelemetry, Grafana, Prometheus, Loki, Tempo, Promtail | `otel-collector` | Mix of `/opt/otel-collector` configuration and Docker local volumes under `/var/lib/docker` on `/dev/sdc1` |
| MinIO and Redis | `hvo-docker` project owned by SkyMonitor | Docker local volumes under `/var/lib/docker` on `/dev/sdc1` |

The VM has no `/tank` path. Its Docker data root is a dedicated `/dev/sdc1` XFS mount, so the Docker-managed volumes are already separate and persistent. SQL Server is the exception: it currently uses `/data/mssql` on `/dev/sda1`. The new bundles use distinct named Docker volumes for every stateful service, but do not migrate or alter running services.

## Migration Rules

Do not run the new stacks beside the live services on the same host ports. They will conflict, and deploying them with empty named volumes will create new empty databases, queues, object storage, registries, and dashboards.

For each service group:

1. Back up the existing persistent data and configuration.
2. Stop only the existing service group during its maintenance window.
3. Restore its persistent data into the matching new named volume, preserving ownership and permissions.
4. Configure the new stack `.env` with newly managed credentials and required LAN bindings.
5. Start the replacement stack, run service-specific health and data checks, then reconnect dependent applications.

Migrate one service group at a time. SQL Server, MinIO, RabbitMQ, and Grafana require application-aware backup/restore or export procedures in addition to filesystem copies.
