# RabbitMQ and Service Bus Ingest Plan

Last updated: 2026-05-23

This document defines the proof-of-concept path for moving HVO telemetry ingest from per-collector SQLite outboxes and website API POSTs to a brokered edge/cloud pipeline.

## Goal

Standardize all observatory telemetry on one ingest architecture:

```text
Local devices and collectors
  -> MQTT or AMQP
  -> RabbitMQ on hvo-docker
  -> durable canonical queues
  -> RabbitMQ shovel
  -> Azure Service Bus namespace hvoobs
  -> Azure Functions
  -> Azure SQL v9 schema
  -> SignalR update events
  -> HVO.WebSite read/admin UI
```

The website remains the dashboard, read API, admin surface, and future command/control surface. Telemetry ingest no longer has to pass through website controllers once the POC is proven.

## Design Principles

| Principle | Baseline |
|-----------|----------|
| One ingest path | Prefer RabbitMQ -> Service Bus -> Functions for all telemetry sources |
| Local durability | RabbitMQ durable queues are the local outbox |
| Cloud durability | Azure Service Bus handles cloud retry and dead-lettering |
| Canonical messages | Shovel HVO-shaped messages, not every raw vendor topic |
| Direct canonical publishers | Davis and JK BMS should publish complete HVO messages directly |
| Normalizers only when needed | SolarAssistant and ESPHome may need local source-specific normalizers |
| Website role | Read/admin/commands, not required telemetry ingress |
| Idempotent persistence | Functions must safely handle duplicate delivery |

## Existing Resources

| Resource | Value |
|----------|-------|
| Azure resource group | `observatory-rg` |
| Service Bus namespace | `hvoobs` |
| Service Bus tier | Standard |
| Service Bus endpoint | `hvoobs.servicebus.windows.net` |
| Service Bus ingest topic | `hvo-ingest` |
| Local RabbitMQ host | `hvo-docker` |

The `hvo-ingest` topic, schema-filtered subscriptions, and send-only `rabbitmq-shovel-send` policy are created by `scripts/configure-servicebus-ingest.sh`.

Current POC status:

| Step | Status |
|------|--------|
| Azure Service Bus topic/subscriptions | Created |
| RabbitMQ container on `hvo-docker` | Running |
| RabbitMQ canonical topology | Loaded |
| `hvo.weather.raw.v1` shovel to Service Bus | Working |
| Sample weather messages | Delivered to `weather-raw-v1` subscription |
| Shared ingest contracts | Added in `src/HVO.Ingest.Contracts` |
| Azure Functions persistence | Initial `hvo.weather.raw.v1` handler implemented |
| Davis RabbitMQ publishing | Optional parallel publisher implemented, disabled by default |
| Live Davis -> RabbitMQ -> Service Bus test | Passed |
| Live Service Bus -> Function -> Azure SQL test | Passed |

## POC Validation Result

The initial `hvo.weather.raw.v1` proof is complete enough to stop treating the design as speculative. The live test proved the expected path:

```text
Davis station
  -> Davis collector canonical publisher
  -> RabbitMQ hvo.weather.raw.v1 queue
  -> RabbitMQ shovel
  -> Azure Service Bus weather-raw-v1 subscription
  -> ProcessWeatherRawV1 Azure Function
  -> Azure SQL [v9].[WeatherRaw]
```

Validation summary:

| Check | Result |
|-------|--------|
| Existing Davis consumer stopped/absent before live access | Verified before test |
| Davis TCP connection | Connected successfully to `192.168.1.210:22222` |
| RabbitMQ local queue | Drained to `0` ready and `0` unacknowledged |
| RabbitMQ weather DLQ | `0` messages |
| Service Bus `weather-raw-v1` subscription | Drained from live messages to `0` active, `0` dead-letter |
| Azure Function trigger | `ProcessWeatherRawV1` registered after trigger sync and consumed messages |
| Azure SQL persistence | Recent Davis rows present in `[v9].[WeatherRaw]` with timestamps matching the live run |

