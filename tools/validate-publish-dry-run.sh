#!/usr/bin/env bash
set -euo pipefail

tmp_dir="$(mktemp -d "${TMPDIR:-/tmp}/hvo-publish-dry-run.XXXXXX")"
cleanup() {
	rm -rf "$tmp_dir"
}
trap cleanup EXIT

export HVO_PUBLISH_ENV_FILE="$tmp_dir/.env"
{
	printf '%s\n' 'HVO_CONTAINER_REGISTRY_LOGIN_SERVER=registry.example.invalid'
	printf '%s\n' 'HVO_CONTAINER_REGISTRY_USERNAME=ci'
	printf '%s\n' 'HVO_CONTAINER_REGISTRY_PASSWORD=ci-password'
	printf '%s\n' 'HVO_WEBSITE_IMAGE_REPOSITORY=hvo-website'
	printf '%s\n' 'HVO_WEBSITE_IMAGE_VERSION=0.0.0-ci'
	printf '%s\n' 'HVO_DAVIS_IMAGE_REPOSITORY=hvo-davis'
	printf '%s\n' 'HVO_DAVIS_IMAGE_VERSION=0.0.0-ci'
	printf '%s\n' 'HVO_JKBMS_IMAGE_REPOSITORY=hvo-jkbms'
	printf '%s\n' 'HVO_JKBMS_IMAGE_VERSION=0.0.0-ci'
	printf '%s\n' 'HVO_SOLARASSISTANT_IMAGE_REPOSITORY=hvo-solarassistant'
	printf '%s\n' 'HVO_SOLARASSISTANT_IMAGE_VERSION=0.0.0-ci'
	printf '%s\n' 'HVO_SMARTSHUNT_IMAGE_REPOSITORY=hvo-smartshunt'
	printf '%s\n' 'HVO_SMARTSHUNT_IMAGE_VERSION=0.0.0-ci'
	printf '%s\n' 'HVO_TPLINKKASA_IMAGE_REPOSITORY=hvo-tplinkkasa'
	printf '%s\n' 'HVO_TPLINKKASA_IMAGE_VERSION=0.0.0-ci'
} > "$HVO_PUBLISH_ENV_FILE"

for target in website davis jkbms solarassistant smartshunt tplinkkasa; do
	bash scripts/publish-image.sh --dry-run "$target" >/dev/null
done