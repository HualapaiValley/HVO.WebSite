# SQLite Gateway Backup And Rollback

This is the canonical backup contract for SmartShunt, Davis and JK BMS cutovers
and shared gateway operations. It defines a **quiescent full-volume archive**,
not a live file copy. SQLite uses WAL; a tar of an active database and its WAL
can contain different transaction states. See SQLite's [backup guidance](https://www.sqlite.org/backup.html)
and [active-transaction corruption warning](https://www.sqlite.org/howtocorrupt.html#_backup_or_restore_while_a_transaction_is_active).
An online SQLite backup is an alternative only after a separate, tested procedure
covers the gateway's other persistent files and recovery requirements.

These instructions are an operator runbook, not deployment authorization. A
documentation change or disposable drill does not establish a production backup.
Never use `docker compose down -v`, delete the current volume, or clear its files
to make an older database fit. Do not print or commit runtime secrets, database
contents or backup archives.

## Ownership And Preparation

| Material | Owning host |
|---|---|
| Named gateway volumes and utility containers | Docker daemon selected by the explicit context; `devpi5` means the Pi, not the workstation |
| `gateway.json`, local secret files and Compose inputs | Deployment workstation; deployment scripts synchronize the mounted runtime files to the daemon host |
| Archive, checksum, manifest and verification logs below | Operator workstation running the shell; the redirected Docker stdout is written here |
| Any absolute source of a Docker bind mount | Docker daemon host, even when the CLI is on another workstation |

Do not bind workstation `/tmp` into a remote utility container and assume the
archive will appear locally. The commands below use named volumes and stdout/stdin
streams, with no backup-directory bind mount. Choose an operator-owned, durable
workstation filesystem with enough free space, directory mode `0700` and files
mode `0600`; `/tmp` and an ephemeral container filesystem are not durable backup
locations. Retain a protected copy on a second durable store under the site's
backup policy and verify its checksum before relying on it for host loss.

Before stopping anything:

1. Confirm the context endpoint/daemon identity, actual container names and named
   volumes from `docker inspect`. Known outboxes are `smartshunt_smartshunt-outbox`,
   `davis_davis-outbox` and `jkbms_jkbms-outbox`; inspect rather than guessing.
   Include gateway metadata/cursor files and any required legacy data-protection
   volume in the checkpoint inventory. Repeat the archive/proof for each volume.
2. Record the current image **ID/digest and reference**, source/device identities,
   schema/runtime contract, volume/mount names, pending/sent/failed counts and
   latest central accepted source timestamps. Preserve the original Compose and
   mounted configuration contract securely; record hashes without exposing secrets.
3. Verify the required central schema migration, protected endpoint and source
   claim are deployed and compatible **before** a new collector starts. Maintain
   exactly one acquisition authority and one canonical writer for each source.
   Stop competing device probes and keep HA exporter mappings for HVO-owned MQTT
   projections disabled. Allow the current queue to drain where possible; retain
   and account for pending/failed records even when it cannot drain.
4. Prepare a trusted utility image containing `tar`, `gzip` and `sqlite3` on the
   selected daemon before the maintenance window. For example, save these lines
   as a workstation-owned `backup-helper.Dockerfile` in an otherwise empty build
   directory, build it on the explicit context, then record its immutable image ID:

   ```dockerfile
   FROM alpine:3.22
   RUN apk add --no-cache sqlite
   ```

   ```bash
   docker --context "$backup_context" build -f "$helper_build_dir/backup-helper.Dockerfile" \
     -t hvo-sqlite-backup-helper:414 "$helper_build_dir"
   backup_helper=$(docker --context "$backup_context" image inspect \
     hvo-sqlite-backup-helper:414 --format '{{.Id}}')
   ```

   Keep the resolved image ID in the manifest and use it below. The stopped-volume
   operation must not depend on downloading tools on an offline gateway.

## Stop, Prove Quiescence And Archive

Set these values from the inspected deployment, in a Bash shell on the operator
workstation. `backup_dir` is an absolute durable path on that workstation;
`backup_helper` is the prepared immutable image ID on the selected Docker daemon.
`checkpoint_id` must be unique for this gateway/attempt. Do not reuse an existing
archive or proof volume name.

```bash
set -euo pipefail
umask 077
: "${backup_context:?explicit Docker context required}"
: "${writer_container:?inspected writer container required}"
: "${source_volume:?inspected existing volume required}"
: "${backup_dir:?durable absolute workstation path required}"
: "${checkpoint_id:?unique checkpoint identifier required}"
: "${backup_helper:?prepared immutable utility image ID required}"
test "${backup_dir#/}" != "$backup_dir"
mkdir -p "$backup_dir"
chmod 0700 "$backup_dir"
archive="$backup_dir/$checkpoint_id.tar.gz"
test ! -e "$archive"
test ! -e "$archive.part"
docker --context "$backup_context" volume inspect "$source_volume" > "$backup_dir/$checkpoint_id.volume.json"
docker --context "$backup_context" inspect "$writer_container" \
  --format '{{.Id}} {{.Config.Image}} {{.Image}}' > "$backup_dir/$checkpoint_id.image.txt"
docker --context "$backup_context" stop --timeout 60 "$writer_container"
test "$(docker --context "$backup_context" inspect "$writer_container" --format '{{.State.Running}}')" = false
test -z "$(docker --context "$backup_context" ps -q --filter "volume=$source_volume")"
```

Hold the maintenance window: no orchestrator, operator or alternate collector may
restart a writer until qualification finishes. Check all volume users, including
host processes/other mounts that Docker's volume filter cannot detect; container
exit alone is insufficient if another process can access the files. Confirm the
device session has been released and no other acquisition/central writer exists.
If that cannot be established, stop here; do not call a raw copy a checkpoint.

Archive the **whole** stopped volume, including any retained `-wal`/`-shm`, gateway
metadata and cursor files. Do not open or checkpoint the source database in the
utility container. A forced process exit may leave committed records in WAL;
keep the files together. Any failed command leaves an unqualified partial attempt.

```bash
docker --context "$backup_context" run --rm --network none \
  --mount "type=volume,src=$source_volume,dst=/source,readonly" "$backup_helper" \
  tar -C /source -czf - . > "$archive.part"
test -s "$archive.part"
test -z "$(docker --context "$backup_context" ps -q --filter "volume=$source_volume")"
tar -tzf "$archive.part" > "$backup_dir/$checkpoint_id.members.txt"
mv "$archive.part" "$archive"
chmod 0600 "$archive"
sync -f "$archive"
sha256sum "$archive" > "$archive.sha256"
sha256sum --check "$archive.sha256"
sync -f "$backup_dir"
```

These commands assume the Linux workstation's GNU `sync` and `sha256sum`. Record
the archive byte size, checksum, UTC capture boundary and all manifest/log paths.
Check the listing includes the expected database and gateway-owned files. A tar
listing and checksum prove transport/readability, not SQLite consistency.

## Disposable Restore Qualification

Choose a **new**, explicitly test-owned proof volume on the same inspected daemon.
It has no gateway, device, network, secrets, exporter or central-ingest connection.
Fail if the name already exists; never extract into the current gateway volume.

```bash
: "${proof_volume:?new test-owned proof volume name required}"
if docker --context "$backup_context" volume inspect "$proof_volume" >/dev/null 2>&1; then
  printf 'Proof volume already exists; choose a new name.\n' >&2
  exit 1
fi
docker --context "$backup_context" volume create --label hvo.backup-proof=true "$proof_volume"
sha256sum --check "$archive.sha256"
docker --context "$backup_context" run --rm -i --network none \
  --mount "type=volume,src=$proof_volume,dst=/restore" "$backup_helper" \
  tar -C /restore -xzf - < "$archive"
docker --context "$backup_context" run --rm --network none \
  --mount "type=volume,src=$proof_volume,dst=/restore" "$backup_helper" \
  sh -ec 'test -s /restore/outbox.db; test "$(sqlite3 -readonly /restore/outbox.db "PRAGMA integrity_check; PRAGMA foreign_key_check;")" = ok; sqlite3 -readonly /restore/outbox.db "PRAGMA user_version; SELECT COUNT(*) FROM OutboxRecords;"' \
  > "$backup_dir/$checkpoint_id.sqlite-proof.txt"
```

The isolated clone is writable for SQLite WAL/shared-memory coordination; the
original archive and gateway volume stay untouched. Require `integrity_check`
to return exactly `ok` and `foreign_key_check` to return no rows for **every**
SQLite database in the volume, using its actual relative path. The example uses
`outbox.db`; inventory additional databases explicitly rather than assuming there
are none. Compare schema, pending/sent/failed counts and source timestamp bounds
with the captured stopped-state evidence using schema-compatible, payload-free
queries. Pre-stop diagnostics are a continuity reference, not an exact final-row
count: record the stopped snapshot's counts and timestamp boundary from this
clone, reconcile any final shutdown rows with central history, and retain that
snapshot inventory with the checkpoint. Verify gateway metadata/cursors and
required companion volumes too.
Run the preserved binary's schema-compatibility check against a disposable clone
only when a documented offline mode guarantees no acquisition or forwarding;
otherwise record compatibility as unproven and do not promise legacy restart.

Only after these checks and the durable archive/manifest are complete may the
operator mark the checkpoint **usable**, naming the proven scope and binary/schema
limitations. Do not restart a utility clone as a gateway. Cleanup may remove only
the individually named proof volumes/containers created by this attempt, after
their ownership and recorded results are checked. Keep the durable archive.

## Preserve Current State During Rollback

1. Stop the candidate writer/acquisition process and prove the same quiescence.
   Keep the alternate authority disabled. Never start two collectors to compare
   rollback behavior.
2. Capture a new verified archive of the **current** volume using this contract
   before any schema or mount change. Keep the original named volume intact with
   all post-checkpoint observations, pending/failed rows and gateway metadata.
3. Prefer the preserved image against the current volume only when its schema
   compatibility is established. If incompatible, restore the qualified earlier
   archive to a **new** recovery volume, as above. Do not overwrite/clear the
   current volume. A separately reviewed operator Compose/runtime override must
   explicitly mount that recovery volume at `/app/data` and preserve required
   companion mounts; this documentation does not change the shipped Compose file.
4. Compare both snapshots with central history by source/device/timestamp and
   strict per-record outcome accounting. Identify every post-checkpoint record
   absent from the older clone. Keep a documented, source-compatible recovery
   plan for those records before enabling forwarding; an older checkpoint alone
   cannot deliver them. Do not relabel missing records as duplicates, casually
   merge SQLite schemas, or discard current pending/failed observations. Where
   replay cannot yet be proven, preserve the evidence and report rollback/history
   recovery incomplete rather than silently losing the interval.
5. Confirm the central endpoint/schema/source claim remains compatible, then
   enable exactly one preserved acquisition authority and canonical writer.
   Verify device ownership, health, fresh central source timestamps, queue drain,
   inserted/skipped/failed counts and MQTT presentation. Container liveness alone
   does not prove recovery. Keep the failed candidate stopped and all original
   volumes/archives until separate retention/deletion authorization.

## Historical And Future Migrations

The SmartShunt #326, Davis #330 and JK #328/#356 vNext runbooks describe migrations
to the existing direct collectors. Their historical evidence remains in those
runbooks; this stricter checkpoint contract does not retroactively claim that
every historical archive received all these checks.

Future [SmartShunt native-HA migration #352](https://github.com/HualapaiValley/HVO.WebSite/issues/352)
has separate field/sign/cadence parity, exporter-contract, source-authority and soak
acceptance. A read-only native passive shadow must never become a competing source
writer. At cutover, drain/account for the direct queue, stop the direct collector,
prove quiescence and qualify its backup before enabling any exporter mapping. On
rollback, disable/stop the failed mapping and release its central authority before
re-enabling the direct writer. Keep exactly one selected acquisition authority
and canonical writer; no #352 commissioning, exporter enablement or volume removal
is authorized by these documentation changes.
