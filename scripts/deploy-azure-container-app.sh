#!/usr/bin/env bash

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
env_file="${HVO_PUBLISH_ENV_FILE:-${repo_root}/.env}"
dry_run=false
resource_group="observatory-rg"
container_app="hvo-website"
registry_login_server="hvoobsacr.azurecr.io"
repository="hvo-website"
target="website"
image=""
tag=""

usage() {
	printf 'Usage: %s [--dry-run] [--resource-group <name>] [--app <name>] [--tag <tag>|--image <image>] website\n' "$(basename "$0")"
	printf '\n'
	printf 'Updates the Azure Container App website to a selected ACR image.\n'
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

while (($# > 0)); do
	case "$1" in
		--dry-run)
			dry_run=true
			shift
			;;
		--resource-group)
			[[ $# -ge 2 ]] || fail '--resource-group requires a value.'
			resource_group="$2"
			shift 2
			;;
		--app)
			[[ $# -ge 2 ]] || fail '--app requires a value.'
			container_app="$2"
			shift 2
			;;
		--tag)
			[[ $# -ge 2 ]] || fail '--tag requires a value.'
			tag="$2"
			shift 2
			;;
		--image)
			[[ $# -ge 2 ]] || fail '--image requires a value.'
			image="$2"
			shift 2
			;;
		-h|--help)
			usage
			exit 0
			;;
		website|hvo-website)
			target="website"
			shift
			;;
		*)
			fail "Unknown argument '$1'."
			;;
	esac
done

command -v az >/dev/null 2>&1 || fail 'Required command not found: az'

if [[ -f "${env_file}" ]]; then
	set -a
	# shellcheck disable=SC1090
	source "${env_file}"
	set +a
	registry_login_server="${AZURE_CONTAINER_REGISTRY_LOGIN_SERVER:-${registry_login_server}}"
	repository="${HVO_WEBSITE_IMAGE_REPOSITORY:-${repository}}"
	tag="${tag:-${HVO_WEBSITE_IMAGE_VERSION:-}}"
fi

if [[ -z "${image}" ]]; then
	if [[ -z "${tag}" ]]; then
		tag="$(az acr repository show-tags --name hvoobsacr --repository "${repository}" --orderby time_desc --top 1 --output tsv)"
	fi
	[[ -n "${tag}" ]] || fail 'No image tag specified and no latest ACR tag was found.'
	image="${registry_login_server}/${repository}:${tag}"
fi

printf 'Target: %s\n' "${target}"
printf 'Container App: %s/%s\n' "${resource_group}" "${container_app}"
printf 'Image: %s\n' "${image}"

run_cmd az containerapp update \
	--name "${container_app}" \
	--resource-group "${resource_group}" \
	--image "${image}"

run_cmd az containerapp show \
	--name "${container_app}" \
	--resource-group "${resource_group}" \
	--query '{name:name,latestRevision:properties.latestRevisionName,image:properties.template.containers[0].image,fqdn:properties.configuration.ingress.fqdn,provisioningState:properties.provisioningState,runningStatus:properties.runningStatus}' \
	--output table