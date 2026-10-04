# HVO.Hardware.JkBms

Headless .NET 10 edge collector for JK BMS battery banks. It retains one persistent
BlueZ session per configured device, coordinates connection attempts per adapter,
parses the proven JK protocol, and isolates reconnect/backoff state per device.

[Program.cs](Program.cs) and [registration](Hosting/JkBmsServiceCollectionExtensions.cs) compose Edge.Contracts, Edge.Hosting, Edge.Outbox and Edge.HomeAssistant.Mqtt; Linux.Bluetooth supplies BlueZ access. [#356](https://github.com/HualapaiValley/HVO.WebSite/issues/356) completed the vNext/MQTT cutover recorded in [dated endurance evidence](../../docs/gateways/jkbms/deployment-and-endurance.md). Source inspection is not new physical validation or deployment.

## Runtime

- `HVO.Edge.Hosting` supplies mounted configuration, secret files, structured
  logging, OpenTelemetry, liveness/readiness, and protected diagnostics.
- `HVO.Edge.Outbox` durably stores one complete `BmsIngressRecord` per poll and
  owns forwarding retries, dead-letter state, compaction, and diagnostics.
- `HVO.Edge.HomeAssistant.Mqtt` publishes bounded pack/cell-health/temperature/state/availability telemetry. Configured password devices additionally expose the existing button/status; MQTT is not a historical delivery path.
- Battery current and power are positive while charging and negative while
  discharging, matching the validated JK protocol and central BMS contract.

Public `/health/live` proves process liveness. `/health` and `/health/ready` return the actual snapshot (Critical → 503; degraded/noncritical can be 200). Protected GET `/diagnostics/health`, `/diagnostics/status`, `/diagnostics/outbox` and PUT `/diagnostics/outbox/settings` require the diagnostics key (missing/wrong → 403). Devices are inside status; runtime outbox overrides reset on restart. There is no local UI or generic command endpoint.

## Configuration

Production mounts `/app/config/gateway.json`, `/run/secrets`, and the existing
`jkbms-outbox` volume at `/app/data`. Use
[mounted example](../../deploy/pi-gateways/jkbms/gateway.json.example) as the configuration reference. Production config is required; environment overrides win, JSON reload is disabled and secrets are read at startup. Approved config/secret changes need restart/replacement.
Each device requires a Bluetooth `Address`, stable `DeviceId`, display `Alias`,
and optional `HciAdapter`/poll override.

An enabled device with optional `SettingsPasswordSecret` exposes `change_settings_password`. Startup requires six ASCII digits in that file. Its registered topic accepts non-retained `PRESS`, with the password taken only from the secret. Offline/busy/already-verified guards protect one bounded queued operation; success requires positive ACK and matching DeviceInfo.SetupPasscode readback. This credential write is not generic control, protection-setting writes or proof of explicit settings-query support. No ordinary test presses a deployed button or changes a credential. See the [current command/safety contract](../../docs/gateways/jk-bms.md#bounded-settings-password-command).

On startup, the collector migrates a pre-vNext JK outbox before the shared outbox
initializer validates the schema. Deliverable reading rows are canonicalized;
redundant standalone config/device-info rows are explicitly marked accounted
because those snapshots were already embedded in their corresponding reading.

## Validation

Use the exact [pinned SDK](../../global.json) and [testing prerequisites](../../docs/development/testing.md). From repository root after locked restore/build, run focused ordinary cases without physical Bluetooth:

```bash
dotnet test tests/HVO.Hardware.JkBms.Tests --filter "TestCategory!=Integration&TestCategory!=Live"
```

Owned Integration fixtures have separate prerequisites. Physical BLE Live cases and future commissioning/endurance need explicit authority; the original cutover is complete. The [manual](../../docs/gateways/jk-bms.md), [operations](../../docs/GATEWAY_OPERATIONS.md), [Pi setup](../../deploy/pi-gateways/README.md) and [SQLite backup/rollback](../../docs/gateways/sqlite-backup-and-rollback.md) own deeper contracts. The [archived lifecycle proposal](../../docs/archive/jkbms-session-lifecycle.md) retains rationale and settings-query uncertainty.