POC caveats before production rollout:

| Area | Current POC State | Required Before Production |
|------|-------------------|----------------------------|
| Davis publisher | Runs in parallel with SQLite/API path and is disabled by default | Decide cutover strategy and remove/disable duplicate ingest paths deliberately |
| Function App | Temporary POC app was created and stopped after validation | Create permanent Function App/IaC with managed identity and monitoring |
| Function persistence | Uses parameterized SQL because Functions v4 supports `net9.0` while shared EF models target `net10.0` | Decide whether to keep SQL commands, multi-target data models, or isolate persistence services |
| SQL auth | Runtime still depends on SQL password via Key Vault for the website | Move apps to Entra auth/managed identity and separate migration credentials |
| Service Bus auth | Function POC used a listen secret | Prefer managed identity/RBAC for permanent Functions |
| RabbitMQ -> Service Bus TLS | POC may require `verify=verify_none` for shovel TLS hostname validation | Fix verified TLS before production |
| SignalR updates | Not implemented in the Function path | Add after persistence ownership is finalized |
| Observability | Basic logs only for new path | Add structured logging, metrics, traces, and alerts |

## Message Envelope

All canonical messages should use the same envelope shape:

```json
{
  "messageId": "hvo.weather.raw.v1:hvo-davis-01:2026-05-22T20:30:00Z",
  "schema": "hvo.weather.raw.v1",
  "siteId": "hvo",
  "source": "davis-vantage-pro2",
  "deviceExternalId": "hvo-davis-01",
  "observedAtUtc": "2026-05-22T20:30:00Z",
  "publishedAtUtc": "2026-05-22T20:30:01Z",
  "payload": {}
}
```

The broker message should also set application properties where supported:

| Property | Value |
|----------|-------|
| `schema` | Same value as the envelope `schema` |
| `siteId` | Observatory/site id, currently `hvo` |
| `deviceExternalId` | Stable source device id |
| `source` | Source system or collector name |

The `messageId` must be deterministic where the source has a natural idempotency key. For Davis weather, use schema + station id + recorded timestamp. For JK BMS, use schema + device id + recorded timestamp.

## Canonical Schemas

| Schema | Purpose | POC Source |
|--------|---------|------------|
| `hvo.weather.raw.v1` | Complete Davis weather observation | Davis collector |
| `hvo.weather.sensor.v1` | Weather-station auxiliary sensor event | Future Davis/other weather sensors |
| `hvo.weather.alarm.v1` | Weather-station alarm/event | Future Davis alarms |
| `hvo.environment.reading.v1` | Temperature/humidity/battery environment reading | ESPHome/Govee normalizer |
| `hvo.power.reading.v1` | Inverter/load/charge/battery power reading | SolarAssistant normalizer |
| `hvo.bms.reading.v1` | JK BMS pack/cell reading | JK BMS collector |

## RabbitMQ Topology

RabbitMQ runs locally on `hvo-docker` and accepts both AMQP and MQTT traffic.

### Exchanges

| Exchange | Type | Purpose |
|----------|------|---------|
| `hvo.raw` | topic | Optional raw source messages for local normalizers |
| `hvo.ingest` | topic | Canonical HVO ingest messages |
| `hvo.dead` | topic | Dead-letter exchange for local poison messages |

### Canonical Queues

| Queue | Binding Key |
|-------|-------------|
| `hvo.weather.raw.v1` | `hvo.ingest.weather.raw.v1.#` |
| `hvo.weather.sensor.v1` | `hvo.ingest.weather.sensor.v1.#` |
| `hvo.weather.alarm.v1` | `hvo.ingest.weather.alarm.v1.#` |
| `hvo.environment.reading.v1` | `hvo.ingest.environment.reading.v1.#` |
| `hvo.power.reading.v1` | `hvo.ingest.power.reading.v1.#` |
| `hvo.bms.reading.v1` | `hvo.ingest.bms.reading.v1.#` |

