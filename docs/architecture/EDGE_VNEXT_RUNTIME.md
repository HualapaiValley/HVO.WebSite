# Edge vNext Runtime

Issue #332 establishes the standard headless runtime used by new HVO edge
executables. Existing UI-bearing gateways remain unchanged until their focused
port and cutover issues.

## Dependency Direction

```text
HVO.Edge.Contracts
  <- HVO.Edge.Outbox
  <- HVO.Edge.Hosting
  <- HVO.Edge.HomeAssistant.Mqtt
  <- vNext device executable
```

- `HVO.Edge.Contracts` owns stable identity, health, diagnostics, and telemetry
  contracts.
- `HVO.Edge.Outbox` owns SQLite registration, initialization, durable queue
  mechanics, forwarding orchestration, retry/dead-letter behavior, compaction,
  and outbox diagnostics.
- `HVO.Edge.Hosting` owns mounted configuration, secret-file resolution, one
  runtime identity, structured logging, standard OpenTelemetry, resilient HTTP,
  health, and protected diagnostics endpoints.
- `HVO.Edge.HomeAssistant.Mqtt` owns the optional, in-memory Home Assistant
  discovery/current-state projection and its broker worker. It references
  Hosting for runtime identity and secret resolution, but has no outbox API.
- Device executables own only protocol acquisition, mapping, and an
  `IEdgeOutboxBatchSender` adapter for their typed central-ingest contract.

Shared edge projects and vNext executables must not reference Razor, Blazor,
MudBlazor, `HVO.WebSite.Themes`, or `HVO.Enterprise.Telemetry.*`.

## Standard Program

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.AddHvoEdgeRuntime();
builder.Services.AddSingleton<IEdgeOutboxBatchSender, DeviceBatchSender>();
builder.Services.AddHostedService<DeviceAcquisitionWorker>();

var app = builder.Build();
app.MapHvoEdgeRuntimeEndpoints();
app.Run();

