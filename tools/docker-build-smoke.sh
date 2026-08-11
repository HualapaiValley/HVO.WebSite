#!/usr/bin/env bash

set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd -- "${script_dir}/.." && pwd)"
docker_command="${DOCKER_COMMAND:-docker}"
cache_mode="${DOCKER_SMOKE_CACHE_MODE:-ephemeral}"
run_key="${DOCKER_SMOKE_RUN_KEY:-${GITHUB_RUN_ID:-local}-${GITHUB_JOB:-smoke}-$$}"
run_key="${run_key//[^a-zA-Z0-9_.-]/-}"
smoke_image=""

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
build_smoke_image src/HVO.WebSite.v9/Dockerfile "hvo-website-ci:${run_key}"
build_smoke_image src/HVO.Hardware.DavisVantagePro2/Dockerfile "hvo-davis-ci:${run_key}"
build_smoke_image src/HVO.Hardware.JkBms/Dockerfile "hvo-jkbms-ci:${run_key}"
build_smoke_image src/HVO.Gateway.SolarAssistant/Dockerfile "hvo-solarassistant-ci:${run_key}"
build_smoke_image src/HVO.Hardware.VictronSmartShunt/Dockerfile "hvo-smartshunt-ci:${run_key}"
build_smoke_image src/HVO.Gateway.TplinkKasa/Dockerfile "hvo-tplinkkasa-ci:${run_key}"
build_smoke_image src/HVO.Hardware.Eg4/Dockerfile "hvo-eg4-ci:${run_key}"
build_smoke_image src/HVO.Edge.Exporter.HomeAssistant/Dockerfile "hvo-ha-exporter-ci:${run_key}"
