# JK BMS vNext Deployment And Endurance

Issue #328 replaces the JK local UI/custom host with the standard headless Edge
runtime. This document is a cutover checklist, not authorization to deploy.

## Preflight

1. Keep the current production container running while preparing configuration.
2. Copy `deploy/pi-gateways/jkbms/gateway.json.example` to ignored `gateway.json`.
3. Confirm every physical BMS has the same address and alias as production plus a
   stable `DeviceId`. Reordering entries must not change `DeviceId`.
4. Create non-empty `diagnostics-api-key` and `central-ingest-api-key` files in
   the ignored secrets directory. Add MQTT username/password files only when MQTT
   is enabled.
5. Verify `Outbox:DatabasePath` remains `/app/data/outbox.db` and the Compose
   volume remains `jkbms-outbox`. Do not create a replacement volume.
6. Run `./scripts/deploy-pi-gateway.sh --dry-run --context devpi5 jkbms`. Resolve
   all mounted-config, secret, and shell-override failures before cutover.

The application migrates the legacy database after DI is built but before host
startup. This ordering is intentional: `EdgeOutboxInitializer.StartingAsync`
would otherwise validate the old JK table first and fail before migration.
Legacy reading rows remain pending/deliverable under the canonical payload type.
Redundant standalone config/device-info rows are terminally accounted with an
explicit migration reason rather than silently stranded outside the shared
forwarder's single payload lane.

## Bounded Endurance Check

Perform this once before production cutover, not on every PR:

1. Confirm no second JK acquisition authority is running.
2. Run the candidate against all configured devices for a bounded observation
   window agreed by the operator, with no other live Bluetooth test process.
3. Verify one persistent BlueZ connection owner per BMS, healthy devices continue
   polling while one test device is unavailable, and cancellation shuts sessions
   down cleanly.
4. Verify reconnect backoff, adapter coordination, and recovery without restarting
   healthy device sessions.
5. Interrupt central network access long enough to accumulate outbox rows, restore
   access, and verify strict inserted/skipped/failed accounting drains the queue.
6. Exhaust retries for a controlled transient failure, then verify the requeue
   worker returns retry-exhausted rows to pending delivery.
7. Restart the candidate and verify HA entity IDs and retained availability topics
   remain stable. Confirm positive current/power means charge and negative means
   discharge.
8. Verify `/health/live`, `/health`, and API-key-protected `/diagnostics/status`
   and `/diagnostics/outbox` expose no secrets, Bluetooth addresses, or file paths.

Record the window, device count, reconnects, failed polls, maximum outbox depth,
drain result, and any BlueZ anomalies before approving cutover. Roll back by
restarting the preserved prior image against the same named outbox volume; never
run old and new collectors simultaneously.

## Production Validation: 2026-08-12/13

Issue #356 completed the production vNext cutover with the existing
`jkbms_jkbms-outbox` volume and exactly one direct BLE collector:

- A checksum-verified, tar-readable quiescent volume archive was created before
  the legacy schema migration. The rollback image reference was also preserved.
- All seven configured BMS sessions connected through the direct collector and
  recovered after bounded collector, Mosquitto, and Home Assistant restarts.
- Home Assistant exposes 25 available entities per bank with stable entity IDs.
  Device names use the reported JK model, nominal capacity, and stable bank
  number; device metadata includes reported hardware and firmware versions.
- MQTT and Home Assistant restarts did not interrupt BLE collection or canonical
  outbox delivery. The queue returned to zero pending after each restart.
- Twenty records that failed with HTTP 401 during a controlled ingest-key
  rotation were requeued from a checksum-backed quiescent database and delivered.
  Five unrelated pre-existing permanent failures remain preserved for separate
  payload investigation; current forwarding is synchronized.
- A Home Assistant configuration/database backup was completed through the
  supported backup API before updating stale entity-registry precision
  suggestions. The supported entity-registry API updated 172 HVO MQTT sensors
  across Davis, EG4, SmartShunt, and JK BMS; no explicit user display-precision
  override was present or changed.
- Effective precision was verified through
  `config/entity_registry/list_for_display` after a full Home Assistant restart.
  Examples include JK voltage/current at 3 decimals, SmartShunt voltage at 2 and
  current at 3, EG4 6500EX battery voltage at 2 and current at 0, EG4 MPPT100
  voltage/current at 1, and Davis pressure/console battery at 3.

Home Assistant-native Kasa and Govee entities were not modified by the HVO MQTT
migration. Their discovery metadata remains owned by their native integrations,
and no duplicate HVO MQTT writer was introduced.
