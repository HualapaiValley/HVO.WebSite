#!/usr/bin/env bash

set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd -- "${script_dir}/.." && pwd)"
docker_command="${DOCKER_COMMAND:-docker}"
cache_mode="${DOCKER_SMOKE_CACHE_MODE:-ephemeral}"
run_key="${DOCKER_SMOKE_RUN_KEY:-${GITHUB_RUN_ID:-local}-${GITHUB_JOB:-smoke}-$$}"
run_key="${run_key//[^a-zA-Z0-9_.-]/-}"
smoke_image=""
images=(website davis jkbms smartshunt eg4 ha-exporter)
if (( $# )); then
	[[ "$1" == --images ]] || { printf 'Usage: %s [--images [website davis jkbms smartshunt eg4 ha-exporter ...]]\n' "$0" >&2; exit 2; }
	shift
	images=("$@")
fi
declare -A selected_images=()
for image_id in "${images[@]}"; do
	case "$image_id" in website|davis|jkbms|smartshunt|eg4|ha-exporter) ;; *) printf 'Unknown Docker smoke image: %s\n' "$image_id" >&2; exit 2 ;; esac
	[[ -z "${selected_images[$image_id]:-}" ]] || { printf 'Repeated Docker smoke image: %s\n' "$image_id" >&2; exit 2; }
	selected_images[$image_id]=1
done
if (( ${#images[@]} == 0 )); then
	printf 'Verified empty Docker image selection; no Docker work requested.\n'
	exit 0
fi
[[ "$cache_mode" == ephemeral || "$cache_mode" == persistent ]] || { printf 'Unknown Docker smoke cache mode: %s\n' "$cache_mode" >&2; exit 2; }

remove_smoke_image() {
	if [[ -n "${smoke_image}" ]]; then
		if ! "${docker_command}" image rm --force "${smoke_image}" >/dev/null 2>&1; then
			printf 'Failed to remove Docker smoke image %s.\n' "${smoke_image}" >&2
			return 1
		fi
		smoke_image=""
	fi
}

prune_cache() {
	if [[ "${cache_mode}" == persistent ]]; then
		"${docker_command}" builder prune --force --max-used-space 30GB >/dev/null
	else
		"${docker_command}" builder prune --all --force >/dev/null
	fi
}

cleanup_after_build() {
	remove_smoke_image
	if [[ "${cache_mode}" != persistent ]]; then
		prune_cache
	fi
}

cleanup_on_exit() {
	local exit_code="$?"
	local cleanup_status=0
	set +e
	remove_smoke_image
	cleanup_status="$?"
	prune_cache
	if [[ "$?" -ne 0 && "${cleanup_status}" -eq 0 ]]; then
		cleanup_status=1
	fi
	if [[ "${exit_code}" -eq 0 && "${cleanup_status}" -ne 0 ]]; then
		exit_code="${cleanup_status}"
	fi
	exit "${exit_code}"
}
trap cleanup_on_exit EXIT

build_smoke_image() {
	local dockerfile="$1"
	local image="$2"

	smoke_image="${image}"
	"${docker_command}" build --file "${repo_root}/${dockerfile}" --tag "${image}" "${repo_root}"
	cleanup_after_build
}

# GitHub-hosted runners have limited ephemeral storage. Keep only one smoke image's
# layers at a time. The self-hosted runner retains recent layers within a bounded
# cache so repeated PR builds do not restore and compile every image from scratch.
prune_cache
for image_id in "${images[@]}"; do
	case "$image_id" in
		website) project=HVO.WebSite.v9 ;;
		davis) project=HVO.Hardware.DavisVantagePro2 ;;
		jkbms) project=HVO.Hardware.JkBms ;;
		smartshunt) project=HVO.Hardware.VictronSmartShunt ;;
		eg4) project=HVO.Hardware.Eg4 ;;
		ha-exporter) project=HVO.Edge.Exporter.HomeAssistant ;;
	esac
	build_smoke_image "src/$project/Dockerfile" "hvo-$image_id-ci:${run_key}"
done
