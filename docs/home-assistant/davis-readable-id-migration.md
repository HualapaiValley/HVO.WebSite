# Davis readable entity-ID migration

This migration changes the 39 Davis user-facing Home Assistant entity IDs from
their encoded defaults to `sensor.davis_*`. MQTT `unique_id` values, discovery
topics, state topics, availability, device grouping, units, device classes,
state classes, and enabled-by-default settings do not change.

Home Assistant history begins at this cutover. Existing HVO databases remain
canonical history. Do not replay historical observations over MQTT and do not
edit Recorder or `.storage` databases directly.

## Prerequisites

- Home Assistant Core is exactly `2026.8.1`.
- The Davis collector is publishing all 39 discovery entries.
- An administrator long-lived token is available as `HOME_ASSISTANT_TOKEN`.
- The Home Assistant backup integration has an available backup agent and its
  restore path has been verified.
- Tracked dashboards and configuration contain no old Davis entity IDs.

The migration tool uses only supported authenticated WebSocket commands:
`config/entity_registry/list`, `config/entity_registry/update`,
`search/related`, `lovelace/config`, `backup/agents/info`, `backup/generate`, and
`backup/info`. It never reads or writes Home Assistant `.storage` directly.

## Preflight

Run from the repository root:

```bash
HVO_HOME_ASSISTANT_URL=http://192.168.1.113 \
HVO_HOME_ASSISTANT_ALLOW_INSECURE=true \
HOME_ASSISTANT_TOKEN=<administrator-token> \
dotnet run --project tools/HVO.Tools.HomeAssistantEntityMigration -- --check
```

Preflight fails unless:

- the registry contains exactly one MQTT entity for every expected stable
  Davis `unique_id`;
- every entity is at either its old encoded ID or its intended readable ID;
- all 39 targets are collision-free;
- automations, scripts, scenes, groups, people, and Lovelace dashboards contain
  no references to an old ID.

`HVO_HOME_ASSISTANT_URL` is mandatory. Prefer HTTPS. The explicit
`HVO_HOME_ASSISTANT_ALLOW_INSECURE=true` opt-in is required for the trusted
local HTTP deployment because its administrator token is not protected by
transport encryption.

If references are reported, run `--apply` once before editing them. It creates
a pre-reference HA configuration/database backup plus ignored rollback manifest, then stops
without renaming any entity. Update every reported reference through Home
Assistant's supported editor or the tracked YAML source and rerun `--check`.
Do not proceed with unresolved references; retain the first backup/manifest for
a full rollback of both registry IDs and references.

## Apply

```bash
HVO_HOME_ASSISTANT_URL=http://192.168.1.113 \
HVO_HOME_ASSISTANT_ALLOW_INSECURE=true \
HOME_ASSISTANT_TOKEN=<administrator-token> \
dotnet run --project tools/HVO.Tools.HomeAssistantEntityMigration -- --apply
```

Before the first rename, the tool creates a fresh Home Assistant configuration/database backup
through `backup/generate` and writes its backup ID plus an ignored reverse
manifest under `artifacts/home-assistant/`. Keep that file with the HA backup
until migration acceptance is complete. Apply is resumable: entities already
at their target are verified and skipped.

After apply, restart Home Assistant and the Davis collector. Rerun `--check`
and verify:

- 39 unique Davis registry entries and no encoded duplicates;
- all 27 enabled-by-default entities are available when their telemetry is
  known;
- all 12 advanced/raw entries remain disabled by default;
- current state continues updating;
- Home Assistant logs contain no MQTT discovery or registry errors.

## Rollback

Use the ignored reverse manifest produced by apply:

```bash
HVO_HOME_ASSISTANT_URL=http://192.168.1.113 \
HVO_HOME_ASSISTANT_ALLOW_INSECURE=true \
HOME_ASSISTANT_TOKEN=<administrator-token> \
dotnet run --project tools/HVO.Tools.HomeAssistantEntityMigration -- \
  --rollback artifacts/home-assistant/davis-readable-ids-v1-<timestamp>.json
```

Rollback verifies each unchanged `unique_id`, renames the entity back through
the supported registry API, and verifies all old IDs. Restore the full HA
backup if registry or reference reconciliation is incomplete. Canonical HVO
history is not changed by apply or rollback.
