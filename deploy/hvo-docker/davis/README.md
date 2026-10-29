# Davis collector on hvo-docker

The single Davis acquisition authority is **hvo-docker**, using the existing
standalone Compose project `davis`. The devpi5 container/image/volume are
preserved for rollback and must remain stopped. Issue
[#462](https://github.com/HualapaiValley/HVO.WebSite/issues/462) records source-bound
checksums, runtime evidence, delivery errors and the final Pi restart policy.

## Current authorization and acceptance

Roy's final instruction supersedes the earlier trials' rollback rules: keep
collection on hvo-docker and diagnose optional delivery problems after transfer.

Rollback is limited to a core failure:

- The container cannot start or stay healthy.
- The collector receives no Davis LOOP data for **10 minutes or more**.
- Website ingest fails outright.

**CWOP, Weather Underground and MQTT timeouts/failures do not trigger rollback.**
Record their UTC timestamps, sanitized failure categories and observed counts for
later diagnosis. They can produce Warning health while core collection and
ingest remain healthy; assess the actual source, outbox and central timestamps.

After **at least 15 minutes of healthy collection and website ingest**, set the
already stopped Pi container to `restart=no`, then verify it remains stopped.
Keep the documentation PR draft and unmerged. See #462 for actual observation
boundaries and any remaining optional-delivery issues.

## Deployment contract

Reuse [the original Davis Compose definition](../../pi-gateways/davis/docker-compose.yml);
do not add another collector to the website stack.

| Item | Contract |
|---|---|
| Host / architecture | hvo-docker / amd64 |
| Container / project | `davis-hvo-davis-1` / `davis` |
| Runtime network | `davis_default`, bridge MTU **1450** |
| Health / diagnostics | `hvo-docker:5100` to container HTTP 8080 |
| WeatherLink logger | `192.168.1.210:22222`; no device mappings, privilege or host networking |
| Mounted configuration | `/home/roys/.local/share/hvo-edge/davis/gateway.json` to `/app/config/gateway.json`, read-only |
| Mounted secrets | `/home/roys/.local/share/hvo-edge/davis/secrets/` to `/run/secrets`, read-only |
| Durable state | `davis_davis-outbox` to `/app/data` |
| Central ingest | `http://hvo-docker.hvo.lan/` |
| Optional consumers | CWOP `DW4515`, WU `KAZKINGM12`, HA MQTT `192.168.1.113:1883` |

The reused image is
`sha256:06e0e5aad579d3ca4b06b8d35faf07f63b03cb8f9e154ab62c45dde991d1741d`,
built from main `1dced139fcb811fd1a20c09e230457130148e962`. The operational
checkout is `/home/roys/deploy/HVO.WebSite-462` on hvo-docker. The retained Pi
image is arm64. **Never build on hvo-docker or home-docker**, including when an
image is missing. The one-off image remains in use until Kepler/Roy separately
cuts over to the registry image from
[#464](https://github.com/HualapaiValley/HVO.WebSite/issues/464).

The historical build-only host-network overlay has been removed under #464.
Its earlier approval for the eth0 MTU 1450 / bridge MTU 1500 build failure does
not authorize future target-host builds. CI publishes a versioned multi-arch
`registry.hualapaivalleyobservatory.org/hvo-davis` image; target hosts pull the
reviewed version and immutable index digest. Publishing and pulling do not
authorize a deployment/restart. The owner subsequently repaired Docker's default
MTU outside this migration session; the Davis bridge now reports
`{"com.docker.network.driver.mtu":"1450"}`.

Use the existing LAN SSH aliases. Do not change daemon/network/MTU settings,
routes or gateways as part of collector deployment. Health/diagnostic consumers
should use `hvo-docker:5100`, not the stopped Pi.

## Preserved cutover procedure

These steps describe the authorized #462 transfer, not permission to repeat a
live cutover or restart the Pi after closeout. The
[SQLite checkpoint contract](../../../docs/gateways/sqlite-backup-and-rollback.md)
also applies. Preserve current source/device identities and disabled archive
catch-up under [#346](https://github.com/HualapaiValley/HVO.WebSite/issues/346).

1. Verify the target is stopped, the recorded amd64 image exists and the Davis
   network reports MTU 1450. Reuse the existing configuration/secrets if an
   in-memory content/mode/owner comparison matches the Pi. Otherwise use secure
   SSH/tar transfer, retaining owner and permissions: files `0600`, secrets
   directory `0700`. Never print values or secret hashes, dump the rendered
   environment, or commit operational files.
2. In the operational checkout, the ignored
   `deploy/pi-gateways/davis/.env` contains these non-secret paths/settings:

   ```dotenv
   DAVIS_STATION_HOST=192.168.1.210
   DAVIS_REMOTE_CONFIG_FILE=/home/roys/.local/share/hvo-edge/davis/gateway.json
   DAVIS_REMOTE_SECRETS_DIRECTORY=/home/roys/.local/share/hvo-edge/davis/secrets
   ```

   Validate without displaying configuration values:

   ```bash
   docker compose --env-file deploy/pi-gateways/davis/.env \
     -f deploy/pi-gateways/davis/docker-compose.yml -p davis config --quiet
   docker image inspect davis-hvo-davis --format '{{.Id}} {{.Architecture}}'
   docker network inspect davis_default --format '{{json .Options}}'
   ```

3. Stop `davis-hvo-davis-1` on devpi5 without removing it. Verify process exit,
   no running Docker volume users and no other open database writers. Never run
   both collectors or a separate WeatherLink acquisition probe.
4. Inventory/archive the quiescent current state, retaining `outbox.db`,
   `davis-local.db`, any WAL/SHM companions for **both** databases and gateway
   metadata. **Exclude `outbox.db.old` and retained `.pre-migration-*` snapshot
   directories from transfer**, leaving them intact on the source. Qualify an
   isolated restore with integrity and foreign-key checks. Never open/checkpoint
   the source database to simplify the copy.
5. Preserve existing target database files by rename before replacement so
   stale WAL/SHM cannot mix with the new snapshot. Copy the current state into
   its existing named volume. Compare per-file SHA-256, sizes, modes and owners
   before target startup. Retain protected archives on the operator workstation
   and a second durable host.
6. Recheck the Pi is stopped. Start the existing target without rebuilding or
   recreating its container; ensure its restart policy is `unless-stopped`:

   ```bash
   docker update --restart=unless-stopped davis-hvo-davis-1
   docker compose --env-file deploy/pi-gateways/davis/.env \
     -f deploy/pi-gateways/davis/docker-compose.yml -p davis \
     up -d --no-build --no-recreate
   ```

7. Observe healthy core collection/ingest for at least 15 minutes. Capture
   health/source freshness, connection/LOOP evidence, website Batch ingest
   inserted/skipped/failed lines, pending counts and
   `/api/v1/weather/current` **`current.recordDateTime`** over time. Individual
   successful LOOP packets are not logged at Information level in this image;
   Live state, advancing observations and live MQTT can supplement connection
   logs. Do not invent packet log evidence.
8. Record optional CWOP/WU/MQTT errors and successes throughout the window.
   Initial CWOP `no-reading` enters its normal five-minute backoff and should
   be distinguished from a transport attempt. Diagnose optional failures later;
   keep healthy core collection/ingest on hvo-docker.
9. After the healthy core window passes, on devpi5:

   ```bash
   docker update --restart=no davis-hvo-davis-1
   docker inspect davis-hvo-davis-1 \
     --format '{{.State.Status}} {{.HostConfig.RestartPolicy.Name}}'
   ```

   Verify stopped/no-restart state; retain the container, image, volume, old DB
   and all checkpoints. Publish the observation window and error counts on
   #462; keep PR #463 draft.

## Core-failure rollback only

If a permitted core failure occurs, stop the target first, prove quiescence and
set its restart policy to `no`. Preserve its current state with a verified
archive/isolated restore. Copy any acquired unsent observations and current
outbox send state back before restarting the Pi; retain original destination
files by rename and verify checksums before startup. Respect schema compatibility
and preserve later observations under the SQLite contract. Restore Pi
`restart=unless-stopped` only for that authorized rollback, then start only it
and verify collection/ingest. Never discard later observations or restart both
collectors. Optional delivery failures alone do not authorize this path.

## Earlier trials and retained evidence

The first preparation was held for stale local NuGet package hashes and a
target build network stall. The owner repaired the cache, and the approved
host-network build produced the retained image. An earlier WU timeout and a
later CWOP timeout caused record-preserving rollbacks under the then-current
rule. Roy explicitly changed that rule for the final transfer.

Prior `.pre-migration-*` directories and protected archives remain on their
owning hosts. The final transfer preserves another target snapshot at
`.pre-migration-20261029T1638/` inside its volume. The pre-cutover archive is
retained on hvo-dev-02 and under
`/home/roys/backups/hvo-462/cutover-final-20261029T1638/` on hvo-docker.
Collection gaps during downtime cannot currently be recovered while archive
catch-up is disabled; no gap recovery is claimed.

The prior MTU retry explicitly authorized `compose down` without `-v` to replace
only its obsolete stopped target container/bridge. The final transfer reuses
them without deletion. No image, volume, database, Pi artifact or checkpoint
cleanup is part of #462.
