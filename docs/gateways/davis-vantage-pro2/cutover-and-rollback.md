# Davis vNext Cutover And Rollback

This runbook records the Davis slice of issue #330. It does not close the broader source-retirement issue.

## Authority

| Responsibility | Authority |
|---|---|
| WeatherLink IP TCP session | `HVO.Hardware.DavisVantagePro2` on `devpi5` |
| Live and archive acquisition | Davis vNext collector |
| Canonical history writer | Davis collector outbox to the website raw/archive APIs |
| Home Assistant presentation | Davis collector to local MQTT to HA |
| Historical backfill | Davis console archive through `DMPAFT`; never HA Recorder or the HA exporter |

Only one process may connect to the WeatherLink IP bridge. Never start the legacy and vNext collectors together, and do not run a direct station probe while either collector owns the bridge.

The HA exporter must exclude HVO-owned Davis MQTT entities. MQTT state is current-state presentation and availability only; it is not canonical history.

## Persistent State

- Keep the Compose volume `davis_davis-outbox` mounted at `/app/data` through upgrades and rollback.
- The volume contains the shared outbox plus gateway-owned station metadata and the archive cursor.
- The shared SQLite outbox uses WAL mode and a bounded lock timeout. Do not copy an active database as a rollback backup.
- Keep the legacy `davis_davis-data-protection` volume while the legacy image remains a rollback option.
- Runtime `gateway.json`, mounted secrets, databases, and backup archives are ignored operational material. Do not commit or print them.

## Cutover Sequence

1. Record the running legacy image ID, container name, named volumes, outbox counts, and latest central raw timestamp.
2. Run `./scripts/deploy-pi-gateway.sh --dry-run --context devpi5 davis` with stale shell overrides unset.
3. Stop the legacy collector and confirm it has exited before vNext starts.
4. Back up the quiescent named volumes to an operator-owned directory with directory mode `0700` and files mode `0600`; verify the archive checksum.
5. Deploy vNext with `./scripts/deploy-pi-gateway.sh --context devpi5 davis` against the existing `davis_davis-outbox` volume.
6. Verify the container is healthy with restart count zero and is the only WeatherLink client.
7. Verify `/diagnostics/status` reports one online device, a compatible outbox schema, no failed records, and current forwarding.
8. Verify central raw ingestion continues and archive batches are accepted by the dedicated archive endpoint.
9. Verify HA discovers the stable Davis device and current enabled entities report availability. Advanced/raw entities may be disabled by default.
10. Restart the vNext container once. Verify WeatherLink reconnect, stable HA entity IDs, MQTT state republish, preserved cursor, and outbox drain.

## Archive Recovery

The Davis logger is a circular buffer. A requested timestamp older than retained history can return a full circular page range with stale records before the requested window instead of returning zero pages.

The collector therefore:

- skips stale leading circular-buffer records until it reaches the requested window;
- advances the durable cursor only after an archive record is accepted by the local outbox;
- requests records after the exact durable console cursor and downloads at most 25 accepted records per top-off so downstream persistence completes within the live freshness budget before LOOP collection resumes;
- cancels a 513-page full circular-buffer response before requesting page one, preserving live LOOP collection while bounded recovery is deferred to #346;
- runs production v1 with archive catch-up disabled until #346, preserving the existing archive data and durable cursor while live raw forwarding continues;
- uses the protocol-defined `<ESC>` byte for bounded archive cancellation;
- retries from the exact durable console cursor, relying on atomic outbox idempotency to ignore any repeated timestamp without error logs.

When #346 re-enables backlog recovery, verify that the cursor moves forward across slices while central raw timestamps continue to advance. Do not treat HA Recorder as an archive source.

## Rollback

Rollback preserves the one-owner invariant:

1. Stop vNext and confirm the container is not running.
2. Preserve the current vNext volume before modifying it. Never restore into a mounted, active volume.
3. If the shared-schema migration must be undone, restore the verified quiescent pre-cutover volume backup. The legacy image is not expected to understand every vNext schema change.
4. Restore the preserved legacy image and its original Compose/runtime contract with the `davis_davis-outbox` and `davis_davis-data-protection` volumes.
5. Start only the legacy collector.
6. Verify one WeatherLink owner, live central raw timestamps, outbox drainage, and legacy health before declaring rollback complete.
7. Keep vNext stopped until a new controlled cutover.

Never infer successful rollback from container liveness alone. The station TCP session, raw history, outbox state, and central timestamps must all be checked.

## Production Evidence

The August 2026 Davis cutover established:

- legacy acquisition stopped before vNext started;
- the existing outbox volume was migrated in place with a verified quiescent backup retained;
- vNext remained the sole WeatherLink owner through deployment and restart;
- raw and archive website endpoints accepted vNext batches;
- archive recovery diagnostics advanced the preserved cursor to August 11 before live testing confirmed 513-page responses could interrupt LOOP; production v1 now leaves catch-up disabled and defers bounded recovery to #346;
- HA received 39 scalar Davis entities: 27 enabled by default and 12 advanced/raw entities disabled by default;
- outbox WAL mode was active, failed count remained zero, and repeated timestamps were ignored atomically.

Outdoor ISS fields stopped reporting before the cutover. Central history showed the last non-null outside temperature, humidity, wind, and solar values before legacy shutdown, while inside/console fields continued. HA correctly reports those absent outdoor values as `unknown`; do not replace them with stale values. Investigate Davis ISS radio, transmitter, battery, or console reception separately.
