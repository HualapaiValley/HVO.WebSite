# HVO.Hardware.JkBms

Headless .NET 10 edge collector for JK BMS battery banks. It retains one persistent
BlueZ session per configured device, coordinates connection attempts per adapter,
parses the proven JK protocol, and isolates reconnect/backoff state per device.

## Runtime

- `HVO.Edge.Hosting` supplies mounted configuration, secret files, structured
  logging, OpenTelemetry, liveness/readiness, and protected diagnostics.
- `HVO.Edge.Outbox` durably stores one complete `BmsIngressRecord` per poll and
  owns forwarding retries, dead-letter state, compaction, and diagnostics.
- `HVO.Edge.HomeAssistant.Mqtt` publishes a bounded ten-entity current-state and
  availability projection. It is not a historical delivery path.
- Battery current and power are positive while charging and negative while
  discharging, matching the validated JK protocol and central BMS contract.

The standard endpoints are `/health/live`, `/health`, `/health/ready`, and the
`X-Api-Key` protected `/diagnostics/*` routes. There is no local UI.

## Configuration

Production mounts `/app/config/gateway.json`, `/run/secrets`, and the existing
`jkbms-outbox` volume at `/app/data`. Use
`deploy/pi-gateways/jkbms/gateway.json.example` as the configuration reference.
Each device requires a Bluetooth `Address`, stable `DeviceId`, display `Alias`,
and optional `HciAdapter`/poll override.

On startup, the collector migrates a pre-vNext JK outbox before the shared outbox
initializer validates the schema. Deliverable reading rows are canonicalized;
redundant standalone config/device-info rows are explicitly marked accounted
because those snapshots were already embedded in their corresponding reading.

## Validation

Run the non-live suite without accessing Bluetooth:

```bash
dotnet test tests/HVO.Hardware.JkBms.Tests --filter "TestCategory!=Live"
```

Physical BLE tests remain tagged `TestCategory=Live` and are reserved for a
bounded commissioning/endurance check before production cutover.
