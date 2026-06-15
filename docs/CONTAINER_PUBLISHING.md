# Container Publishing

This repository publishes six application images to Azure Container Registry (ACR), and each image is versioned independently.

## Azure Inventory

Current Azure container publishing targets:

| Item | Value |
|------|-------|
| Subscription name | `RoySalisbury_MSDN150` |
| Subscription ID | `167bf707-6208-4851-b4de-d47c8c7f5cbc` |
| Resource group | `observatory-rg` |
| Registry name | `hvoobsacr` |
| Login server | `hvoobsacr.azurecr.io` |
| Region | `westus` |

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
./scripts/publish-acr-image.sh website
./scripts/publish-acr-image.sh --push-latest website
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
./scripts/publish-acr-image.sh website
./scripts/publish-acr-image.sh davis
./scripts/publish-acr-image.sh jkbms
./scripts/publish-acr-image.sh solarassistant
./scripts/publish-acr-image.sh smartshunt
./scripts/publish-acr-image.sh tplinkkasa
```

To inspect the exact commands without building or pushing:

```bash
./scripts/publish-acr-image.sh --dry-run website
```

The script:

- sources `.env`
- logs into ACR with `az acr login`
- uses a temporary Docker config for ACR login when `DOCKER_CONFIG` is unset, avoiding local credential-helper failures and persistent publish credentials
- builds the selected Dockerfile
- tags the image with the configured version, plus `latest` when `--push-latest` is used
- pushes the selected tag set
- verifies the repository tags in ACR

## Deployment Scripts

Publishing to ACR does not roll a running service by itself. Use the deployment scripts after publishing or when rebuilding directly to a Docker context.

Deploy the Azure website Container App to a specific image tag:

```bash
./scripts/deploy-azure-container-app.sh --tag 1.0.22 website
```

If `--tag` is omitted, the script uses `HVO_WEBSITE_IMAGE_VERSION` from `.env` when present, otherwise it queries the newest `hvo-website` tag in ACR.

Deploy Pi gateway compose stacks to the configured Docker context:

```bash
./scripts/deploy-pi-gateway.sh --context devpi5 all
./scripts/deploy-pi-gateway.sh --context devpi5 jkbms
```

Check Azure and Pi health endpoints:

```bash
./scripts/check-deployments.sh
```

All three scripts support dry-run or environment overrides where appropriate; run each script with `--help` for details.

## Standard Publish Workflow

When you are publishing a new version for a single target:

1. Update the corresponding `HVO_*_IMAGE_VERSION` value in `.env`.
2. Sync the private `.env` gist so new devcontainers pull the same version metadata.
3. Run `./scripts/publish-acr-image.sh <target>`.
4. Verify the repository tags in ACR.
5. Add or update a short image-publish note in `CHANGELOG.md` under `Unreleased` when the published version changes.
6. If the publish changes the operational deployment workflow or release guidance, update this document and the README.

Example for a Davis-only publish:

```bash
./scripts/sync-env-gist.sh
./scripts/publish-acr-image.sh davis
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

Useful Azure queries for container publishing and verification:

```bash
az account show --output table
az acr show --name hvoobsacr --resource-group observatory-rg --output table
az acr repository list --name hvoobsacr --output table
az acr repository show-tags --name hvoobsacr --repository hvo-website --output table
az acr repository show-tags --name hvoobsacr --repository hvo-davis --output table
az acr repository show-tags --name hvoobsacr --repository hvo-jkbms --output table
az acr repository show-tags --name hvoobsacr --repository hvo-solarassistant --output table
az acr repository show-tags --name hvoobsacr --repository hvo-smartshunt --output table
az acr repository show-tags --name hvoobsacr --repository hvo-tplinkkasa --output table
```

## Gist Sync

The devcontainer bootstrap uses private gist `f343db002d980ebe5fcc51413b0b7227` as the `.env` source.

After changing image version variables in `.env`, run `./scripts/sync-env-gist.sh` so fresh containers inherit the same publish metadata.

## Changelog Convention

When a published image version changes, add a concise line to `CHANGELOG.md` under `Unreleased` describing the target and new image version.

Example:

```markdown
- Published `hvo-davis` container image `1.0.1` to `hvoobsacr.azurecr.io`
```

## Notes

- The website image also uses `WEBSITE_RUNTIME` from `.env` at build time.
- Use braces around repository variables when appending `:latest` in shell commands. Missing braces previously created incorrect repositories such as `hvo-websiteatest`.
- ACR queries and publishes assume the Azure CLI is already authenticated to the `RoySalisbury_MSDN150` subscription.
