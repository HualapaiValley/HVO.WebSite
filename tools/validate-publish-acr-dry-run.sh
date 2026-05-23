#!/usr/bin/env bash
set -euo pipefail

tmp_dir="$(mktemp -d "${TMPDIR:-/tmp}/hvo-publish-dry-run.XXXXXX")"
cleanup() {
	rm -rf "$tmp_dir"
}
trap cleanup EXIT

export HVO_PUBLISH_ENV_FILE="$tmp_dir/.env"
{
	printf '%s\n' 'AZURE_CONTAINER_REGISTRY_NAME=hvo-ci'
	printf '%s\n' 'AZURE_CONTAINER_REGISTRY_LOGIN_SERVER=hvo-ci.azurecr.io'
	printf '%s\n' 'HVO_WEBSITE_IMAGE_REPOSITORY=hvo-website'
	printf '%s\n' 'HVO_WEBSITE_IMAGE_VERSION=0.0.0-ci'
	printf '%s\n' 'HVO_DAVIS_IMAGE_REPOSITORY=hvo-davis'
	printf '%s\n' 'HVO_DAVIS_IMAGE_VERSION=0.0.0-ci'
	printf '%s\n' 'HVO_JKBMS_IMAGE_REPOSITORY=hvo-jkbms'
	printf '%s\n' 'HVO_JKBMS_IMAGE_VERSION=0.0.0-ci'
	printf '%s\n' 'HVO_SOLARASSISTANT_IMAGE_REPOSITORY=hvo-solarassistant'
	printf '%s\n' 'HVO_SOLARASSISTANT_IMAGE_VERSION=0.0.0-ci'
} > "$HVO_PUBLISH_ENV_FILE"

for target in website davis jkbms solarassistant; do
	bash scripts/publish-acr-image.sh --dry-run "$target" >/dev/null
done
