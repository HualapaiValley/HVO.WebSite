#!/usr/bin/env bash

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
docker_context="${HVO_PI_DOCKER_CONTEXT:-devpi5}"
dry_run=false
pull_images=false
build_images=true
allow_env_overrides=false
target=""

usage() {
	printf 'Usage: %s [--dry-run] [--context <docker-context>] [--pull] [--no-build] [--allow-env-overrides] <all|davis|eg4|ha-exporter|jkbms|solarassistant|smartshunt|tplinkkasa>\n' "$(basename "$0")"
	printf '\n'
	printf 'Deploys one or more Pi gateway compose stacks using an existing Docker context.\n'
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

warn_shell_env_overrides() {
	local compose_file="$1"
	local env_names=()
	local line remainder env_name existing

	while IFS= read -r line; do
		remainder="${line}"
		while [[ "${remainder}" =~ \$\{([A-Za-z_][A-Za-z0-9_]*) ]]; do
			env_name="${BASH_REMATCH[1]}"
			env_names+=("${env_name}")
			remainder="${remainder#*\$\{}"
		done
	done < "${compose_file}"

	for env_name in "${env_names[@]}"; do
		for existing in "${seen_env_names[@]:-}"; do
			[[ "${existing}" == "${env_name}" ]] && continue 2
		done
		seen_env_names+=("${env_name}")

		if [[ -v "${env_name}" ]]; then
			printf 'Warning: shell environment variable %s is set and will override the .env file used by docker compose --env-file.\n' "${env_name}" >&2
			override_env_names+=("${env_name}")
		fi
	done
}

read_env_value() {
	local env_file="$1"
	local name="$2"
	local key value

	if [[ -v "${name}" ]]; then
		printf '%s\n' "${!name}"
		return
	fi

	while IFS='=' read -r key value; do
		if [[ "${key}" == "${name}" ]]; then
			value="${value%$'\r'}"
			value="${value#\"}"
			value="${value%\"}"
			value="${value#\'}"
			value="${value%\'}"
			printf '%s\n' "${value}"
			return
		fi
	done < "${env_file}"
}

is_true() {
	case "${1,,}" in
		1|true|yes|on) return 0 ;;
		*) return 1 ;;
	esac
}

compose_dir_for_target() {
	case "$1" in
		davis) printf '%s\n' 'deploy/pi-gateways/davis' ;;
		eg4) printf '%s\n' 'deploy/pi-gateways/eg4' ;;
		ha-exporter|home-assistant-exporter) printf '%s\n' 'deploy/pi-gateways/home-assistant-exporter' ;;
		jkbms) printf '%s\n' 'deploy/pi-gateways/jkbms' ;;
		solarassistant) printf '%s\n' 'deploy/pi-gateways/solarassistant' ;;
		smartshunt) printf '%s\n' 'deploy/pi-gateways/smartshunt' ;;
		tplinkkasa|tplink-kasa) printf '%s\n' 'deploy/pi-gateways/tplink-kasa' ;;
		*) fail "Unknown gateway target '$1'." ;;
	esac
}

deploy_target() {
	local gateway="$1"
	local compose_dir compose_file env_file two_device_file mppt_file
	compose_dir="$(compose_dir_for_target "${gateway}")"
	compose_file="${repo_root}/${compose_dir}/docker-compose.yml"
	env_file="${HVO_PI_GATEWAY_ENV_FILE:-${repo_root}/${compose_dir}/.env}"
	two_device_file="${repo_root}/${compose_dir}/docker-compose.two-device.yml"
	mppt_file="${repo_root}/${compose_dir}/docker-compose.mppt.yml"

	[[ -f "${compose_file}" ]] || fail "Compose file not found: ${compose_file}"
	[[ -f "${env_file}" ]] || fail "Environment file not found: ${env_file}"

	printf 'Deploying %s with Docker context %s\n' "${gateway}" "${docker_context}"

	local compose_files=("${compose_file}")
	local up_args=(up -d --wait --wait-timeout 120)
	local seen_env_names=()
	local override_env_names=()

	warn_shell_env_overrides "${compose_file}"
	if [[ "${gateway}" == eg4 && -f "${two_device_file}" ]]; then
		warn_shell_env_overrides "${two_device_file}"
		if is_true "$(read_env_value "${env_file}" EG4_DEVICE_1_ENABLED)"; then
			compose_files+=("${two_device_file}")
			printf 'Enabling the EG4 two-device Compose overlay.\n'
		fi
	fi
	if [[ "${gateway}" == eg4 && -f "${mppt_file}" ]]; then
		warn_shell_env_overrides "${mppt_file}"
		if is_true "$(read_env_value "${env_file}" EG4_MPPT_0_ENABLED)"; then
			compose_files+=("${mppt_file}")
			printf 'Enabling the EG4 MPPT read-only Compose overlay.\n'
		fi
	fi
	if [[ "${gateway}" == eg4 && "${allow_env_overrides}" == false && ${#override_env_names[@]} -gt 0 ]]; then
		fail "EG4 deployment refuses shell environment overrides (${override_env_names[*]}). Unset them or pass --allow-env-overrides after verifying docker compose config."
	fi

	local compose_args=(--context "${docker_context}" compose --env-file "${env_file}")
	if [[ "${gateway}" == eg4 ]]; then
		compose_args+=(--project-name eg4)
	fi
	for file in "${compose_files[@]}"; do
		compose_args+=(-f "${file}")
	done

	if [[ "${build_images}" == true ]]; then
		up_args+=(--build)
	fi

	if [[ "${pull_images}" == true ]]; then
		up_args+=(--pull always)
	fi

	run_cmd docker "${compose_args[@]}" config --quiet
	run_cmd docker "${compose_args[@]}" "${up_args[@]}"
	run_cmd docker "${compose_args[@]}" ps
	if [[ "${dry_run}" == false ]]; then
		mapfile -t container_ids < <(docker "${compose_args[@]}" ps -aq)
		"${repo_root}/tools/verify-running-container-policy.sh" --require-core "${docker_context}" "${container_ids[@]}"
	fi
}

while (($# > 0)); do
	case "$1" in
		--dry-run)
			dry_run=true
			shift
			;;
		--context)
			[[ $# -ge 2 ]] || fail '--context requires a value.'
			docker_context="$2"
			shift 2
			;;
		--pull)
			pull_images=true
			shift
			;;
		--no-build)
			build_images=false
			shift
			;;
		--allow-env-overrides)
			allow_env_overrides=true
			shift
			;;
		-h|--help)
			usage
			exit 0
			;;
		all|davis|eg4|ha-exporter|home-assistant-exporter|jkbms|solarassistant|smartshunt|tplinkkasa|tplink-kasa)
			[[ -z "${target}" ]] || fail 'Only one target can be specified.'
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

if [[ "${target}" == all && -n "${HVO_PI_GATEWAY_ENV_FILE:-}" ]]; then
	fail 'HVO_PI_GATEWAY_ENV_FILE can only be used with a single gateway target.'
fi

command -v docker >/dev/null 2>&1 || fail 'Required command not found: docker'

if [[ "${target}" == all ]]; then
	for gateway in davis jkbms solarassistant smartshunt tplinkkasa; do
		deploy_target "${gateway}"
	done
else
	deploy_target "${target}"
fi