Each canonical queue has a matching dead-letter queue bound to `hvo.dead`.

### Shovel Strategy

Use RabbitMQ shovel to move canonical queue messages to Azure Service Bus.

The first POC should prove one queue end to end:

```text
hvo.weather.raw.v1
  -> RabbitMQ shovel destination AMQP 1.0
  -> Service Bus topic hvo-ingest
  -> subscription weather-raw-v1
```

Once one queue is proven, add shovels for the remaining canonical queues.

Do not commit Azure Service Bus connection strings to the repo. The RabbitMQ shovel should use a send-only SAS policy connection string stored on `hvo-docker` as a local secret or environment value.

Get the send-only policy key with Azure CLI when configuring the target host:

```bash
az servicebus topic authorization-rule keys list \
  --resource-group observatory-rg \
  --namespace-name hvoobs \
  --topic-name hvo-ingest \
  --name rabbitmq-shovel-send
```

Convert the key to the RabbitMQ shovel AMQP URI shape before setting `SERVICEBUS_RABBITMQ_AMQP_URI`:

```text
amqps://rabbitmq-shovel-send:<url-encoded-key>@hvoobs.servicebus.windows.net:5671/?sasl=plain
```

RabbitMQ 4.x on the current `hvo-docker` runtime failed the Azure Service Bus wildcard certificate hostname check during the first POC shovel test. For the POC, append `&verify=verify_none` to the Service Bus AMQP URI if the shovel remains stuck in `starting` with a `hostname_check_failed` TLS error. Before production, revisit this and prefer a fully verified TLS configuration.

Important shell quoting detail: quote the full Service Bus URI in `.env`, otherwise `&verify=verify_none` can be interpreted by the shell and will not reach RabbitMQ:

```text
SERVICEBUS_RABBITMQ_AMQP_URI='amqps://rabbitmq-shovel-send:<url-encoded-key>@hvoobs.servicebus.windows.net:5671/?sasl=plain&verify=verify_none'
```

The POC includes `scripts/configure-rabbitmq-weather-shovel.sh` to create the first dynamic shovel for `hvo.weather.raw.v1`. It expects these environment variables on the target host:

| Variable | Purpose |
|----------|---------|
| `RABBITMQ_SHOVEL_SOURCE_URI` | URL-encoded local RabbitMQ AMQP URI for the shovel source |
| `SERVICEBUS_RABBITMQ_AMQP_URI` | URL-encoded Azure Service Bus AMQP URI from the send-only policy |
| `SERVICEBUS_DESTINATION_ADDRESS` | Service Bus destination, default `hvo-ingest` |

The first proof should use the sample message in `deploy/observatory/samples/weather-raw-v1.json` and publish it to `hvo.ingest` with routing key `hvo.ingest.weather.raw.v1.hvo.hvo-davis-01`. Set the RabbitMQ message header/application property `schema` to `hvo.weather.raw.v1` so the Service Bus subscription filter can route it.

## Azure Service Bus Topology

Use one Service Bus topic and schema-filtered subscriptions.

| Entity | Name |
|--------|------|
| Namespace | `hvoobs` |
| Topic | `hvo-ingest` |

Subscriptions:

| Subscription | SQL Filter |
|--------------|------------|
| `weather-raw-v1` | `schema = 'hvo.weather.raw.v1'` |
| `weather-sensor-v1` | `schema = 'hvo.weather.sensor.v1'` |
| `weather-alarm-v1` | `schema = 'hvo.weather.alarm.v1'` |
| `environment-reading-v1` | `schema = 'hvo.environment.reading.v1'` |
| `power-reading-v1` | `schema = 'hvo.power.reading.v1'` |
| `bms-reading-v1` | `schema = 'hvo.bms.reading.v1'` |

