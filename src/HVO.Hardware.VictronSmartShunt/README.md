# HVO.Hardware.VictronSmartShunt

Headless .NET 10 direct SmartShunt collector. One paired BlueZ session uses public `6597...` GATT fields and the existing bounded keepalive; current source has no private enrichment, settings/sync write or local UI. Direct acquisition remains the sole canonical writer. [#352](https://github.com/HualapaiValley/HVO.WebSite/issues/352) reevaluates passive HA acquisition after encryption-key, field/sign/cadence parity, atomic exporter and controlled cutover gates; advertisement transport evidence alone does not migrate authority.

[Program.cs](Program.cs) composes [registration](Hosting/SmartShuntServiceCollectionExtensions.cs), ordered legacy migration and shared runtime endpoints. [Dependencies](HVO.Hardware.VictronSmartShunt.csproj) are Linux.Bluetooth and Edge.Contracts/Hosting/Outbox/HomeAssistant.Mqtt. The [public decoder](SmartShunt/SmartShuntPublicProtocol.cs) owns exact UUIDs, scales/signs and null sentinels. HA MQTT is bounded current presentation; summary/detail history uses one atomic durable observation and the dedicated website source claim.

## Configuration and prerequisites

Production requires non-secret `/app/config/gateway.json`, startup-read files under `/run/secrets`, preserved `/app/data/outbox.db` on `smartshunt-outbox`, paired hardware and exactly one acquisition owner. Environment overrides win; JSON reload is disabled. Use the [mounted example](../../deploy/pi-gateways/smartshunt/gateway.json.example) and [options](Configuration/SmartShuntOptions.cs) for stable source/device IDs, address/adapter, approved central endpoint and cadence. Diagnostics/ingest keys are distinct; enabled MQTT requires separate username/password files. Website seeding must grant the dedicated SmartShunt key the exact source; a broad power key is insufficient. Config/secret changes need approved restart/replacement.

Public `/health/live` is liveness only. `/health` and `/health/ready` return actual health (Critical → 503; degraded/noncritical can be 200). Protected GET `/diagnostics/health`, `/diagnostics/status`, `/diagnostics/outbox` and PUT `/diagnostics/outbox/settings` use the diagnostics key (missing/wrong → 403). Runtime batch/sweep overrides reset on restart; devices are included in status. No status UI or generic BLE command route is mapped.

## Local validation

Use the exact [pinned SDK](../../global.json) and [testing prerequisites](../../docs/development/testing.md). From repository root after locked restore/build:

```bash
dotnet test tests/HVO.Hardware.VictronSmartShunt.Tests --filter "TestCategory!=Integration&TestCategory!=Live"
```

Physical Live checks need separate bounded authority; ordinary tests do not connect to the installed shunt. The [current manual](../../docs/gateways/victron-smartshunt.md), [operations](../../docs/GATEWAY_OPERATIONS.md), [Pi setup](../../deploy/pi-gateways/README.md) and [SQLite recovery](../../docs/gateways/sqlite-backup-and-rollback.md) own deeper contracts. The [May 25 archive](../../docs/archive/2026-05-25-smartshunt-plan.md) preserves private-field/SoC-overlay/UI research.
