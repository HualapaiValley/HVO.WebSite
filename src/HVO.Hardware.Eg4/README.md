# HVO.Hardware.Eg4

Headless .NET 10 direct collector for allowlisted EG4 6500EX inverter and MPPT100-48HV telemetry. [Program.cs](Program.cs) composes [registration](Hosting/Eg4ServiceCollectionExtensions.cs), Edge.Hosting config/diagnostics, Edge.Outbox durability, Edge.Contracts typed power readings with optional MPPT/inverter detail and optional Edge.HomeAssistant.Mqtt current presentation. System.IO.Ports supports serial; no local UI or generic hardware control exists.

6500EX uses the reviewed PI30 inquiry allowlist through stable HID paths; MPPT uses one fixed Modbus holding-register read. Request writes are read inquiries, not configuration changes. Separate [6500EX](../../docs/gateways/eg4/6500ex-protocol.md) and [MPPT](../../docs/gateways/eg4/mppt100-48hv-protocol.md) references preserve CRC/layout/capture provenance. Durable `com.hvo.eg4.observation.v1` bundles preserve measurement point and native signs; do not sum branch, whole-bus and bank values as independent loads.

The [persisted bundle](Outbox/PowerOutboxWriter.cs) contains a power reading and optional MPPT/inverter detail; the [fleet worker](Workers/Eg4FleetWorker.cs) enqueues those members and the [sender](Outbox/Eg4OutboxBatchSender.cs) forwards their three endpoints. 6500EX [QET/QLT acquisition and caching](Telemetry/Eg46500ExTelemetrySource.cs) produce source-native energy-counter samples, but the current bundle and sender do not persist or forward them. A shared energy contract does not establish durable central energy history from this collector; see [observation identity and energy boundaries](../../docs/development/power-observation-identity.md).

## Configuration and prerequisites

Production requires non-secret `/app/config/gateway.json`, startup-read files under `/run/secrets`, preserved `/app/data/outbox.db` on `eg4-outbox`, approved physical mappings and one acquisition owner. Environment overrides win; JSON reload is disabled. [Options](Configuration/Eg4Options.cs), [validator](Configuration/Eg4OptionsValidator.cs) and [mounted example](../../deploy/pi-gateways/eg4/gateway.json.example) own stable source/device/alias IDs, types, polling and central endpoint. 6500EX requires `/dev/hvo/...`, UnitId 0; MPPT requires `/dev/serial/by-id/...`, UnitId 1. Diagnostic/ingest keys are distinct, with separate MQTT files when enabled. Simulation is restricted to Development/Testing; config/secret changes require approved restart/replacement.

Public `/health/live` is liveness only. `/health` and `/health/ready` return actual health (Critical → 503; degraded/noncritical can be 200). Protected GET `/diagnostics/health`, `/diagnostics/status`, `/diagnostics/outbox` and PUT `/diagnostics/outbox/settings` require the diagnostics key (missing/wrong → 403). Runtime batch/sweep overrides reset on restart; devices are included in status.

## Local validation

Use the exact [pinned SDK](../../global.json) and [testing prerequisites](../../docs/development/testing.md). From repository root after locked restore/build:

```bash
dotnet test tests/HVO.Hardware.Eg4.Tests --filter "TestCategory!=Integration&TestCategory!=Live"
```

Ordinary tests require no physical serial/HID target. Owned Integration/simulation fixtures and authorized physical Live checks have separate prerequisites. [Deployment/shadow guidance](../../docs/gateways/eg4/deployment-and-shadow-validation.md), [gateway operations](../../docs/GATEWAY_OPERATIONS.md), [Pi setup](../../deploy/pi-gateways/README.md) and [quiescent SQLite recovery](../../docs/gateways/sqlite-backup-and-rollback.md) own operational commands; this documentation verification executes none.
