# Davis collector placement on hvo-docker

Issue [#462](https://github.com/HualapaiValley/HVO.WebSite/issues/462) authorizes
moving the single Davis collector from Wi-Fi-only `devpi5` to `hvo-docker`, beside
the website and SQL. **The migration is not complete.** The resumed 2026-10-29
cutover was rolled back because Weather Underground timed out from the new
container. The Pi remains the acquisition authority. See the issue comments for
the source-bound checksums, health, ingest and rollback evidence before resuming.

## Attempt and rollback on 2026-10-29

- The first attempt stopped before cutover. The owner resolved `NU1403` by
  backing up/removing stale locally packed `HVO.Core`/cfitsio packages from the
  workstation NuGet cache. The resumed exact SDK `10.0.402` locked restore and
  solution build passed with zero warnings/errors; all 1,411 required non-live
  tests passed, including 60 browser tests. No lock files were changed.
- The target build stall was diagnosed as eth0 MTU 1450 versus Docker bridge
  MTU 1500. Roy approved the [build-only host-network overlay](compose.build-host.yml).
  Using it, the amd64 image built successfully from main
  `1dced139fcb811fd1a20c09e230457130148e962`. Runtime remained on `davis_default`.
  No daemon/MTU/routes/gateway changes or Docker restart occurred.
- Configuration/secrets were securely copied with matching permissions. The Pi
  stopped at 15:57:20 UTC; SHA-256 verification and isolated SQLite integrity
  checks passed before target startup at 15:59:07 UTC. Both cleanly stopped
  databases had no remaining WAL/SHM companions. `outbox.db.old` was excluded.
- Target MQTT published live state and the website accepted 57,850 records in
  1,157 batches with no skipped/failed rows during the target's run. Pending
  records fell from 95,963 to 38,245. No Davis ingest data-rate error was found
  in that bounded window. LOOP freshness became Live; this source does not log
  individual successful LOOP packets at Information level. Central weather
  advanced but did not reach the few-minutes freshness requirement.
- Weather Underground timed out with three consecutive failures and no target
  success. Roy's explicit failure rule triggered rollback. CWOP's initial
  `no-reading` result entered its normal 300-second backoff; the target was
  stopped before a CWOP transport attempt, so CWOP was not qualified there.
- Target stopped cleanly at 16:00:05 UTC and was set to `restart=no`. Its full
  current outbox, including new observations and updated send state, passed
  isolated integrity checks and SHA-256 copy-back verification. Pi restarted at
  16:01:39 UTC with its retained original image and `restart=unless-stopped`.
  Target container/image/volume/configuration remain available but stopped.
- Original Pi databases are preserved under
  `/var/lib/docker/volumes/davis_davis-outbox/_data/.pre-migration-20261029T155720/`;
  `outbox.db.old` remains at the volume root. Restricted pre-cutover and rollback
  archives are retained on `hvo-dev-02`, with copies on hvo-docker and devpi5
  respectively. Nothing was deleted. Acceptance remains open on #462.

The build overlay fixes build transport only. It does not qualify runtime
outbound delivery over the bridge. Do not retry the cutover until that remaining
runtime issue is resolved under applicable authorization; the Docker MTU/daemon
maintenance is separately scheduled.

## Deployment contract

Reuse [the existing Davis Compose file](../../pi-gateways/davis/docker-compose.yml)
as its own project `davis`; do not add a second service to the website stack.
The target is amd64, so rebuild current `main` on hvo-docker: the retained Pi
image is arm64. Davis needs TCP to `192.168.1.210:22222`, no physical-device
mapping, privilege or runtime host networking. Use LAN SSH aliases `hvo-docker` and
`devpi5`; never change networking, routes or gateways for this move.

| Item | Target contract |
|---|---|
| Container / project | `davis-hvo-davis-1` / `davis` |
| Health and diagnostics | `hvo-docker:5100` to container HTTP 8080 |
| Mounted configuration | `/home/roys/.local/share/hvo-edge/davis/gateway.json` to `/app/config/gateway.json`, read-only |
| Mounted secrets | `/home/roys/.local/share/hvo-edge/davis/secrets/` to `/run/secrets`, read-only |
| Durable state | `davis_davis-outbox` to `/app/data` |
| Central ingest | Existing `http://hvo-docker.hvo.lan/` endpoint |
| Consumers | CWOP `DW4515`, WU `KAZKINGM12`, HA MQTT `192.168.1.113:1883` |

Keep the existing source/device identities, external-delivery settings and
disabled archive catch-up unchanged. Issue [#346](https://github.com/HualapaiValley/HVO.WebSite/issues/346)
owns archive recovery; readings missed during downtime cannot currently be
recovered. Move any diagnostics/dashboard references from `devpi5:5100` to
`hvo-docker:5100` only after a successful cutover.

## Resume the authorized migration

Follow #462 in order, together with the
[SQLite checkpoint/rollback contract](../../../docs/gateways/sqlite-backup-and-rollback.md).
Its full-volume default has one explicit exception for this assignment:
**exclude `outbox.db.old` from transfer and keep it on the Pi**.

1. Resolve the reported runtime delivery prerequisite before another cutover attempt.
   Build the amd64 image from a recorded current `main` commit on hvo-docker
   while the Pi collector remains running. The initial source was
   `1dced139fcb811fd1a20c09e230457130148e962`, staged at
   `/home/roys/deploy/HVO.WebSite-462`. Verify any existing directory before
   reuse; a staged source tree or partial image build is not a deployed service.
   For build interpolation, first create the ignored
   `deploy/pi-gateways/davis/.env` in that checkout with these non-secret values:

   ```dotenv
   DAVIS_STATION_HOST=192.168.1.210
   DAVIS_REMOTE_CONFIG_FILE=/home/roys/.local/share/hvo-edge/davis/gateway.json
   DAVIS_REMOTE_SECRETS_DIRECTORY=/home/roys/.local/share/hvo-edge/davis/secrets
   ```

   From that checkout on hvo-docker, use the same file/project and overlay for
   build, configuration validation and later startup. Relative paths resolve
   from the first (original Davis) Compose file:

   ```bash
   docker compose --env-file deploy/pi-gateways/davis/.env \
     -f deploy/pi-gateways/davis/docker-compose.yml \
     -f deploy/hvo-docker/davis/compose.build-host.yml -p davis config --quiet
   docker compose --env-file deploy/pi-gateways/davis/.env \
     -f deploy/pi-gateways/davis/docker-compose.yml \
     -f deploy/hvo-docker/davis/compose.build-host.yml -p davis build
   ```

2. After a successful build, create the target mounted-config directory and
   securely stream `gateway.json` and `secrets/` between the hosts with SSH/tar,
   preserving ownership and modes. Configuration and secret files are mode
   `0600`; the secrets directory is `0700`. Never print, log or commit their
   values, or render them through Compose. Refuse to overwrite unrecognized
   target files; verify recognized retained files before reuse. Validate
   configuration again before proceeding.
3. Stop `davis-hvo-davis-1` on devpi5 without removing it. Verify it is stopped,
   there are no other outbox writers and the target collector is not running.
   Never run two collectors or a separate WeatherLink acquisition probe.
4. Inventory and checkpoint the stopped volume, retaining `outbox.db`,
   `davis-local.db`, any WAL/SHM companions for **both** databases, and other
   gateway-owned metadata. Preserve the original Pi volume. Transfer to the
   target's `davis_davis-outbox` only while both collectors are stopped, excluding
   `outbox.db.old`. Record matching per-file SHA-256 checksums before startup,
   and qualify an isolated restore as specified by the SQLite contract. Never
   open/checkpoint the source database to make a copy easier.
5. Recheck that the Pi is stopped, then start the prebuilt target image:

   ```bash
   docker compose --env-file deploy/pi-gateways/davis/.env \
     -f deploy/pi-gateways/davis/docker-compose.yml \
     -f deploy/hvo-docker/davis/compose.build-host.yml -p davis up -d --no-build
   ```

6. Verify actual `/health` state and source freshness as well as Docker health.
   Capture collector acquisition/LOOP log evidence, website `Batch ingest: N
   inserted` lines, and zero new Davis `MinRequestBodyDataRate`/ingest errors.
   Sample protected outbox pending counts and the website's
   `/api/v1/weather/current` **`current.recordDateTime`** over time. Require a
   steadily draining backlog and central observations within a few minutes of
   now; HTTP 200 alone is insufficient. Verify fresh CWOP/WU success timestamps
   and MQTT state publication from the sole target collector.
7. After successful verification, set
   `docker update --restart=no davis-hvo-davis-1` on devpi5 and verify it remains
   stopped. Keep its container, image, volume and `outbox.db.old` until separate
   cleanup approval. Publish progress/final evidence on #462; keep the associated
   documentation PR draft and unmerged.

## Failure boundary and rollback

On an unexpected build, health, ingest or CWOP/WU/MQTT failure/stall, stop and report rather than
changing dependencies, batching, networking or gateway settings to force a move.
Before cutover, leave the original Pi collector running; no target data exists
to restore. After target startup, stop it first and prove quiescence. Preserve
its current state and set the failed target to `restart=no` so a reboot cannot
create a second collector. If it acquired unsent records, copy its stopped outbox
back to the Pi as required by #462 and the preservation/schema rules in the
SQLite contract. Do not discard later records or overwrite an unqualified
snapshot; preserve original destination files by rename before copy-back so
stale WAL/SHM files cannot mix with the restored database. Verify checksums
before restart. Then restore Pi `restart=unless-stopped` and start only the Pi
collector. Retain all containers, images, volumes and checkpoints, and report
the verification result on the issue.