Authorization policies:

| Policy | Rights | Consumer |
|--------|--------|----------|
| `rabbitmq-shovel-send` | Send | RabbitMQ shovel on `hvo-docker` |

Azure Functions should use managed identity where possible. If that is not available during the POC, use a separate listen-capable policy stored in Key Vault or Function App configuration.

## Azure Functions

Create one Functions app with separate handlers by schema/subscription.

Project:

```text
src/HVO.Ingest.Functions
```

The initial Functions project targets `net9.0` because Azure Functions v4 isolated worker tooling currently supports .NET targets up to `net9.0`. The shared contracts project targets both `net9.0` and `net10.0` so it can be used by the Function and the .NET 10 collectors.

Initial handlers:

| Function | Trigger |
|----------|---------|
| `ProcessWeatherRawV1` | Service Bus topic `hvo-ingest`, subscription `weather-raw-v1` |
| `ProcessWeatherSensorV1` | Service Bus topic `hvo-ingest`, subscription `weather-sensor-v1` |
| `ProcessWeatherAlarmV1` | Service Bus topic `hvo-ingest`, subscription `weather-alarm-v1` |
| `ProcessEnvironmentReadingV1` | Service Bus topic `hvo-ingest`, subscription `environment-reading-v1` |
| `ProcessPowerReadingV1` | Service Bus topic `hvo-ingest`, subscription `power-reading-v1` |
| `ProcessBmsReadingV1` | Service Bus topic `hvo-ingest`, subscription `bms-reading-v1` |

Each handler must:

1. Deserialize the canonical envelope.
2. Validate `schema` and payload version.
3. Write idempotently to the `v9` schema using the current `WeatherRaw` persistence rules.
4. Publish a SignalR update event after successful persistence.
5. Throw for poison messages so Service Bus dead-letter handling remains visible.

The first `ProcessWeatherRawV1` handler deserializes the canonical envelope, validates `hvo.weather.raw.v1`, and writes idempotently to `[v9].[WeatherRaw]` with the same unique key as the website API (`StationId` + `RecordedAt`). It uses parameterized SQL for the POC because the repo's EF data model currently targets `net10.0` while Functions v4 requires `net9.0` or earlier.

## SQL Authentication And Password Plan

The live POC exposed a deployment risk: the website currently gets `ConnectionStrings--HualapaiValleyObservatory` from Key Vault and that secret contains the SQL password for the shared `roys` SQL login. That login is also used outside this repo, so routine POC work must not rotate it accidentally.

Current state:

| Item | State |
|------|-------|
| Website SQL connection | Loaded from Key Vault secret `ConnectionStrings--HualapaiValleyObservatory` |
| Website identity | User-assigned managed identity `hvoobs-website-mi` exists |
| Azure SQL Entra admin | Not configured at POC time |
| Runtime SQL auth | SQL password auth |
| EF migrations | Website runs v9 EF migrations during startup in `ApiKeySeedService` |

Required security/auth work before production brokered ingest:

1. Configure an Azure SQL Microsoft Entra admin.
2. Create database users for managed identities/service principals, starting with `hvoobs-website-mi` and the permanent ingest Function identity.
3. Grant least-privilege runtime permissions to app identities.
4. Stop running EF migrations from the website runtime path, or split migration execution into a separate deployment/admin identity with DDL permissions.
5. Move website and Function runtime connection strings to Entra authentication, for example managed identity authentication rather than `User ID`/`Password`.
6. Keep any SQL password login as a legacy/manual break-glass credential only, with documented ownership and rotation procedure.
7. Avoid printing app settings, connection strings, SAS tokens, AMQP URIs, or SQL credentials in diagnostics. Prefer metadata-only checks and immediate rotation if a value is exposed.

Open implementation choice:

