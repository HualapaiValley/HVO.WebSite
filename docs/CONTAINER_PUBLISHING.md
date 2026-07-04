# Container Publishing

This repository publishes six application images to the self-hosted Docker registry on `hvo-docker`, and each image is versioned independently.

## Registry Inventory

Current container publishing target:

| Item | Value |
|------|-------|
| Host | `hvo-docker` |
| Registry container | `registry:2` |
| Login server | `registry.hualapaivalleyobservatory.org` |
| Active publish script | `scripts/publish-image.sh` |

The repo-local source of truth for these settings is `.env`, and the devcontainer bootstrap gist must stay aligned with that file.

## Image Repositories

The published repositories are:

| Target | Repository | Dockerfile | Version variable |
|--------|------------|------------|------------------|
| Website | `hvo-website` | `src/HVO.WebSite.v9/Dockerfile` | `HVO_WEBSITE_IMAGE_VERSION` |
| Davis | `hvo-davis` | `src/HVO.Hardware.DavisVantagePro2/Dockerfile` | `HVO_DAVIS_IMAGE_VERSION` |
| JK BMS | `hvo-jkbms` | `src/HVO.Hardware.JkBms/Dockerfile` | `HVO_JKBMS_IMAGE_VERSION` |
| SolarAssistant | `hvo-solarassistant` | `src/HVO.Gateway.SolarAssistant/Dockerfile` | `HVO_SOLARASSISTANT_IMAGE_VERSION` |
| SmartShunt | `hvo-smartshunt` | `src/HVO.Hardware.VictronSmartShunt/Dockerfile` | `HVO_SMARTSHUNT_IMAGE_VERSION` |
| TP-Link/Kasa | `hvo-tplinkkasa` | `src/HVO.Gateway.TplinkKasa/Dockerfile` | `HVO_TPLINKKASA_IMAGE_VERSION` |

Each publish always writes the explicit version tag from `.env`. Add `--push-latest` when you intentionally want to update the mutable `latest` tag as well.

For example:

```bash
./scripts/publish-image.sh website
./scripts/publish-image.sh --push-latest website
```

## Version Variables

The independent image version variables live in `.env`. Each image tracks its own version independently:

```bash
# Current values are in .env -- check the file for live version numbers
HVO_WEBSITE_IMAGE_VERSION=<current>
HVO_DAVIS_IMAGE_VERSION=<current>
HVO_JKBMS_IMAGE_VERSION=<current>
HVO_SOLARASSISTANT_IMAGE_VERSION=<current>
HVO_SMARTSHUNT_IMAGE_VERSION=<current>
HVO_TPLINKKASA_IMAGE_VERSION=<current>
```

Only bump the variable for the image you are publishing. See `CHANGELOG.md` for published version history.

## Publish Script

Use the repo script to build, tag, push, and verify one target at a time:

```bash
./scripts/publish-image.sh website
./scripts/publish-image.sh davis
./scripts/publish-image.sh jkbms
./scripts/publish-image.sh solarassistant
./scripts/publish-image.sh smartshunt
./scripts/publish-image.sh tplinkkasa
```

To inspect the exact commands without building or pushing:

```bash
./scripts/publish-image.sh --dry-run website
```

The script:

- sources `.env`
- logs into the self-hosted registry with `HVO_CONTAINER_REGISTRY_USERNAME` and `HVO_CONTAINER_REGISTRY_PASSWORD`
- builds the selected Dockerfile
- tags the image with the configured version, plus `latest` when `--push-latest` is used
- pushes the selected tag set

## Deployment Scripts

Publishing to the registry does not roll a running service by itself. Use the deployment scripts after publishing or when rebuilding directly to a Docker context.

Deploy the hvo-docker website stack to a specific image tag:

```bash
./scripts/deploy-hvo-website.sh --tag 20260704011327 --no-build
```

If `--tag` is omitted, the script uses the image configured by the hvo-docker compose file or rebuilds locally unless `--no-build` is supplied.

Deploy Pi gateway compose stacks to the configured Docker context:

```bash
./scripts/deploy-pi-gateway.sh --context devpi5 all
./scripts/deploy-pi-gateway.sh --context devpi5 jkbms
```

Check website and Pi health endpoints:

```bash
./scripts/check-deployments.sh
```

All three scripts support dry-run or environment overrides where appropriate; run each script with `--help` for details.

## Standard Publish Workflow

When you are publishing a new version for a single target:

1. Update the corresponding `HVO_*_IMAGE_VERSION` value in `.env`.
2. Sync the private `.env` gist so new devcontainers pull the same version metadata.
3. Run `./scripts/publish-image.sh <target>`.
4. Verify the image tag in the self-hosted registry or by pulling/running the target deployment.
5. Add or update a short image-publish note in `CHANGELOG.md` under `Unreleased` when the published version changes.
6. If the publish changes the operational deployment workflow or release guidance, update this document and the README.

Example for a Davis-only publish:

```bash
./scripts/sync-env-gist.sh
./scripts/publish-image.sh davis
```

## Gist Sync Script

Use the companion gist-sync script after changing `.env` publish metadata:

```bash
./scripts/sync-env-gist.sh
```

To verify the action without writing to GitHub:

```bash
./scripts/sync-env-gist.sh --dry-run
```

## Query Commands

Useful Docker commands for container publishing and verification:

```bash
docker --context hvo-docker ps
docker pull registry.hualapaivalleyobservatory.org/hvo-website:<tag>
docker pull registry.hualapaivalleyobservatory.org/hvo-davis:<tag>
docker pull registry.hualapaivalleyobservatory.org/hvo-jkbms:<tag>
docker pull registry.hualapaivalleyobservatory.org/hvo-solarassistant:<tag>
docker pull registry.hualapaivalleyobservatory.org/hvo-smartshunt:<tag>
docker pull registry.hualapaivalleyobservatory.org/hvo-tplinkkasa:<tag>
```

## Gist Sync

The devcontainer bootstrap uses private gist `f343db002d980ebe5fcc51413b0b7227` as the `.env` source.

After changing image version variables in `.env`, run `./scripts/sync-env-gist.sh` so fresh containers inherit the same publish metadata.

## Changelog Convention

When a published image version changes, add a concise line to `CHANGELOG.md` under `Unreleased` describing the target and new image version.

Example:

```markdown
- Published `hvo-davis` container image `1.0.1` to `registry.hualapaivalleyobservatory.org`
```

## Notes

- The website image also uses `WEBSITE_RUNTIME` from `.env` at build time.
- Use braces around repository variables when appending `:latest` in shell commands. Missing braces previously created incorrect repositories such as `hvo-websiteatest`.
- The deprecated `publish-acr-image.sh` script is retained only as historical reference; use `publish-image.sh` for active publishing.
