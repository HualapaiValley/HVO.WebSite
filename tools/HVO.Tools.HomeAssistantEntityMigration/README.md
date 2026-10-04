# HVO.Tools.HomeAssistantEntityMigration

This console uses supported Home Assistant WebSocket APIs for the managed Davis entity-ID migration and off-grid Energy preferences/audit. [Program.cs](Program.cs) owns CLI/environment handling; [the project](HVO.Tools.HomeAssistantEntityMigration.csproj) references Edge.HomeAssistant.Mqtt and copies the [entity manifest](davis-readable-ids.json) and [Energy manifest](hvo-energy-preferences.json) to its output. It does not edit HA `.storage` files or acquire physical telemetry.

## Preparation and tests

Use the exact SDK in [global.json](../../global.json), from the repository root:

```bash
dotnet restore HVO.WebSite.sln --locked-mode --nologo
dotnet build tools/HVO.Tools.HomeAssistantEntityMigration/HVO.Tools.HomeAssistantEntityMigration.csproj --no-restore --nologo
```

[Tool tests](../../tests/README.md#hvotoolshomeassistantentitymigrationtests) use fake clients for non-live checks and the owned HA fixture for separately provisioned Integration/HomeAssistantIntegration tests. The repository-root working directory also matters for operational entity checks: `Program.cs` searches `deploy/home-assistant/configuration` for tracked references and writes rollback manifests under `artifacts/home-assistant`. Run the full solution build before focused `--no-build` test commands.

## Connection prerequisites

An authorized HA read or mutation requires `HOME_ASSISTANT_TOKEN` for an appropriate administrator/service identity and explicit absolute `HVO_HOME_ASSISTANT_URL`. HTTPS maps to WSS `/api/websocket`. HTTP is rejected unless `HVO_HOME_ASSISTANT_ALLOW_INSECURE=true` explicitly permits token transmission on a trusted local network; do not bypass certificate trust. Follow [HA service identity and transport prerequisites](../../deploy/home-assistant/README.md#core-deployment), not example tokens in source. Entity/Energy manifests pin the expected HA version, and runners reject mismatches. No production token is needed for fake-client tests.

## CLI modes and effects

```text
HVO.Tools.HomeAssistantEntityMigration --check|--apply [manifest-path]
HVO.Tools.HomeAssistantEntityMigration --energy-check|--energy-apply [manifest-path]
HVO.Tools.HomeAssistantEntityMigration --energy-audit
HVO.Tools.HomeAssistantEntityMigration --backup
HVO.Tools.HomeAssistantEntityMigration --rollback <backup-path> [manifest-path]
```

| Mode | Contract |
|---|---|
| `--check` | Reads the registry, related/Lovelace/tracked-config references; rejects identity/version/disabled-state/collision/reference mismatches. Does not rename or create a backup. |
| `--energy-check` | Reads state/statistics/preferences and validates managed entities, equivalence and HA Energy validation; does not save preferences. |
| `--energy-audit` | Reads timezone/current state/statistics and reports Energy evidence; no preference write or backup request. |
| `--apply` | Creates an HA backup and a new local rollback manifest before reference blocking/renames; renames through the registry API and verifies identity/group/disabled-state preservation. A blocked run may already have created a backup. |
| `--energy-apply` | Saves managed Energy preferences while preserving supported unmanaged entries; verifies persistence/validation and attempts bounded restoration of previous preferences on failure. It does not automatically create the `--backup` recovery checkpoint. |
| `--backup` | Requests an HA backup from all available configured agents and waits for reported completion; prints the verified backup ID. This is a mutation. |
| `--rollback` | Validates a supplied entity rollback manifest against the pinned migration/version, checks current identities/collisions, renames back through the API and verifies the result. It is not a full HA backup restore or Energy rollback command. |

Check/audit modes contact the configured HA and are distinct from offline manifest/unit checks. Apply/backup/rollback need applicable operational authorization; their presence is not routine development permission. The [entity runner](EntityMigrationRunner.cs) does not make a multi-entity rename transactional or automatically undo every partial rename failure; preserve its rollback manifest and use the approved recovery procedure.

The [backup client](HomeAssistantRegistryClient.cs) checks HA-reported completion and agent errors. A returned ID does not export an archive, prove its actual contents/storage destinations, recreate mounts/schedules or establish an off-host restore drill. [HA recovery/backup guidance](../../deploy/home-assistant/README.md#energy-preferences) owns those additional checks.

Use the [Davis readable-ID procedure](../../docs/home-assistant/davis-readable-id-migration.md) for migration references and the [Energy procedure](../../deploy/home-assistant/README.md#energy-preferences) for preferences/audit/recovery acceptance. Passing disposable fixtures does not prove production migration or deployment success.
