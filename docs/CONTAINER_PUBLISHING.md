# Container Publishing

Publishing builds and pushes an image; deployment is a separate authorized
operation. Davis, JK BMS, SmartShunt and EG4 use CI multi-arch publishing below;
the website retains its manual publisher. The HA exporter is
implemented but disabled in production; retired SolarAssistant/Kasa images are
not targets.

## Publishing configuration

This section describes the legacy manual publisher, which supports website,
Davis, JK BMS and SmartShunt. Active collector release publishing uses CI;
EG4 is supported by its CI workflow, not by this manual script.

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

### Davis CI publishing

The [Publish Davis image workflow](../.github/workflows/publish-davis.yml) builds
`src/HVO.Hardware.DavisVantagePro2/Dockerfile` with Buildx and publishes
`registry.hualapaivalleyobservatory.org/hvo-davis`. Website publishing remains
the explicit-version `publish-image.sh` path described below; it has no current
release-tag CI workflow. Davis retains its existing `hvo-davis` repository and
registry credential names, adding required semver and multi-arch readback.

After this workflow is on `main`, either dispatch it **on main** with a new
`version` (for example `1.0.0-rc.1`), or push a `davis-v<version>` tag. Stable
release tags must point to a commit contained in main. For explicit publication
qualification while a PR remains draft, the owner may push a **prerelease** tag
(for example `davis-v0.0.0-ci.464.1`) on the independently reviewed candidate.
This runs the tag's workflow without a merge, ready transition or deployment;
it never publishes a stable version from an unmerged branch. Versions use semver without `+build` metadata, which Docker
tags cannot represent. The workflow rejects invalid sources, missing credentials,
existing image versions and registry errors; it never moves `latest`. Publications
are serialized, but the registry itself must restrict other writers to preserve
version tags. The digest is the immutable deployment identity.

Prerequisites for the repository owner:

- Configure Actions secrets `HVO_CONTAINER_REGISTRY_USERNAME` and
  `HVO_CONTAINER_REGISTRY_PASSWORD` from the existing approved Key Vault registry
  credentials. Never paste values into an issue, PR, workflow or log. This workflow
  does not materialize the global secret helper or need collector secrets.
- Permit this repository to use the organization `hvo-linux-x64` runner group.
  The workflow selects `[self-hosted, Linux, X64, hvo-website, docker]`. The
  2026-10-29 inspection found six online AMD/x64 runners (`gh-runner-01` through
  `06`). The `ARM64`, `pi5`, `hvo-skymonitor-arm64` runner is in a group selected
  only for SkyMonitor, so Davis uses QEMU arm64 emulation on the AMD runner.
  Native ARM access would require a separately reviewed owner configuration change.
- Ensure the runner reaches the HTTPS registry and Microsoft/Docker/NuGet sources.
  The workflow prints link MTUs and default bridge options before building.
  hvo-docker's eth0 1450 / Docker bridge 1500 mismatch was a prior stall cause;
  investigate the same mismatch on a stalled runner. Do not change host networking
  or use a target-host build as a workaround.

Buildx builds `linux/amd64` and `linux/arm64` into a single versioned index. Registry
readback requires exactly those two platforms and valid child digests, then checks
the version tag resolves to the build's index digest. The run summary records the
source SHA and complete `hvo-davis:<version>@sha256:<digest>` reference. No collector
starts during publishing. A failed post-push verification can leave a version in
the registry: investigate it and use a new version rather than overwriting it.

Use the [Davis registry-pull handoff](../deploy/pi-gateways/davis/README.md) for
hvo-docker or an authorized Pi fallback. **Never build on hvo-docker or home-docker.**
The historical host-network build overlay from draft PR #463 is retired; its
one-off image remains historical evidence, not the deployment build path.

### Pi collector CI publishing

JK BMS, SmartShunt and EG4 follow the Davis workflow above with the same
read-only repository permission, runner labels, pinned Actions/BuildKit/QEMU,
main-only manual dispatch, stable-source ancestry check, unused-version check,
two-architecture build and registry readback. Each collector has its own
publication concurrency group and image repository:

| Workflow | Tag | Registry repository | Dockerfile project |
|---|---|---|---|
| [Publish JK BMS image](../.github/workflows/publish-jkbms.yml) | `jkbms-v<semver>` | `hvo-jkbms` | `HVO.Hardware.JkBms` |
| [Publish SmartShunt image](../.github/workflows/publish-smartshunt.yml) | `smartshunt-v<semver>` | `hvo-smartshunt` | `HVO.Hardware.VictronSmartShunt` |
| [Publish EG4 image](../.github/workflows/publish-eg4.yml) | `eg4-v<semver>` | `hvo-eg4` | `HVO.Hardware.Eg4` |

All use `registry.hualapaivalleyobservatory.org` and the existing Actions registry
credentials described above. No collector/device secret is needed. The existing
self-hosted `[self-hosted, Linux, X64, hvo-website, docker]` runner builds both
linux/amd64 and linux/arm64 via QEMU; no Pi/docker-host image build is permitted.
No runner-group or Docker-daemon configuration change is part of publishing.

Push release tags individually when qualifying all four collectors. GitHub does
not emit tag push events for a push containing more than three tags, even when
the refs were successfully created. A successful Git push alone is not evidence
that a publication workflow started; require the matching source/tag Actions run.

After the workflows merge, dispatch the selected workflow **on main** with a new
semver `version`, or push its version tag at an authorized commit. Stable tags
must be contained in main. For #466 the owner specifically permits these three
prerelease qualification tags at the draft candidate's exact SHA (replace the
placeholder with that recorded SHA; never retag an existing publication):

```bash
candidate=REPLACE_WITH_CANDIDATE_SHA
git tag jkbms-v0.0.0-ci.466.1 "$candidate"
git tag smartshunt-v0.0.0-ci.466.1 "$candidate"
git tag eg4-v0.0.0-ci.466.1 "$candidate"
git push https://github.com/HualapaiValley/HVO.WebSite.git \
  refs/tags/jkbms-v0.0.0-ci.466.1 \
  refs/tags/smartshunt-v0.0.0-ci.466.1 \
  refs/tags/eg4-v0.0.0-ci.466.1
```

Require a successful run for each tag and record its source, version and
multi-arch index digest. A failure after push can leave an occupied registry
version; investigate rather than overwrite it. The workflow verifies exactly
the two Linux platforms and checks the version tag still resolves to that
index. It does not move `latest`, pull to a deployment host or start collectors.
See [registry pins, verification and rollback](../deploy/pi-gateways/README.md#collector-registry-publishing-and-pins)
for the Compose/env handoff. Publishing qualification is separate from PR
review/standard CI and from future deployment authorization.

### Existing manual publisher

1. Select and review the actual publish env file, repository and image version.
2. Compare its managed credentials with Key Vault. `sync-secrets-from-keyvault.sh`
   defaults to read-only drift detection; `--apply` materializes approved local
   files and does not rotate credentials in Key Vault. Establish the
   [root-bootstrap prerequisites and whole-helper scope](development/key-vault-materialization.md)
   first; also review the
   [gateway secret exceptions](../deploy/pi-gateways/README.md#mounted-configuration-and-secrets).
   A custom `HVO_PUBLISH_ENV_FILE` is not automatically synchronized.
3. Inspect the commands with `--dry-run`, then publish only the intended target
   on an approved build machine. Active collector publishing uses CI above.
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