| Option | Pros | Cons |
|--------|------|------|
| Website managed identity has DDL and DML | Minimal code change if startup migrations stay | Too much runtime privilege |
| Separate migration job/identity, runtime identities have DML only | Least privilege and cleaner ops boundary | Requires deployment workflow changes |
| Keep SQL password for all app runtime | No auth refactor | Continues accidental rotation and shared-secret risk |

Preferred direction: separate migration identity/job, managed identity for website read/admin runtime, and managed identity for Functions ingest runtime.

## Post-POC Update Plan

Do not continue expanding telemetry domains until the following design cleanup is planned and implemented. The POC proves the architecture works; the next phase should make it maintainable and safe.

### 1. Project And Namespace Cleanup

- Normalize project names and namespaces around deployable boundaries: website, ingest functions, edge collectors, shared contracts, shared edge infrastructure.
- Decide whether `HVO.Ingest.Contracts` remains a standalone shared contract assembly or becomes part of a broader shared SDK.
- Decide how Functions and .NET 10 projects share persistence logic while Functions tooling is still on supported `net9.0`/`net10.0` combinations.
- Remove temporary POC-only assumptions from project files and deployment docs.

### 2. Shared Contracts And Mapping

- Keep canonical envelope/schema constants in one place.
- Move Davis `Loop2Packet`/`ArchiveRecord` to `WeatherRawV1` mapping into testable mapper classes instead of anonymous or worker-local construction.
- Add equivalent mappers for JK BMS and future normalizers.
- Add schema/version validation tests so invalid payloads dead-letter predictably.
- Decide whether raw Davis fields missing from `[v9].[WeatherRaw]` need new SQL columns, a JSON sidecar, or separate detailed weather tables.

### 3. Shared Publisher And Outbox Infrastructure

- Extract reusable RabbitMQ publisher options, connection handling, confirm handling, and message-property logic.
- Decide the cutover model for SQLite/API outbox versus RabbitMQ local durability.
- Avoid double ingest during migration by making each collector's output mode explicit: API-only, broker-only, or parallel POC.
- Add local dead-letter/retry visibility to collector UIs.
- Add integration tests for publisher idempotency, routing keys, headers, and broker failures using mocks/simulators by default.

### 4. Website Role And API Migration

- Keep current REST ingest APIs as fallback/debug during migration.
- Move long-term telemetry writes to Functions.
- Keep the website focused on dashboards, read APIs, admin, command/control, and operational visibility.
- Ensure Razor pages/components call application services or APIs consistently rather than duplicating DB access patterns.

### 5. Function Persistence And Idempotency

- Choose a permanent persistence approach for Functions: parameterized SQL commands, shared persistence library, or multi-targeted EF models.
- Keep idempotency deterministic by schema/device/timestamp or another natural key.
- Add poison-message behavior tests and document Service Bus dead-letter handling.
- Add replay/backfill strategy for retained Service Bus/RabbitMQ messages.

### 6. Logging, Telemetry, And Alerts

- Use structured logs with schema, message id, station/device id, observed timestamp, and correlation ids.
- Add metrics for RabbitMQ publish success/failure, publish latency, queue depth, shovel health, Function processing latency, SQL persistence latency, duplicate skips, and dead-letter counts.
- Add traces around collector read, canonical mapping, RabbitMQ publish, Function processing, and SQL write.
- Ensure secrets are redacted from logs and diagnostics.
- Add dashboards/alerts for no-data windows, queue backlog, DLQ growth, Function failures, SQL write failures, and collector reconnect loops.

### 7. Deployment And Operations

- Convert manually created POC cloud resources into repeatable scripts or IaC.
- Replace temporary Function App resources with permanent naming, managed identity, app settings, and monitoring.
- Pin RabbitMQ image/version and revisit the Service Bus shovel TLS verification workaround.
- Document how to start/stop live Davis access safely so only one process owns the console connection.
- Add runbooks for credential rotation, dead-letter replay, RabbitMQ recovery, and Service Bus subscription inspection.

