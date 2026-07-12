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
hvo-smartshunt
hvo-solarassistant
hvo-tplinkkasa
```

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

## Serilog OTLP logs

The Serilog OTLP sink needs the logs signal path explicitly when configured with
HTTP/Protobuf:

```csharp
var otlpEndpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT");

if (!string.IsNullOrWhiteSpace(otlpEndpoint))
{
    loggerConfig.WriteTo.OpenTelemetry(options =>
    {
        options.Endpoint = otlpEndpoint.TrimEnd('/') + "/v1/logs";
        options.Protocol = OtlpProtocol.HttpProtobuf;
    });
}
```

Do not append `/v1/logs` to `OTEL_EXPORTER_OTLP_ENDPOINT`; it must remain the
base endpoint for the OpenTelemetry SDK trace and metric exporters.

## Collector routing

The collector enriches all received telemetry with:

```text
deployment.environment = hvo-production
service.namespace      = haulapai-valley-observatory
```

It routes telemetry as follows:

| Signal | Destination |
| --- | --- |
| Traces | Tempo |
| Metrics | Prometheus |
| Logs | Loki |

The collector no longer exports to Azure Monitor or Application Insights. Do
not configure Application Insights connection strings in applications solely
for this collector pipeline.

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