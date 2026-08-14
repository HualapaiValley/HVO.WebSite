# Container Publishing

This repository actively publishes four application images to the self-hosted Docker registry on `hvo-docker`, and each image is versioned independently. EG4 is built natively through the remote Pi Docker context rather than published by this workflow.

## Registry Inventory

Current container publishing target:

| Item | Value |
|------|-------|
| Host | `hvo-docker` |
| Registry container | `registry:2` |
| Login server | `registry.hualapaivalleyobservatory.org` |
| Active publish script | `scripts/publish-image.sh` |

Image repositories, tags, and other non-secret publish settings are maintained in
`.env`. Registry credentials come from `hvo-central-kv`; the devcontainer
bootstrap gist is only a cache of the synchronized `.env`.

## Image Repositories

The published repositories are:

| Target | Repository | Dockerfile | Version variable |
|--------|------------|------------|------------------|
| Website | `hvo-website` | `src/HVO.WebSite.v9/Dockerfile` | `HVO_WEBSITE_IMAGE_VERSION` |
| Davis | `hvo-davis` | `src/HVO.Hardware.DavisVantagePro2/Dockerfile` | `HVO_DAVIS_IMAGE_VERSION` |
| JK BMS | `hvo-jkbms` | `src/HVO.Hardware.JkBms/Dockerfile` | `HVO_JKBMS_IMAGE_VERSION` |
| SmartShunt | `hvo-smartshunt` | `src/HVO.Hardware.VictronSmartShunt/Dockerfile` | `HVO_SMARTSHUNT_IMAGE_VERSION` |

The retired direct `hvo-solarassistant` and `hvo-tplinkkasa` images have been removed and are not publish targets. The HA exporter is implemented but intentionally disabled in production and is not part of the active image-publishing inventory.

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
HVO_SMARTSHUNT_IMAGE_VERSION=<current>
```

Only bump the variable for the image you are publishing. See `CHANGELOG.md` for published version history.

## Publish Script

Use the repo script to build, tag, push, and verify one target at a time:

```bash
./scripts/publish-image.sh website
./scripts/publish-image.sh davis
./scripts/publish-image.sh jkbms
./scripts/publish-image.sh smartshunt
```

To inspect the exact commands without building or pushing:

```bash
./scripts/publish-image.sh --dry-run website
```

The script:

- reads only the allowlisted `HVO_*` publishing keys from `.env` as dotenv data; optional `export`, matching single/double quotes, CRLF, and values containing `=` are supported, while shell expansion and command substitution are intentionally not evaluated
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

The website deployment also verifies that ASP.NET Core Data Protection uses the
durable encrypted key ring. Before the first migration or any key-ring change,
follow [WEBSITE_DATA_PROTECTION.md](WEBSITE_DATA_PROTECTION.md) for backup,
restore, authentication-continuity, and rollback requirements. Never remove the
named key volume with `docker compose down -v`.

Deploy Pi gateway compose stacks to the configured Docker context:

```bash
./scripts/deploy-pi-gateway.sh --context devpi5 davis
./scripts/deploy-pi-gateway.sh --context devpi5 jkbms
./scripts/deploy-pi-gateway.sh --context devpi5 smartshunt
./scripts/deploy-pi-gateway.sh --context devpi5 eg4
```

Check website and Pi health endpoints:

```bash
./scripts/check-deployments.sh
```

All three scripts support dry-run or environment overrides where appropriate; run each script with `--help` for details.

## Standard Publish Workflow

When you are publishing a new version for a single target:

1. Update the corresponding `HVO_*_IMAGE_VERSION` value in `.env`.
2. Pull authoritative credentials with `./scripts/sync-secrets-from-keyvault.sh --apply`.
3. Sync the private `.env` gist so new devcontainers pull the same version metadata.
4. Run `./scripts/publish-image.sh <target>`.
5. Verify the image tag in the self-hosted registry or by pulling/running the target deployment.
6. Add or update a short image-publish note in `CHANGELOG.md` under `Unreleased` when the published version changes.
7. If the publish changes the operational deployment workflow or release guidance, update this document and the README.

Example for a Davis-only publish:

```bash
./scripts/sync-secrets-from-keyvault.sh --apply
./scripts/sync-env-gist.sh
./scripts/publish-image.sh davis
```

## Gist Sync Script

Use the companion gist-sync script after changing `.env` publish metadata. It
refuses to upload when allowlisted credentials drift from `hvo-central-kv`:

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
docker pull registry.hualapaivalleyobservatory.org/hvo-smartshunt:<tag>
```

## Gist Sync

The devcontainer bootstrap uses private gist `f343db002d980ebe5fcc51413b0b7227`
as an initial `.env` cache. Azure Key Vault `hvo-central-kv` is the primary
source of truth for credentials.

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
