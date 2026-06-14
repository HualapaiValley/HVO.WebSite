#!/usr/bin/env bash

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
env_file="${HVO_PUBLISH_ENV_FILE:-${repo_root}/.env}"

usage() {
	printf 'Usage: %s [--dry-run] [--push-latest] <website|davis|jkbms|solarassistant|smartshunt|tplinkkasa>\n' "$(basename "$0")"
	printf '\n'
	printf 'Builds, tags, pushes, and verifies a single image in Azure Container Registry.\n'
	printf 'Version and repository names come from %s.\n' "${env_file}"
	printf '\n'
	printf 'Examples:\n'
	printf '  %s website\n' "$(basename "$0")"
	printf '  %s --push-latest website\n' "$(basename "$0")"
	printf '  %s --dry-run davis\n' "$(basename "$0")"
	printf '  %s tplinkkasa\n' "$(basename "$0")"
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
			require_env HVO_WEBSITE_IMAGE_REPOSITORY
			require_env HVO_WEBSITE_IMAGE_VERSION
			image_repository="${HVO_WEBSITE_IMAGE_REPOSITORY}"
			image_version="${HVO_WEBSITE_IMAGE_VERSION}"
			dockerfile_path="src/HVO.WebSite.v9/Dockerfile"
			build_args=()
			;;
		davis|hvo-davis)
			require_env HVO_DAVIS_IMAGE_REPOSITORY
			require_env HVO_DAVIS_IMAGE_VERSION
			image_repository="${HVO_DAVIS_IMAGE_REPOSITORY}"
			image_version="${HVO_DAVIS_IMAGE_VERSION}"
			dockerfile_path="src/HVO.Hardware.DavisVantagePro2/Dockerfile"
			build_args=()
			;;
		jkbms|hvo-jkbms)
			require_env HVO_JKBMS_IMAGE_REPOSITORY
			require_env HVO_JKBMS_IMAGE_VERSION
			image_repository="${HVO_JKBMS_IMAGE_REPOSITORY}"
			image_version="${HVO_JKBMS_IMAGE_VERSION}"
			dockerfile_path="src/HVO.Hardware.JkBms/Dockerfile"
			build_args=()
			;;
		solarassistant|hvo-solarassistant)
			require_env HVO_SOLARASSISTANT_IMAGE_REPOSITORY
			require_env HVO_SOLARASSISTANT_IMAGE_VERSION
			image_repository="${HVO_SOLARASSISTANT_IMAGE_REPOSITORY}"
			image_version="${HVO_SOLARASSISTANT_IMAGE_VERSION}"
			dockerfile_path="src/HVO.Gateway.SolarAssistant/Dockerfile"
			build_args=()
			;;
		smartshunt|hvo-smartshunt)
			require_env HVO_SMARTSHUNT_IMAGE_REPOSITORY
			require_env HVO_SMARTSHUNT_IMAGE_VERSION
			image_repository="${HVO_SMARTSHUNT_IMAGE_REPOSITORY}"
			image_version="${HVO_SMARTSHUNT_IMAGE_VERSION}"
			dockerfile_path="src/HVO.Hardware.VictronSmartShunt/Dockerfile"
			build_args=()
			;;
		tplinkkasa|hvo-tplinkkasa)
			require_env HVO_TPLINKKASA_IMAGE_REPOSITORY
			require_env HVO_TPLINKKASA_IMAGE_VERSION
			image_repository="${HVO_TPLINKKASA_IMAGE_REPOSITORY}"
			image_version="${HVO_TPLINKKASA_IMAGE_VERSION}"
			dockerfile_path="src/HVO.Gateway.TplinkKasa/Dockerfile"
			build_args=()
			;;
		*)
			fail "Unknown target '$1'. Expected website, davis, jkbms, solarassistant, smartshunt, or tplinkkasa."
			;;
	esac
}

dry_run=false
push_latest=false
target=""

while (($# > 0)); do
	case "$1" in
		--dry-run)
			dry_run=true
			shift
			;;
		--push-latest)
			push_latest=true
			shift
			;;
		-h|--help)
			usage
			exit 0
			;;
		website|hvo-website|davis|hvo-davis|jkbms|hvo-jkbms|solarassistant|hvo-solarassistant|smartshunt|hvo-smartshunt|tplinkkasa|hvo-tplinkkasa)
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

docker_config_dir=""
cleanup_docker_config() {
	if [[ -n "${docker_config_dir}" ]]; then
		rm -rf "${docker_config_dir}"
	fi
}

if [[ "${dry_run}" == false && -z "${DOCKER_CONFIG:-}" ]]; then
	docker_config_dir="$(mktemp -d "${TMPDIR:-/tmp}/hvo-acr-docker-config.XXXXXX")"
	export DOCKER_CONFIG="${docker_config_dir}"
	trap cleanup_docker_config EXIT
fi

set -a
# shellcheck disable=SC1090
source "${env_file}"
set +a

require_env AZURE_CONTAINER_REGISTRY_NAME
require_env AZURE_CONTAINER_REGISTRY_LOGIN_SERVER

resolve_target "${target}"

version_ref="${AZURE_CONTAINER_REGISTRY_LOGIN_SERVER}/${image_repository}:${image_version}"
latest_ref="${AZURE_CONTAINER_REGISTRY_LOGIN_SERVER}/${image_repository}:latest"

printf 'Target: %s\n' "${target}"
printf 'Dockerfile: %s\n' "${dockerfile_path}"
printf 'Version tag: %s\n' "${version_ref}"
if [[ "${push_latest}" == true ]]; then
	printf 'Latest tag: %s\n' "${latest_ref}"
else
	printf 'Latest tag: disabled (use --push-latest to publish mutable latest)\n'
fi

run_cmd az acr login --name "${AZURE_CONTAINER_REGISTRY_NAME}"

docker_build_args=("${build_args[@]}" -t "${version_ref}")
if [[ "${push_latest}" == true ]]; then
	docker_build_args+=(-t "${latest_ref}")
fi

run_cmd docker build \
	"${docker_build_args[@]}" \
	-f "${repo_root}/${dockerfile_path}" \
	"${repo_root}"

run_cmd docker push "${version_ref}"
if [[ "${push_latest}" == true ]]; then
	run_cmd docker push "${latest_ref}"
fi

run_cmd az acr repository show-tags \
	--name "${AZURE_CONTAINER_REGISTRY_NAME}" \
	--repository "${image_repository}" \
	--output table

if [[ "${dry_run}" == true ]]; then
	printf 'Would publish %s\n' "${version_ref}"
	if [[ "${push_latest}" == true ]]; then
		printf 'Would publish %s\n' "${latest_ref}"
	fi
else
	printf 'Published %s\n' "${version_ref}"
	if [[ "${push_latest}" == true ]]; then
		printf 'Published %s\n' "${latest_ref}"
	fi
fi
