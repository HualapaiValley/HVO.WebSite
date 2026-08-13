#!/usr/bin/env bash

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
env_file="${HVO_PUBLISH_ENV_FILE:-${repo_root}/deploy/hvo-docker/.env}"
dry_run=false
push_latest=false
target=""
user_tag=""

usage() {
	printf 'Usage: %s [--dry-run] [--push-latest] [--tag <tag>] <website|davis|jkbms|solarassistant|smartshunt|tplinkkasa>\n' "$(basename "$0")"
	printf '\n'
	printf 'Builds, tags, and pushes a single image to the self-hosted container\n'
	printf 'registry at registry.hualapaivalleyobservatory.org.\n'
	printf 'Env file: %s\n' "${env_file}"
	printf '\n'
	printf 'If --tag is provided, uses that as the image version. Otherwise uses\n'
	printf 'HVO_<TARGET>_IMAGE_VERSION from the env file.\n'
	printf '\n'
	printf 'Examples:\n'
	printf '  %s website\n' "$(basename "$0")"
	printf '  %s --tag 20260101000000 website\n' "$(basename "$0")"
	printf '  %s --push-latest website\n' "$(basename "$0")"
}

fail() {
	printf 'Error: %s\n' "$*" >&2
	exit 1
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

read_env_value() {
	local file="$1"
	local name="$2"
	local key value

	while IFS='=' read -r key value || [[ -n "${key}${value}" ]]; do
		key="${key#export }"
		if [[ "${key}" == "${name}" ]]; then
			value="${value%$'\r'}"
			if [[ "${value}" == \"*\" && "${value}" == *\" ]]; then
				value="${value:1:${#value}-2}"
			elif [[ "${value}" == \'*\' && "${value}" == *\' ]]; then
				value="${value:1:${#value}-2}"
			fi
			printf '%s' "${value}"
			return 0
		fi
	done < "${file}"

	return 1
}

load_publish_environment() {
	local name value
	local names=(
		HVO_CONTAINER_REGISTRY_LOGIN_SERVER
		HVO_CONTAINER_REGISTRY_USERNAME
		HVO_CONTAINER_REGISTRY_PASSWORD
		HVO_WEBSITE_IMAGE_REPOSITORY HVO_WEBSITE_IMAGE_VERSION
		HVO_DAVIS_IMAGE_REPOSITORY HVO_DAVIS_IMAGE_VERSION
		HVO_JKBMS_IMAGE_REPOSITORY HVO_JKBMS_IMAGE_VERSION
		HVO_SOLARASSISTANT_IMAGE_REPOSITORY HVO_SOLARASSISTANT_IMAGE_VERSION
		HVO_SMARTSHUNT_IMAGE_REPOSITORY HVO_SMARTSHUNT_IMAGE_VERSION
		HVO_TPLINKKASA_IMAGE_REPOSITORY HVO_TPLINKKASA_IMAGE_VERSION
	)

	for name in "${names[@]}"; do
		if value="$(read_env_value "${env_file}" "${name}")"; then
			printf -v "${name}" '%s' "${value}"
		fi
	done
}

resolve_target() {
	local var_prefix
	case "$1" in
		website|hvo-website)
			var_prefix="HVO_WEBSITE"
			dockerfile_path="src/HVO.WebSite.v9/Dockerfile"
			;;
		davis|hvo-davis)
			var_prefix="HVO_DAVIS"
			dockerfile_path="src/HVO.Hardware.DavisVantagePro2/Dockerfile"
			;;
		jkbms|hvo-jkbms)
			var_prefix="HVO_JKBMS"
			dockerfile_path="src/HVO.Hardware.JkBms/Dockerfile"
			;;
		solarassistant|hvo-solarassistant)
			var_prefix="HVO_SOLARASSISTANT"
			dockerfile_path="src/HVO.Gateway.SolarAssistant/Dockerfile"
			;;
		smartshunt|hvo-smartshunt)
			var_prefix="HVO_SMARTSHUNT"
			dockerfile_path="src/HVO.Hardware.VictronSmartShunt/Dockerfile"
			;;
		tplinkkasa|hvo-tplinkkasa)
			var_prefix="HVO_TPLINKKASA"
			dockerfile_path="src/HVO.Gateway.TplinkKasa/Dockerfile"
			;;
		*)
			fail "Unknown target '$1'. Expected website, davis, jkbms, solarassistant, smartshunt, or tplinkkasa."
			;;
	esac

	image_repository_var="${var_prefix}_IMAGE_REPOSITORY"
	image_version_var="${var_prefix}_IMAGE_VERSION"

	image_repository="${!image_repository_var:-$1}"
	image_version="${user_tag:-${!image_version_var:-}}"

	if [[ -z "${image_version}" ]]; then
		image_version="$(date +%Y%m%d%H%M%S)"
		printf 'No version tag specified; generated: %s\n' "${image_version}"
	fi

	printf 'Dockerfile: %s\n' "${dockerfile_path}"
}

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
		--tag)
			[[ $# -ge 2 ]] || fail '--tag requires a value.'
			user_tag="$2"
			shift 2
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

[[ -n "${target}" ]] || { usage; exit 1; }
[[ -f "${env_file}" ]] || fail "Expected environment file at ${env_file}"

load_publish_environment

require_env HVO_CONTAINER_REGISTRY_LOGIN_SERVER
require_env HVO_CONTAINER_REGISTRY_USERNAME
require_env HVO_CONTAINER_REGISTRY_PASSWORD

resolve_target "${target}"

version_ref="${HVO_CONTAINER_REGISTRY_LOGIN_SERVER}/${image_repository}:${image_version}"
latest_ref="${HVO_CONTAINER_REGISTRY_LOGIN_SERVER}/${image_repository}:latest"

printf 'Target: %s\n' "${target}"
printf 'Version tag: %s\n' "${version_ref}"
if [[ "${push_latest}" == true ]]; then
	printf 'Latest tag: %s\n' "${latest_ref}"
else
	printf 'Latest tag: disabled (use --push-latest to publish mutable latest)\n'
fi

echo "Logging in to ${HVO_CONTAINER_REGISTRY_LOGIN_SERVER}"
if [[ "${dry_run}" == true ]]; then
	run_cmd docker login "${HVO_CONTAINER_REGISTRY_LOGIN_SERVER}" --username "${HVO_CONTAINER_REGISTRY_USERNAME}" --password-stdin
else
	printf '%s\n' "${HVO_CONTAINER_REGISTRY_PASSWORD}" | docker login "${HVO_CONTAINER_REGISTRY_LOGIN_SERVER}" --username "${HVO_CONTAINER_REGISTRY_USERNAME}" --password-stdin
fi

docker_build_args=(-t "${version_ref}")
if [[ "${push_latest}" == true ]]; then
	docker_build_args+=(-t "${latest_ref}")
fi

run_cmd docker build \
	-f "${dockerfile_path}" \
	"${docker_build_args[@]}" \
	"${repo_root}"

run_cmd docker push "${version_ref}"

if [[ "${push_latest}" == true ]]; then
	run_cmd docker push "${latest_ref}"
fi

printf 'Published: %s\n' "${version_ref}"
