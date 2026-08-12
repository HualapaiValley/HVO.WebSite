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
