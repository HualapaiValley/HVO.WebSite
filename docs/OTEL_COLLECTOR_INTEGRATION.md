# OTEL Collector Integration

Use this guide when configuring another HVO application or gateway to emit
OpenTelemetry data to the on-premises observability stack.

## Collector

| Setting | Value |
| --- | --- |
| Collector host | `hvo-docker` (`192.168.1.238`) |
| OTLP HTTP/Protobuf endpoint | `http://192.168.1.238:4318` |
| OTLP gRPC endpoint | `http://192.168.1.238:4317` |
| Protocol used by existing .NET gateways | `http/protobuf` |
| Network scope | HVO local network; no application-level OTLP authentication is configured |

Pass the base endpoint, without a signal path, to standard OpenTelemetry SDK
exporters. The SDK appends `/v1/traces` and `/v1/metrics` for OTLP/HTTP.

## Required Docker environment

Add these settings to the application's Compose service. Use a stable,
lowercase service name that identifies the application rather than a machine,
container, or deployment instance.

```yaml
environment:
  OTEL_SERVICE_NAME: hvo-<service-name>
  OTEL_EXPORTER_OTLP_ENDPOINT: http://192.168.1.238:4318
  OTEL_EXPORTER_OTLP_PROTOCOL: http/protobuf
```

Existing service names:

```text
hvo-website
hvo-davis
hvo-jkbms
hvo-eg4
hvo-smartshunt
```

The retired direct `hvo-solarassistant` and `hvo-tplinkkasa` services are no longer telemetry producers. `HVO.Edge.Exporter.HomeAssistant` is implemented but intentionally disabled in production, so it has no active production service telemetry.

Use the same convention for new services, for example `hvo-skymonitor` or
`hvo-camera`.

## .NET traces and metrics

Reference the OpenTelemetry packages appropriate to the application, then add
the OTLP exporters. `AddOtlpExporter()` reads the environment variables above.

```csharp
services.AddOpenTelemetry()
    .WithTracing(builder => builder.AddOtlpExporter())
    .WithMetrics(builder => builder.AddOtlpExporter());
```

Register custom `ActivitySource` and `Meter` names with the tracing and metrics
providers so application-specific telemetry is not omitted. The existing HVO
services use the `HVO.Enterprise.Telemetry` helpers and add their custom meter
names through that configuration.

All gateways register the canonical `HVO.Edge` meter and activity source. Common
instruments use the names, units, and bounded tags documented in
`docs/gateways/common-gateway-standards.md`; physical battery, weather, and power
measurements remain in typed payloads rather than operational metrics. Existing
gateway-specific meter names are compatibility aliases for externally stored
Grafana dashboards and have a documented removal gate.

## Gateway logs

Gateway hosts use `HVO.Edge.Hosting` and `UseHvoGatewayLogging(...)` rather than
registering Serilog sinks independently:

```csharp
builder.Host.UseHvoGatewayLogging(new GatewayLogIdentity(
    "hvo-example",
    "example",
    "device-type"));
```

The shared bootstrap writes compact structured JSON to stdout and conditionally
exports logs over OTLP. It appends `/v1/logs` to the generic base endpoint for
HTTP/Protobuf without changing the base value used by traces and metrics. Set
`OTEL_EXPORTER_OTLP_LOGS_ENDPOINT` only when logs require a complete,
signal-specific endpoint. OTLP log export is disabled by default in the
`Testing` environment.

Gateway applications do not create local `.log` files or `/app/logs`. Docker
stdout/stderr is the authoritative short-term local log source (`docker logs`),
and Loki is the authoritative central destination queried through Grafana.
Docker log rotation, central retention, and outage buffering are operations
settings rather than application file sinks.

Gateway OTLP logging is explicitly bounded to 5,000 queued events, 256 events
per batch, and ten minutes of retry time. Docker's `local` driver retains at
most three compressed 10 MB files per container and uses a 4 MB non-blocking
buffer. Queue or buffer overflow drops logs instead of blocking a gateway.

Every gateway event includes `service.name`, `service.version`,
`service.instance.id`, `deployment.environment.name`, `host.name`,
`hvo.gateway.id`, and `hvo.gateway.type`. A host also supplies
`hvo.source.id` and `hvo.device.id` when one stable source or device applies.
Activity-backed events include trace and span identifiers. Credentials and
other sensitive named properties are redacted, and response bodies are not
written to normal gateway logs.

## Collector routing

Applications provide their resource identity before export. The checked-in
collector batches and routes telemetry; it does not add deployment or service
namespace attributes. It routes telemetry as follows:

| Signal | Destination |
| --- | --- |
| Traces | Tempo |
| Metrics | Prometheus |
| Logs | Loki |

The collector no longer exports to Azure Monitor or Application Insights. Do
not configure Application Insights connection strings in applications solely
for this collector pipeline.

The collector persists a 64 MiB log queue on tank-backed storage and retries
Loki for up to six hours. Loki retains logs for 30 days. Prometheus alerts on
send failure, queue saturation, rejected records, Loki/Promtail unavailability,
the required XFS mount, and 15%/5% free-space thresholds.

## Observability interfaces

| Interface | URL | Purpose |
| --- | --- | --- |
| Grafana | `http://192.168.1.238:3000` | Primary UI for metrics, logs, and traces |
| Prometheus | `http://192.168.1.238:9090` | Metrics query and target diagnostics |
| Loki | `http://192.168.1.238:3100` | Log API; normally queried through Grafana |

Tempo is intentionally exposed only on the `hvo-docker` host and is accessed by
Grafana through the collector network.

## Validation

After deployment, validate the gateway network path from its Docker host:

```sh
curl --connect-timeout 5 --max-time 10 --silent --output /dev/null \
  --write-out 'OTLP HTTP: %{http_code}\n' \
  -X POST -H 'Content-Type: application/x-protobuf' --data-binary '' \
  http://192.168.1.238:4318/v1/metrics
```

An HTTP `200` confirms that the receiver is reachable. It does not submit a
telemetry payload. Confirm actual application telemetry in Grafana by filtering
on `service.name = hvo-<service-name>`.

## Operational notes

The observability Compose project is located on `hvo-docker` at
`/opt/otel-collector`. Its services use `restart: unless-stopped`; no additional
restart configuration is required in sending applications.

The collector health endpoint is available only on the host:

```text
http://127.0.0.1:13133/
```

Run `bash tools/validate-observability-policy.sh` for static policy validation,
`bash tools/verify-local-log-budget.sh` for a production-sized failure storm, and
`bash tools/verify-log-outage-recovery.sh` to verify persistent queue recovery,
timestamps, and ordering without touching the deployed stack.