## Source Migration Strategy

### Davis

The Davis collector already converts protocol packets to a normalized weather JSON payload before writing to its SQLite outbox. For the POC, the collector now also has an optional RabbitMQ publisher that can run in parallel with the existing SQLite/API path.

```text
Loop2Packet or ArchiveRecord
  -> normalized hvo.weather.raw.v1 envelope
  -> publish persistent AMQP message to hvo.ingest
```

Enable the publisher with `RabbitMqIngest:Enabled=true` and set `RabbitMqIngest:AmqpUri` from deployment configuration or environment variables. The publisher sets persistent AMQP messages, deterministic message ids, and application headers (`schema`, `siteId`, `source`, `deviceExternalId`) needed by the Service Bus subscription filters.

### JK BMS

The JK BMS collector already has a complete poll result with pack, cell, config, info, and alarm data. It should publish one canonical `hvo.bms.reading.v1` message per device poll.

### SolarAssistant

SolarAssistant is MQTT-native and likely emits many source-specific topics. Use a local normalizer service:

```text
SolarAssistant MQTT topics
  -> RabbitMQ raw queues
  -> HVO.Edge.Normalizer
  -> hvo.power.reading.v1 canonical messages
```

The normalizer may maintain a short latest-value cache and emit snapshots on a timer or when required fields are fresh.

### ESPHome / Govee

Prefer ESPHome to decode BLE locally. If ESPHome cannot emit one complete HVO packet, use the normalizer to combine temperature, humidity, and battery topics into `hvo.environment.reading.v1` snapshots.

## POC Sequence

1. Add docs and repeatable RabbitMQ topology files. Complete.
2. Create Service Bus topic/subscriptions in `hvoobs`. Complete.
3. Deploy RabbitMQ on `hvo-docker` using the observatory compose file. Complete.
4. Create a send-only Service Bus policy for the RabbitMQ shovel. Complete.
5. Configure the local RabbitMQ user with `scripts/configure-rabbitmq-local-user.sh` if the definitions import did not create it. Complete.
6. Configure one RabbitMQ shovel for `hvo.weather.raw.v1` to Service Bus topic `hvo-ingest`. Complete.
7. Publish a test canonical weather message to RabbitMQ. Complete.
8. Confirm it appears in the `weather-raw-v1` Service Bus subscription. Complete.
9. Add a minimal Azure Function handler that logs and validates `hvo.weather.raw.v1`. Complete.
10. Add SQL persistence for `hvo.weather.raw.v1` using current weather model/idempotency. Complete.
11. Convert Davis collector to publish canonical RabbitMQ messages. Complete for optional POC publisher.
12. Run live Davis -> RabbitMQ -> Service Bus -> Function -> SQL validation. Complete.
13. Plan and implement security/auth hardening, refactoring, and observability before expanding to other domains. Next.
14. Repeat for BMS after cleanup.
15. Add SolarAssistant and ESPHome normalizers after the canonical path and shared libraries are production-ready.

## Rollback Strategy

This branch is intentionally isolated. If the POC does not work well:

1. Keep the current SQLite/API outbox path unchanged in deployed collectors.
2. Delete or stop the RabbitMQ observatory compose stack.
3. Delete the Service Bus `hvo-ingest` topic and related policy if unused.
4. Continue using the website ingest APIs.

## Open Questions

| Question | Current Default |
|----------|-----------------|
| RabbitMQ image/version | Use a stable management image and pin before production |
| Shovel count | Start one shovel per canonical queue |
| Service Bus auth for Functions | Prefer managed identity/RBAC for permanent Function App |
| SQL auth for apps | Move from shared SQL password to Entra managed identity/service principal auth |
| EF migrations | Move out of website startup or use a separate migration identity |
| SignalR resource | Not yet created; create after first Function handler exists |
| Website ingest APIs | Keep during migration; eventually read/admin only |
