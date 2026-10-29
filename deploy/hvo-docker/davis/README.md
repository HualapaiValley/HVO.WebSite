# Davis collector placement on hvo-docker

Issue [#462](https://github.com/HualapaiValley/HVO.WebSite/issues/462) authorizes
moving the single Davis collector from Wi-Fi-only `devpi5` to `hvo-docker`, beside
the website and SQL. **The migration remains incomplete.** The MTU 1450 retry
was rolled back after CWOP's second transport attempt timed out during the
required 15-minute observation. **devpi5 is again the sole active collector**;
the hvo-docker target is stopped with `restart=no`. See #462 for the current
result and source-bound operational evidence.

## MTU retry and rollback

The owner repaired Docker's default MTU outside this migration session. The old
`davis_default` bridge still had MTU 1500. Roy explicitly authorized
`docker compose -p davis down` **without `-v`** from the Davis Compose directory,
followed by recreation. This narrowly permits replacement of the stopped target
container and obsolete Davis bridge; retain the image, named volume, checkpoint
data and all Pi artifacts. Do not run a broader network/volume cleanup.

The retry reused image
`sha256:06e0e5aad579d3ca4b06b8d35faf07f63b03cb8f9e154ab62c45dde991d1741d`
without rebuilding. Both databases were copied afresh after stopping the Pi,
with matched SHA-256, ownership and modes and passing isolated SQLite checks.
Before startup, network inspection returned
`{"com.docker.network.driver.mtu":"1450"}` for `davis_default`. The Pi stopped
at 16:16:13 UTC and the target started at 16:17:51 UTC. The target backlog fell
from 38,577 to zero, current central weather caught up within seconds, WU and
MQTT succeeded, and CWOP's first scheduled transport attempt succeeded at
16:22:52 UTC after its normal startup backoff. Its second attempt failed with
category `timeout` at 16:27:57 UTC. Roy's explicit rollback condition applied;
the required 15-minute observation did not pass and Pi `restart=no` was never
set.

The target stopped cleanly at 16:28:35 UTC. Its current databases passed
isolated SQLite integrity/foreign-key checks and matched SHA-256, ownership,
modes and sizes after copy-back, including the one pending observation at
`2026-10-29 16:28:33.988255`. Pi restarted at 16:29:50 UTC with
`restart=unless-stopped`; target remains stopped with `restart=no`. Fresh
pre-retry Pi databases are retained at
`/var/lib/docker/volumes/davis_davis-outbox/_data/.pre-migration-20261029T161613/`.
The earlier target databases are retained at `.pre-migration-20261029T1615/`
inside the hvo-docker volume. All prior checkpoints and `outbox.db.old` remain.

The recreated Davis network remains MTU 1450, and the existing amd64 image is
retained. No daemon/interface MTU or route changes were made by this migration
session. Do not infer that a successful WU delivery or one CWOP cycle qualifies
another cutover; all downstream channels must pass the complete observation.

Do not rebuild the existing target image unless it is missing. The retained
[host-network build overlay](compose.build-host.yml) records the approved
workaround used to build this image before the daemon MTU repair; it changes
build transport only and is not required to run the existing image.

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
  At that checkpoint the target container/image/volume/configuration were
  retained and stopped; the later authorized retry replaced the stopped target
  container and bridge while preserving its data and image.
- Original Pi databases are preserved under
  `/var/lib/docker/volumes/davis_davis-outbox/_data/.pre-migration-20261029T155720/`;
  `outbox.db.old` remains at the volume root. Restricted pre-cutover and rollback
  archives are retained on `hvo-dev-02`, with copies on hvo-docker and devpi5
  respectively. No data or Pi artifacts were deleted. See the later retry
  evidence on #462 for the current acceptance state.

The earlier build overlay fixed build transport only. Runtime delivery became
eligible for a retry after the owner's separately authorized daemon MTU repair
and the explicit Davis bridge-recreation instruction.

## Deployment contract

Reuse [the existing Davis Compose file](../../pi-gateways/davis/docker-compose.yml)
as its own project `davis`; do not add a second service to the website stack.
The target is amd64; reuse its recorded existing image. The retained Pi image
is arm64 and cannot serve that role. Davis needs TCP to `192.168.1.210:22222`, no
physical-device
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

1. Verify the approved runtime delivery/MTU prerequisite and reuse the existing
   amd64 image while the Pi collector remains running. Build on hvo-docker only
   if that image is missing. The recorded application source is
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

   From that checkout on hvo-docker, use the same file/project for configuration
   validation and startup. Relative paths resolve
   from the first (original Davis) Compose file:

   ```bash
   docker compose --env-file deploy/pi-gateways/davis/.env \
     -f deploy/pi-gateways/davis/docker-compose.yml -p davis config --quiet
   docker image inspect davis-hvo-davis --format '{{.Id}} {{.Architecture}}'
   ```

   For the specifically authorized old-bridge replacement, first preserve the
   stopped target's metadata/data and confirm it is not running. From the
   checkout's `deploy/pi-gateways/davis` directory on hvo-docker:

   ```bash
   docker compose -p davis down
   docker volume inspect davis_davis-outbox
   docker compose -p davis up --no-start --no-build
   docker network inspect davis_default --format '{{json .Options}}'
   ```

   **Never add `-v`.** Confirm `com.docker.network.driver.mtu` is `1450` before
   starting acquisition. `up --no-start` creates the replacement container and
   bridge without running another collector. Do not infer this runbook permits
   unrelated networking, routes, gateway or daemon changes.

2. With the image and runtime prerequisites satisfied, verify the target
   mounted-config directory and
   securely stream `gateway.json` and `secrets/` between the hosts with SSH/tar,
   preserving ownership and modes. Configuration and secret files are mode
   `0600`; the secrets directory is `0700`. Never print, log or commit their
   values, or render them through Compose. Refuse to overwrite unrecognized
   target files; verify recognized retained files before reuse. An in-memory
   content/mode/owner comparison can qualify already copied files without
   printing values or hashes of secrets. Validate
   configuration again before proceeding.
3. Stop `davis-hvo-davis-1` on devpi5 without removing it. Verify it is stopped,
   there are no other outbox writers and the target collector is not running.
   Never run two collectors or a separate WeatherLink acquisition probe.
4. Inventory and checkpoint the stopped volume, retaining `outbox.db`,
   `davis-local.db`, any WAL/SHM companions for **both** databases, and other
   gateway-owned metadata. Preserve the original Pi volume and exclude retained
   `.pre-migration-*` snapshot directories from the current-state transfer.
   Preserve existing target database files by rename before replacement, so no
   old WAL/SHM can mix with the new snapshot. Transfer to the
   target's `davis_davis-outbox` only while both collectors are stopped, excluding
   `outbox.db.old`. Record matching per-file SHA-256 checksums before startup,
   and qualify an isolated restore as specified by the SQLite contract. Never
   open/checkpoint the source database to make a copy easier.
5. Recheck that the Pi is stopped, then start the prebuilt target image:

   ```bash
   docker compose --env-file deploy/pi-gateways/davis/.env \
     -f deploy/pi-gateways/davis/docker-compose.yml -p davis up -d --no-build
   ```

6. Verify actual `/health` state and source freshness as well as Docker health.
   Capture collector acquisition/LOOP log evidence, website `Batch ingest: N
   inserted` lines, and zero new Davis `MinRequestBodyDataRate`/ingest errors.
   Sample protected outbox pending counts and the website's
   `/api/v1/weather/current` **`current.recordDateTime`** over time. Require a
   steadily draining backlog and central observations within a few minutes of
   now; HTTP 200 alone is insufficient. Verify fresh CWOP/WU success timestamps
   and MQTT state publication from the sole target collector. Observe **WU,
   CWOP and MQTT for at least 15 minutes after cutover**, including CWOP's
   scheduled transport attempts after its initial no-reading/backoff. Any
   actual downstream failure/stall triggers rollback; do not declare success
   based on HTTP 200, an initial delivery or a build-only workaround.
7. Only after that full successful observation and all other acceptance checks,
   set
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
