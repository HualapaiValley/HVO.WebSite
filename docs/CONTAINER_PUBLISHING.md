# Container Publishing

Publishing builds and pushes an image; deployment is a separate authorized
operation. Current supported publish targets are website, Davis, JK BMS and
SmartShunt. EG4 is built through the Pi deployment workflow. The HA exporter is
implemented but disabled in production; retired SolarAssistant/Kasa images are
not targets.

## Publishing configuration

[publish-image.sh](../scripts/publish-image.sh) reads allowlisted publishing keys
from **`deploy/hvo-docker/.env` by default**, or `HVO_PUBLISH_ENV_FILE` when set.
It treats that file as dotenv data, without shell expansion/command substitution.
Azure Key Vault `hvo-central-kv` is authoritative for credentials. First-install
materialization and the separate website runtime identity are described in the
[website deployment entry](../deploy/hvo-docker/README.md).

| Target | Intended repository | Dockerfile | Independent version setting |
|---|---|---|---|
| `website` | `hvo-website` | `src/HVO.WebSite.v9/Dockerfile` | `HVO_WEBSITE_IMAGE_VERSION` |
| `davis` | `hvo-davis` | `src/HVO.Hardware.DavisVantagePro2/Dockerfile` | `HVO_DAVIS_IMAGE_VERSION` |
| `jkbms` | `hvo-jkbms` | `src/HVO.Hardware.JkBms/Dockerfile` | `HVO_JKBMS_IMAGE_VERSION` |
| `smartshunt` | `hvo-smartshunt` | `src/HVO.Hardware.VictronSmartShunt/Dockerfile` | `HVO_SMARTSHUNT_IMAGE_VERSION` |

Set each `HVO_<TARGET>_IMAGE_REPOSITORY` explicitly to the intended repository:
the script falls back to the invocation target string, which is not necessarily
the `hvo-*` name. Also configure `HVO_CONTAINER_REGISTRY_LOGIN_SERVER`,
`HVO_CONTAINER_REGISTRY_USERNAME` and `HVO_CONTAINER_REGISTRY_PASSWORD` in the
selected ignored file. The current registry DNS name is
`registry.hualapaivalleyobservatory.org`; verify its deployment/transport before
publishing, rather than treating the unauthenticated shared-stack candidate as
the current registry.

`--tag` overrides the version setting; absent both, the script generates a
timestamp. Use an explicit reviewed version for reproducibility. `--push-latest`
also updates the mutable `latest` tag and needs that release intent. The current
Dockerfile/publisher does not consume `WEBSITE_RUNTIME`.

Historical failure lesson (observation date unrecorded): the
[original publishing notes](https://github.com/HualapaiValley/HVO.WebSite/blob/8a27b3f512ce8ef6425b93b6c7f52dea16df7bcb/docs/CONTAINER_PUBLISHING.md)
reported a malformed repository such as `hvo-websiteatest` while constructing a
`latest` reference. Preserve braces around repository variables when assembling
tags. The current [publisher](../scripts/publish-image.sh) uses explicit braced
components for both version and latest references; this is retained historical
failure context, not a newly observed publisher defect or publish proof.

## Authorized publish sequence

1. Select and review the actual publish env file, repository and image version.
2. Compare its managed credentials with Key Vault. `sync-secrets-from-keyvault.sh`
   defaults to read-only drift detection; `--apply` materializes approved local
   files and does not rotate credentials in Key Vault. Establish the
   [root-bootstrap prerequisites and whole-helper scope](development/key-vault-materialization.md)
   first; also review the
   [gateway secret exceptions](../deploy/pi-gateways/README.md#mounted-configuration-and-secrets).
   A custom `HVO_PUBLISH_ENV_FILE` is not automatically synchronized.
3. Inspect the commands with `--dry-run`, then publish only the intended target.
4. Independently verify the pushed tag/digest. The script prints commands/status;
   it does not perform a registry readback or deploy the image.
5. Record the actual image/release change in [CHANGELOG.md](../CHANGELOG.md).

For an already configured deployment env file, after satisfying the shared
[materialization prerequisites](development/key-vault-materialization.md):

```bash
./scripts/sync-secrets-from-keyvault.sh --check
./scripts/publish-image.sh --dry-run website
# Separate authorization for materialization/build/push:
./scripts/sync-secrets-from-keyvault.sh --apply
./scripts/publish-image.sh website
```

The selected Docker daemon builds; publishing authenticates with password-stdin.
Never print the credential file or rendered Compose environment. Inspect the
current context before a build. No publish command is required for documentation
validation.

## Root bootstrap gist

[sync-env-gist.sh](../scripts/sync-env-gist.sh) caches **root `.env`**, not
`deploy/hvo-docker/.env` or every deployment file, in private gist
`f343db002d980ebe5fcc51413b0b7227`. Both modes require an existing root `.env`
and available `jq`/`curl`. `--dry-run` only previews the intended update: it
skips Key Vault drift checking, token resolution and upload. Its success does
not prove synchronized or usable credentials.

The real update first runs the global helper's `--check`, with the
[root-bootstrap and vault prerequisites](development/key-vault-materialization.md),
and stops on drift/failure. It then resolves a GitHub token from `GH_PAT`,
`GH_TOKEN`, `GITHUB_TOKEN`, `gh auth token`, or `git credential fill`, in that
order. A usable token with private-gist update access is required; authentication
through `gh` is one supported route. The real command uploads the entire root
file to the private gist; it does not synchronize deployment files.

```bash
./scripts/sync-env-gist.sh --dry-run
# Writes the private gist after approved root-bootstrap changes:
./scripts/sync-env-gist.sh
```

Changing only deployment publish metadata does not require copying deployment
credentials into root `.env`. Synchronize the gist only when its root-bootstrap
content changed intentionally. The gist is a recovery cache, never secret
authority; key-ring XML and recovery archives do not belong there.

## Deployment handoff

Use [website deployment](../deploy/hvo-docker/README.md) or
[Pi gateway commissioning](../deploy/pi-gateways/README.md), with the tested tag,
configuration, mounted secrets, checkpoint and rollback owner. Website `--tag`
selects an image already present on the target daemon and does not issue an
explicit pull; publishing alone does not prove availability there. Preserve the
Data Protection volume and gateway stores. `check-deployments.sh` queries live
targets after an authorized rollout; it is not an offline PR check.

The former `publish-acr-image.sh` workflow is historical. Its Azure context is
retained in the [former ACA record](archive/website-container-app.md).
