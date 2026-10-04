# Self-hosted website deployment

This is the current checked-in website deployment entry point. The website
serves the dashboard and central ingest API on `hvo-docker`; Pi collectors post
to its internal API. Read [publishing](../../docs/CONTAINER_PUBLISHING.md),
[Data Protection](../../docs/WEBSITE_DATA_PROTECTION.md) and
[infrastructure/recovery ownership](../../docs/SHARED_INFRASTRUCTURE.md) before an
authorized rollout. No deployment is implied by repository validation.

## Prerequisites and configuration

- Docker CLI/Compose, the authorized `hvo-docker` SSH context and target access.
- Existing external networks `website_default` and `mssql_default`. Compose
  does not create them. The website currently resolves SQL as `mssql`; moving
  to the shared-stack candidate's `hvo-infrastructure` requires a separate
  reviewed connectivity/data migration.
- Reachable SQL Server/database and the intended connection string. Startup
  seeding runs EF migrations; authorize and checkpoint that actual target before
  starting a replacement. Schema authority is `HVO.DataModels`; an env template
  is not permission to migrate production data.
- Dedicated website runtime identity with secret-read access to `hvo-central-kv`
  and wrap/unwrap access to the configured Key Vault Data Protection key.
  Key Vault is loaded at startup; `AzureAd:*` configuration must describe the
  registered Entra application/redirect URIs for the external host.
- Durable `hvo-website-data-protection` volume, restricted checkpoint and a
  recovery owner. Preserve `DataProtection__ApplicationName=HVO.WebSite.v9`
  and access to every older protector still needed by retained keys.
- A reviewed image available on the target Docker daemon, or explicitly approved
  target build. Publishing a registry tag does not prove target availability.

From the repository root, initialize only on **first install**. Existing files,
including symlinks, stop this sequence; do not overwrite an operator's settings:

```bash
set -euo pipefail
test ! -e deploy/hvo-docker/.env && test ! -L deploy/hvo-docker/.env
(umask 077; set -o noclobber; cat deploy/hvo-docker/.env.example > deploy/hvo-docker/.env)
chmod 600 deploy/hvo-docker/.env
```

Review non-secret host/database/image values in the ignored file. Before the
global sync helper below, establish the
[existing root bootstrap, sourced SSH field, parsable root/website SQL strings
and whole-helper vault/materialization scope](../../docs/development/key-vault-materialization.md).
Creating this website file alone is insufficient, and `--apply` does not create
a complete root bootstrap. Then, with the approved prerequisites satisfied:

```bash
./scripts/sync-secrets-from-keyvault.sh --check
# Local file changes only after reviewing drift:
./scripts/sync-secrets-from-keyvault.sh --apply
```

Synchronization populates `AZURE_CLIENT_*` from `WebsiteRuntime--*`, distinct
from the developer identity. It does not rotate vault credentials. Managed
website files must already exist; custom `HVO_WEBSITE_ENV_FILE` (or legacy
`HVO_WEBSITE_DEPLOY_ENV_FILE`) locations need explicit secure materialization.
Never dump the env file, rendered Compose environment or key-ring XML.

## Safe configuration checks

Shell environment overrides `--env-file` during Compose interpolation. Remove
unintended exports; the deploy helper warns about them. `config --quiet` proves
syntax/interpolation validity, not the expected effective values. Inspect only
non-secret facts with an assertion that emits no rendered values:

```bash
set -euo pipefail
docker compose --env-file deploy/hvo-docker/.env \
  -f deploy/hvo-docker/docker-compose.yml config --quiet
docker compose --env-file deploy/hvo-docker/.env \
  -f deploy/hvo-docker/docker-compose.yml config --format json |
  jq -e '.services["hvo-website"] as $s |
    $s.environment.ASPNETCORE_URLS == "http://+:8080" and
    $s.environment.DataProtection__ApplicationName == "HVO.WebSite.v9" and
    $s.environment.DataProtection__KeysDirectory == "/root/.aspnet/DataProtection-Keys" and
    $s.environment.DataProtection__BlobUri == "" and
    any($s.volumes[]; .type == "volume" and .source == "hvo-website-data-protection" and
      .target == "/root/.aspnet/DataProtection-Keys") and
    .networks.website.name == "website_default" and .networks.mssql.name == "mssql_default"' >/dev/null
```

These local render checks do not contact a production Docker daemon, verify SQL
or attest live identity/key access. Do not redirect the full JSON into logs or
artifacts: it contains secrets.

## Authorized rollout and rollback

The checked-in [Compose](docker-compose.yml) listens on HTTP 8080, published as
host port 80, behind the host's separately managed TLS/proxy boundary. Its probe
is `/health/live`; assess `/health/ready` and actual central ingest separately.
The image contains no baked TLS certificate. If local HTTPS is required, mount
the certificate at runtime with secret-backed Kestrel settings.

After the configuration/identity/data/key-ring prerequisites and approved
checkpoint are satisfied, select an image that is already present on the target:

```bash
./scripts/deploy-hvo-website.sh --dry-run --context hvo-docker --tag <tested-image-tag> --no-build
# Recreates the website, waits, and verifies container/key-ring policy:
./scripts/deploy-hvo-website.sh --context hvo-docker --tag <tested-image-tag> --no-build
./tools/verify-website-data-protection.sh hvo-docker
```

`--tag` sets an image override and removes the build definition; the script
does not explicitly pull that tag. Without `--tag`, it builds unless `--no-build`
is selected. The base Compose has a build definition but no explicit image
tag, so `--no-build` alone is not a registry-tag selector. An approved pull, if
needed, must target the same daemon before rollout.

Verify readiness, protected browser/session continuity and bounded ingest after
rollout. Roll back only the image/configuration that was changed, preserving the
key-ring and database. The [Data Protection runbook](../../docs/WEBSITE_DATA_PROTECTION.md)
governs first plaintext-ring migration and restricted rollback archives. Never
use `down -v` or broad volume pruning. The
[former ACA record](../../docs/archive/website-container-app.md) preserves its
identity/blob/wrapping-key evidence separately.

## Other host stacks

[shared-infrastructure/compose.yaml](shared-infrastructure/compose.yaml) and
[observability/compose.yaml](observability/compose.yaml) are separately owned
deployment candidates; do not launch them beside existing services on the same
ports or with empty replacement stores. Follow the dated-state qualifications,
storage guards, application-aware migration and recovery gaps in
[shared infrastructure](../../docs/SHARED_INFRASTRUCTURE.md).