public partial class Program;
```

Device workers must be registered after `AddHvoEdgeRuntime`. Runtime and outbox
initializers use `IHostedLifecycleService.StartingAsync`, so configuration,
secrets, directories, and SQLite schema are ready before ordinary workers start.

## Filesystem Contract

| Path | Purpose | Mount |
|------|---------|-------|
| `/app/config/gateway.json` | Typed non-secret gateway configuration | Read-only |
| `/app/data/outbox.db` | Durable SQLite outbox | Read-write volume |
| `/run/secrets/*` | One secret value per referenced file | Read-only secret mount |

Production paths must be absolute. The configuration file must remain under the
configured config root, and secret references must be relative file names that
resolve under the secret root. Runtime code creates only the data directory; it
never creates mounted configuration or secret files.

Configuration precedence is application defaults, mounted `gateway.json`, then
environment-variable overrides. `HVO_EDGE_CONFIG_FILE` can select a different
mounted file for commissioning and tests.

## Runtime Identity

`EdgeRuntimeIdentity` is resolved once and supplies the same values to logs,
metrics, traces, and diagnostics:

- service name, version, and instance ID;
- deployment environment and host;
- gateway ID and type;
- site, source, and optional device ID.

Canonical metrics and activities retain the `HVO.Edge` name. Structured JSON
stdout and bounded redacted OTLP logging remain available. Metrics and traces
use the standard OpenTelemetry SDK and OTLP exporter. A missing or unreachable
collector never blocks startup, acquisition, outbox persistence, or forwarding,
and collector reachability is not a liveness dependency.

## Outbox Contract

`AddHvoEdgeOutbox` binds validated `Outbox` settings, registers the standard
SQLite context and store, initializes/updates the schema, and starts the shared
forwarder. The device-specific sender returns one outcome per local record:

- `Sent` for accepted or idempotently skipped observations;
- `TransientFailure` for retryable transport/service failures;
- `PermanentFailure` for invalid payload or unsupported-contract errors. HTTP 401/403 are transient so corrected credential or source-authority configuration can recover without losing durable records.

The shared worker owns attempts, exponential retry, retry exhaustion,
dead-letter state, cancellation-aware sweeps, and daily retention compaction.
Sender exceptions are isolated and scheduled as transient retries rather than
terminating the host.

## HTTP Contract

Public endpoints:

- `GET /health/live`: process liveness only.
- `GET /health`: current readiness/health alias.
- `GET /health/ready`: current readiness/health.

`X-Api-Key` protected endpoints:

- `GET /diagnostics/health`.
- `GET /diagnostics/status`.
- `GET /diagnostics/outbox`.
- `PUT /diagnostics/outbox/settings`.

The API key is loaded from the secret file named by
`Edge:Runtime:DiagnosticsApiKeySecret`. Diagnostic responses expose no secret,
secret path, configuration path, or SQLite path.

## Home Assistant MQTT Contract

Device executables opt in with `AddHvoHomeAssistantMqtt(configuration)` after
registering the edge runtime, then call the synchronous
`IHomeAssistantMqttProjection` methods from acquisition code. Definitions and
current state are held only in memory. State updates are latest-observation-wins;
an update with an equal or older `ObservedAtUtc` is rejected and historical
outbox records are never accepted by this API.

`HomeAssistant:Mqtt` is disabled by default. When enabled it requires broker
host/port, `UsernameSecret` and `PasswordSecret` file names, discovery/topic
prefixes, and initial/maximum reconnect delays bounded to 1-300 seconds. The
secret files are resolved under `Edge:Paths:SecretsDirectory` during startup.
Status and logs never include credential values.

IDs encode configured site, gateway, device, and component UTF-8 bytes into a
collision-safe MQTT/HA identifier and length-prefix compound IDs so distinct
configured identities cannot collapse to one entity or MQTT client. Topic
conventions are:

```text
homeassistant/device/{site_gateway_device}/config
hvo/{site}/{gateway}/availability
hvo/{site}/{gateway}/{device}/availability
hvo/{site}/{gateway}/{device}/state
```

All discovery, state, availability, and removal tombstone messages are retained
at QoS 1. The gateway availability topic is also the retained LWT. On connection
the worker publishes each device's discovery, current state, and availability,
then marks the gateway online. `homeassistant/status=online` triggers one
coalesced snapshot republish. Device removal publishes offline followed by empty
retained discovery/state/availability tombstones. MQTT failures and reconnects
remain inside the hosted worker and cannot block acquisition or outbox delivery.
Command entities, command topics, and command handling are intentionally outside
issue #321.

## Device Authority

| Source | vNext acquisition and canonical writer |
|--------|------------------------------------------|
| EG4 6500EX and MPPT100 | Direct EG4 collector |
| JK BMS | Direct JK collector |
| Davis Vantage Pro 2 | Direct Davis collector |
| SmartShunt | Paired direct public-GATT SmartShunt collector |
| Kasa | Home Assistant Core, exported through the HA exporter |
| Govee | Home Assistant through an ESPHome Bluetooth proxy |
| SolarAssistant | No HVO vNext collector or canonical writer |

No migration may run two acquisition authorities or two central writers for the
same physical observation.

See [Edge Data Flows](EDGE_DATA_FLOWS.md) for the per-source acquisition,
MQTT presentation, outbox, canonical ingest, and migration diagrams.

## Testing Pattern

Every vNext executable exposes `public partial class Program` and uses
`WebApplicationFactory<Program>` for focused non-live integration tests. The
reference project `tests/HVO.Edge.Hosting.TestHost` proves the shared host starts
without UI dependencies and covers endpoint protection, OTLP failure isolation,
worker startup, outbox schema readiness, and identity consistency.

Device-specific tests add protocol simulators and fake batch senders. Physical
BLE, serial, and network-device checks remain explicitly tagged live tests.
