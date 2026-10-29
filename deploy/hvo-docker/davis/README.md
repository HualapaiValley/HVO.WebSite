# Davis collector placement on hvo-docker

Issue [#462](https://github.com/HualapaiValley/HVO.WebSite/issues/462) authorizes
moving the single Davis collector from Wi-Fi-only `devpi5` to `hvo-docker`, beside
the website and SQL. **The migration is not complete.** The 2026-10-29 attempt
stopped during preparation; the Pi remains the acquisition authority. See the
issue comments for the build results and live evidence before resuming.

## Preparation result on 2026-10-29

- Exact SDK `10.0.402` local locked restore failed with `NU1403` for
  `HVO.Core.1.1.2`. The full solution build reported zero warnings and four
  errors for that package's content-hash mismatch. Required full non-live
  tests are blocked; the lock files and packages were not changed.
- Target Compose configuration validation passed. The amd64 build stalled
  fetching Ubuntu package indexes and reported a connection failure to
  `security.ubuntu.com:80`. It was cancelled (exit 130) once local validation
  blocked cutover. No completed target image or running target collector was
  produced. The failure does not authorize networking or dependency changes.
- No configuration/secrets or outbox were transferred. The original Pi
  container remains running with `restart=unless-stopped`; `restart=no` is
  appropriate only after successful migration. No rollback copy is needed
  because the target never acquired records. Acceptance remains open on #462.

## Deployment contract

Reuse [the existing Davis Compose file](../../pi-gateways/davis/docker-compose.yml)
as its own project `davis`; do not add a second service to the website stack.
The target is amd64, so rebuild current `main` on hvo-docker: the retained Pi
image is arm64. Davis needs TCP to `192.168.1.210:22222`, no physical-device
mapping, privilege or host networking. Use LAN SSH aliases `hvo-docker` and
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

1. Resolve the reported build prerequisites before another cutover attempt.
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

   From that checkout on hvo-docker, use the same file/project for build,
   configuration validation and later startup:

   ```bash
   docker compose --env-file deploy/pi-gateways/davis/.env \
     -f deploy/pi-gateways/davis/docker-compose.yml -p davis config --quiet
   docker compose --env-file deploy/pi-gateways/davis/.env \
     -f deploy/pi-gateways/davis/docker-compose.yml -p davis build
   ```

2. After a successful build, create the target mounted-config directory and
   securely stream `gateway.json` and `secrets/` between the hosts with SSH/tar,
   preserving ownership and modes. Configuration and secret files are mode
   `0600`; the secrets directory is `0700`. Never print, log or commit their
   values, or render them through Compose. Refuse to overwrite unrecognized
   target files. Validate configuration again before proceeding.
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
     -f deploy/pi-gateways/davis/docker-compose.yml -p davis up -d --no-build
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

On an unexpected build, health or ingest failure, stop and report rather than
changing dependencies, batching, networking or gateway settings to force a move.
Before cutover, leave the original Pi collector running; no target data exists
to restore. After target startup, stop it first and prove quiescence. Preserve
its current state and, if it acquired unsent records, copy its stopped outbox
back to the Pi as required by #462 and the preservation/schema rules in the
SQLite contract. Do not discard later records or overwrite an unqualified
snapshot. Then restore Pi `restart=unless-stopped` and start only the Pi
collector. Retain all containers, images, volumes and checkpoints, and report
the verification result on the issue.
