#!/usr/bin/env bash

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
env_file="${repo_root}/.env"

usage() {
	printf 'Usage: %s [--dry-run] <website|davis|jkbms>\n' "$(basename "$0")"
	printf '\n'
	printf 'Builds, tags, pushes, and verifies a single image in Azure Container Registry.\n'
	printf 'Version and repository names come from %s.\n' "${env_file}"
	printf '\n'
	printf 'Examples:\n'
	printf '  %s website\n' "$(basename "$0")"
	printf '  %s --dry-run davis\n' "$(basename "$0")"
}

fail() {
	printf 'Error: %s\n' "$*" >&2
	exit 1
}

require_command() {
	command -v "$1" >/dev/null 2>&1 || fail "Required command not found: $1"
}

run_cmd() {
	if [[ "${dry_run}" == true ]]; then
		printf '[dry-run]'
		printf ' %q' "$@"
		printf '\n'
		return 0
	fi

	"$@"
}

require_env() {
	local name="$1"
	[[ -n "${!name:-}" ]] || fail "Required environment variable is missing: ${name}"
}

resolve_target() {
	case "$1" in
		website|hvo-website)
			image_repository="${HVO_WEBSITE_IMAGE_REPOSITORY}"
			image_version="${HVO_WEBSITE_IMAGE_VERSION}"
			dockerfile_path="src/HVO.WebSite.v9/Dockerfile"
			build_args=(--build-arg "WEBSITE_RUNTIME=${WEBSITE_RUNTIME}")
			;;
		davis|hvo-davis)
			image_repository="${HVO_DAVIS_IMAGE_REPOSITORY}"
			image_version="${HVO_DAVIS_IMAGE_VERSION}"
			dockerfile_path="src/HVO.Hardware.DavisVantagePro2/Dockerfile"
			build_args=()
			;;
		jkbms|hvo-jkbms)
			image_repository="${HVO_JKBMS_IMAGE_REPOSITORY}"
			image_version="${HVO_JKBMS_IMAGE_VERSION}"
			dockerfile_path="src/HVO.Hardware.JkBms/Dockerfile"
			build_args=()
			;;
		*)
			fail "Unknown target '$1'. Expected website, davis, or jkbms."
			;;
	esac
}

dry_run=false
target=""

while (($# > 0)); do
	case "$1" in
		--dry-run)
			dry_run=true
			shift
			;;
		-h|--help)
			usage
			exit 0
			;;
		website|hvo-website|davis|hvo-davis|jkbms|hvo-jkbms)
			[[ -z "${target}" ]] || fail 'Only one target can be published per invocation.'
			target="$1"
			shift
			;;
		*)
			fail "Unknown argument '$1'."
			;;
	esac
done

[[ -n "${target}" ]] || {
	usage
	exit 1
}

[[ -f "${env_file}" ]] || fail "Expected environment file at ${env_file}"

require_command az
require_command docker

set -a
# shellcheck disable=SC1090
source "${env_file}"
set +a

require_env AZURE_CONTAINER_REGISTRY_NAME
require_env AZURE_CONTAINER_REGISTRY_LOGIN_SERVER
require_env HVO_WEBSITE_IMAGE_REPOSITORY
require_env HVO_WEBSITE_IMAGE_VERSION
require_env HVO_DAVIS_IMAGE_REPOSITORY
require_env HVO_DAVIS_IMAGE_VERSION
require_env HVO_JKBMS_IMAGE_REPOSITORY
require_env HVO_JKBMS_IMAGE_VERSION
require_env WEBSITE_RUNTIME

resolve_target "${target}"

version_ref="${AZURE_CONTAINER_REGISTRY_LOGIN_SERVER}/${image_repository}:${image_version}"
latest_ref="${AZURE_CONTAINER_REGISTRY_LOGIN_SERVER}/${image_repository}:latest"

printf 'Target: %s\n' "${target}"
printf 'Dockerfile: %s\n' "${dockerfile_path}"
printf 'Version tag: %s\n' "${version_ref}"
printf 'Latest tag: %s\n' "${latest_ref}"

run_cmd az acr login --name "${AZURE_CONTAINER_REGISTRY_NAME}"

run_cmd docker build \
	"${build_args[@]}" \
	-t "${version_ref}" \
	-t "${latest_ref}" \
	-f "${repo_root}/${dockerfile_path}" \
	"${repo_root}"

run_cmd docker push "${version_ref}"
run_cmd docker push "${latest_ref}"

run_cmd az acr repository show-tags \
	--name "${AZURE_CONTAINER_REGISTRY_NAME}" \
	--repository "${image_repository}" \
	--output table

if [[ "${dry_run}" == true ]]; then
	printf 'Would publish %s\n' "${version_ref}"
	printf 'Would publish %s\n' "${latest_ref}"
else
	printf 'Published %s\n' "${version_ref}"
	printf 'Published %s\n' "${latest_ref}"
fi