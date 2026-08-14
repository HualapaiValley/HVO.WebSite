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
	printf 'Usage: %s [--dry-run] [--context <docker-context>] [--pull] [--no-build] [--allow-env-overrides] <all|davis|eg4|ha-exporter|jkbms|smartshunt>\n' "$(basename "$0")"
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

docker_context_ssh_target() {
	local context_host
	context_host="$(docker context inspect "$1" --format '{{ (index .Endpoints "docker").Host }}')"
	[[ "${context_host}" == ssh://* ]] || fail "Docker context '$1' must use an ssh:// endpoint to synchronize mounted configuration."
	printf '%s\n' "${context_host#ssh://}"
}

sync_remote_mounts() {
	local gateway="$1"
	local local_config="$2"
	local local_secrets="$3"
	local remote_config="$4"
	local remote_secrets="$5"
	local ssh_target remote_root staging_root staging_config staging_secrets
	command -v ssh >/dev/null 2>&1 || fail 'Required command not found: ssh'
	command -v scp >/dev/null 2>&1 || fail 'Required command not found: scp'

	[[ "${remote_config}" = /* ]] || fail "${gateway} remote configuration path must be absolute."
	[[ "${remote_secrets}" = /* ]] || fail "${gateway} remote secrets path must be absolute."
	[[ "${remote_config}" =~ ^/[A-Za-z0-9._/-]+$ ]] || fail "${gateway} remote configuration path contains unsupported characters."
	[[ "${remote_secrets}" =~ ^/[A-Za-z0-9._/-]+$ ]] || fail "${gateway} remote secrets path contains unsupported characters."
	remote_root="$(dirname "${remote_config}")"
	[[ "${remote_secrets}" == "${remote_root}/secrets" ]] || fail "${gateway} remote secrets directory must be ${remote_root}/secrets."
	if [[ "${dry_run}" == true ]]; then
		ssh_target="docker-context-${docker_context}"
	else
		ssh_target="$(docker_context_ssh_target "${docker_context}")"
	fi
	staging_root="${remote_root}/.staging.$$.${gateway}"
	staging_config="${staging_root}/gateway.json"
	staging_secrets="${staging_root}/secrets"

	run_cmd ssh "${ssh_target}" install -d -m 700 "${remote_root}" "${staging_root}" "${staging_secrets}"
	run_cmd scp "${local_config}" "${ssh_target}:${staging_config}"
	run_cmd scp "${repo_root}/scripts/activate-remote-gateway-config.sh" "${ssh_target}:${staging_root}/activate.sh"
	if [[ "${dry_run}" == true ]]; then
		printf '[dry-run] scp %q %q\n' "${local_secrets}/." "${ssh_target}:${staging_secrets}/"
	else
		scp -r "${local_secrets}/." "${ssh_target}:${staging_secrets}/"
	fi
	run_cmd ssh "${ssh_target}" chmod 600 "${staging_config}" "${staging_root}/activate.sh"
	run_cmd ssh "${ssh_target}" find "${staging_secrets}" -type f -exec chmod 600 {} +
	# The uploaded script performs same-filesystem activation and restores the previous
	# active files if either staged rename fails.
	run_cmd ssh "${ssh_target}" sh "${staging_root}/activate.sh" \
		"${remote_root}" "${staging_root}" "${remote_config}" "${remote_secrets}"
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
		smartshunt) printf '%s\n' 'deploy/pi-gateways/smartshunt' ;;
		*) fail "Unknown gateway target '$1'." ;;
	esac
}

preflight_smartshunt_contract() {
	local env_file="${1}"
	local compose_dir="${2}"
	local -n resolved_config_path="${3}"
	local -n resolved_secrets_path="${4}"
	local -n resolved_remote_config="${5}"
	local -n resolved_remote_secrets="${6}"
	local config_setting config_path secrets_setting secrets_path remote_config remote_secrets
	[[ -f "${env_file}" ]] || fail "Environment file not found: ${env_file}"
	config_setting="$(read_env_value "${env_file}" SMARTSHUNT_CONFIG_FILE)"
	config_path="${config_setting:-./gateway.json}"
	[[ "${config_path}" = /* ]] || config_path="${repo_root}/${compose_dir}/${config_path#./}"
	[[ -f "${config_path}" ]] || fail "SmartShunt mounted configuration not found: ${config_path}"
	jq -e '
		(.Edge.Runtime.GatewayId == "smartshunt") and
		(.Edge.Runtime.GatewayType == "victron-smartshunt-public-gatt") and
		(.Edge.Runtime.SourceId == .SmartShunt.SourceId) and
		(.SmartShunt.Address | test("^([0-9A-Fa-f]{2}:){5}[0-9A-Fa-f]{2}$")) and
		(.Outbox.DatabasePath == "/app/data/outbox.db") and
		(.Outbox.PayloadType == "com.hvo.smartshunt.observation.v1")' "${config_path}" >/dev/null ||
		fail "SmartShunt gateway.json does not satisfy the vNext deployment contract."
	secrets_setting="$(read_env_value "${env_file}" SMARTSHUNT_SECRETS_DIRECTORY)"
	secrets_path="${secrets_setting:-./secrets}"
	[[ "${secrets_path}" = /* ]] || secrets_path="${repo_root}/${compose_dir}/${secrets_path#./}"
	for secret_name in diagnostics-api-key central-ingest-api-key; do [[ -s "${secrets_path}/${secret_name}" ]] || fail "SmartShunt required secret file is missing or empty: ${secret_name}"; done
	if jq -e '.HomeAssistant.Mqtt.Enabled == true' "${config_path}" >/dev/null; then
		for secret_name in mqtt-username mqtt-password; do [[ -s "${secrets_path}/${secret_name}" ]] || fail "SmartShunt required MQTT secret file is missing or empty: ${secret_name}"; done
	fi
	remote_config="$(read_env_value "${env_file}" SMARTSHUNT_REMOTE_CONFIG_FILE)"
	remote_secrets="$(read_env_value "${env_file}" SMARTSHUNT_REMOTE_SECRETS_DIRECTORY)"
	[[ -n "${remote_config}" && -n "${remote_secrets}" ]] || fail "SmartShunt remote config and secrets paths are required."
	resolved_config_path="${config_path}"
	resolved_secrets_path="${secrets_path}"
	resolved_remote_config="${remote_config}"
	resolved_remote_secrets="${remote_secrets}"
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
	if [[ "${gateway}" =~ ^(davis|eg4|jkbms|smartshunt)$ && "${allow_env_overrides}" == false && ${#override_env_names[@]} -gt 0 ]]; then
		fail "${gateway} deployment refuses shell environment overrides (${override_env_names[*]}). Unset them or pass --allow-env-overrides after verifying docker compose config."
	fi
	if [[ "${gateway}" == eg4 ]]; then
		local eg4_config_setting eg4_config_path eg4_secrets_setting eg4_secrets_path eg4_remote_config eg4_remote_secrets
		eg4_config_setting="$(read_env_value "${env_file}" EG4_CONFIG_FILE)"
		eg4_config_path="${eg4_config_setting:-./gateway.json}"
		[[ "${eg4_config_path}" = /* ]] || eg4_config_path="${repo_root}/${compose_dir}/${eg4_config_path#./}"
		[[ -f "${eg4_config_path}" ]] || fail "EG4 mounted configuration not found: ${eg4_config_path}"
		jq -e . "${eg4_config_path}" >/dev/null || fail "EG4 mounted configuration is not valid JSON: ${eg4_config_path}"

		local first_port second_port mppt_port expected_inverters expected_mppt
		first_port="$(read_env_value "${env_file}" EG4_DEVICE_0_PORT)"
		first_port="${first_port:-/dev/hvo/eg4-6500ex-a}"
		second_port="$(read_env_value "${env_file}" EG4_DEVICE_1_PORT)"
		second_port="${second_port:-/dev/hvo/eg4-6500ex-b}"
		mppt_port="$(read_env_value "${env_file}" EG4_MPPT_0_PORT)"
		expected_inverters=1
		expected_mppt=0
		is_true "$(read_env_value "${env_file}" EG4_DEVICE_1_ENABLED)" && expected_inverters=2
		is_true "$(read_env_value "${env_file}" EG4_MPPT_0_ENABLED)" && expected_mppt=1
		jq -e --arg first_port "${first_port}" --arg second_port "${second_port}" --arg mppt_port "${mppt_port}" \
			--argjson expected_inverters "${expected_inverters}" --argjson expected_mppt "${expected_mppt}" \
			'([.Eg4.Devices[] | select(.Enabled == true and .Type == "Inverter6500Ex")] | length) == $expected_inverters
			and ([.Eg4.Devices[] | select(.Enabled == true and .Type == "ChargeControllerMppt10048Hv")] | length) == $expected_mppt
			and any(.Eg4.Devices[]; .Enabled == true and .Type == "Inverter6500Ex" and .Port == $first_port)
			and ($expected_inverters == 1 or any(.Eg4.Devices[]; .Enabled == true and .Type == "Inverter6500Ex" and .Port == $second_port))
			and ($expected_mppt == 0 or any(.Eg4.Devices[]; .Enabled == true and .Type == "ChargeControllerMppt10048Hv" and .Port == $mppt_port))' \
			"${eg4_config_path}" >/dev/null || fail "EG4 gateway.json enabled devices do not match the selected Compose device mappings."

		eg4_secrets_setting="$(read_env_value "${env_file}" EG4_SECRETS_DIRECTORY)"
		eg4_secrets_path="${eg4_secrets_setting:-./secrets}"
		[[ "${eg4_secrets_path}" = /* ]] || eg4_secrets_path="${repo_root}/${compose_dir}/${eg4_secrets_path#./}"
		for secret_name in diagnostics-api-key central-ingest-api-key mqtt-username mqtt-password; do
			[[ -s "${eg4_secrets_path}/${secret_name}" ]] || fail "EG4 required secret file is missing or empty: ${secret_name}"
		done
		eg4_remote_config="$(read_env_value "${env_file}" EG4_REMOTE_CONFIG_FILE)"
		[[ -n "${eg4_remote_config}" ]] || fail "EG4_REMOTE_CONFIG_FILE is required in the EG4 .env file."
		eg4_remote_secrets="$(read_env_value "${env_file}" EG4_REMOTE_SECRETS_DIRECTORY)"
		[[ -n "${eg4_remote_secrets}" ]] || fail "EG4_REMOTE_SECRETS_DIRECTORY is required in the EG4 .env file."
		sync_remote_mounts eg4 "${eg4_config_path}" "${eg4_secrets_path}" "${eg4_remote_config}" "${eg4_remote_secrets}"
	fi
	if [[ "${gateway}" == davis ]]; then
		local davis_config_setting davis_config_path davis_secrets_setting davis_secrets_path davis_station_host davis_remote_config davis_remote_secrets
		davis_config_setting="$(read_env_value "${env_file}" DAVIS_CONFIG_FILE)"
		davis_config_path="${davis_config_setting:-./gateway.json}"
		[[ "${davis_config_path}" = /* ]] || davis_config_path="${repo_root}/${compose_dir}/${davis_config_path#./}"
		[[ -f "${davis_config_path}" ]] || fail "Davis mounted configuration not found: ${davis_config_path}"
		jq -e . "${davis_config_path}" >/dev/null || fail "Davis mounted configuration is not valid JSON: ${davis_config_path}"
		jq -e '
			(.Edge.Runtime.GatewayId == "davis")
			and (.Edge.Runtime.SiteId | type == "string" and length > 0)
			and (.Edge.Runtime.SourceId == .Station.StationId)
			and (.Station.ArchiveCatchupMode == "Disabled")
			and (.Station.LegacyArchiveConsoleUtcOffsetHours | type == "number" and . >= -12 and . <= 14)
			and (.Station.LocalDatabasePath == "/app/data/davis-local.db")
			and (.Outbox.DatabasePath == "/app/data/outbox.db")
			and ((.Outbox.PayloadTypes | sort) == (["com.hvo.weather.archive.v1", "com.hvo.weather.raw.v1"] | sort))' \
			"${davis_config_path}" >/dev/null || fail "Davis gateway.json does not satisfy the vNext deployment contract."

		davis_secrets_setting="$(read_env_value "${env_file}" DAVIS_SECRETS_DIRECTORY)"
		davis_secrets_path="${davis_secrets_setting:-./secrets}"
		[[ "${davis_secrets_path}" = /* ]] || davis_secrets_path="${repo_root}/${compose_dir}/${davis_secrets_path#./}"
		for secret_name in diagnostics-api-key central-ingest-api-key; do
			[[ -s "${davis_secrets_path}/${secret_name}" ]] || fail "Davis required secret file is missing or empty: ${secret_name}"
		done
		if jq -e '.HomeAssistant.Mqtt.Enabled == true' "${davis_config_path}" >/dev/null; then
			for secret_name in mqtt-username mqtt-password; do
				[[ -s "${davis_secrets_path}/${secret_name}" ]] || fail "Davis required MQTT secret file is missing or empty: ${secret_name}"
			 done
		fi
		davis_station_host="$(read_env_value "${env_file}" DAVIS_STATION_HOST)"
		[[ -n "${davis_station_host}" ]] || fail "DAVIS_STATION_HOST is required in the Davis .env file."
		davis_remote_config="$(read_env_value "${env_file}" DAVIS_REMOTE_CONFIG_FILE)"
		[[ -n "${davis_remote_config}" ]] || fail "DAVIS_REMOTE_CONFIG_FILE is required in the Davis .env file."
		davis_remote_secrets="$(read_env_value "${env_file}" DAVIS_REMOTE_SECRETS_DIRECTORY)"
		[[ -n "${davis_remote_secrets}" ]] || fail "DAVIS_REMOTE_SECRETS_DIRECTORY is required in the Davis .env file."
		sync_remote_mounts davis "${davis_config_path}" "${davis_secrets_path}" "${davis_remote_config}" "${davis_remote_secrets}"
	fi
	if [[ "${gateway}" == jkbms ]]; then
		local jkbms_config_setting jkbms_config_path jkbms_secrets_setting jkbms_secrets_path jkbms_remote_config jkbms_remote_secrets
		jkbms_config_setting="$(read_env_value "${env_file}" JKBMS_CONFIG_FILE)"
		jkbms_config_path="${jkbms_config_setting:-./gateway.json}"
		[[ "${jkbms_config_path}" = /* ]] || jkbms_config_path="${repo_root}/${compose_dir}/${jkbms_config_path#./}"
		[[ -f "${jkbms_config_path}" ]] || fail "JK BMS mounted configuration not found: ${jkbms_config_path}"
		jq -e . "${jkbms_config_path}" >/dev/null || fail "JK BMS mounted configuration is not valid JSON: ${jkbms_config_path}"
		jq -e '
			(.Edge.Runtime.GatewayId == "jkbms")
			and (.Outbox.DatabasePath == "/app/data/outbox.db")
			and (.Outbox.PayloadType == "com.hvo.bms.reading.v1")
			and ([.JkBms.Devices[] | select(.Enabled == true)] | length > 0)
			and all(.JkBms.Devices[] | select(.Enabled == true);
				(.Address | type == "string" and length > 0)
				and (.DeviceId | type == "string" and length > 0)
				and (.Alias | type == "string" and length > 0))' \
			"${jkbms_config_path}" >/dev/null || fail "JK BMS gateway.json does not satisfy the vNext deployment contract."

		jkbms_secrets_setting="$(read_env_value "${env_file}" JKBMS_SECRETS_DIRECTORY)"
		jkbms_secrets_path="${jkbms_secrets_setting:-./secrets}"
		[[ "${jkbms_secrets_path}" = /* ]] || jkbms_secrets_path="${repo_root}/${compose_dir}/${jkbms_secrets_path#./}"
		for secret_name in diagnostics-api-key central-ingest-api-key; do
			[[ -s "${jkbms_secrets_path}/${secret_name}" ]] || fail "JK BMS required secret file is missing or empty: ${secret_name}"
		done
		if jq -e '.HomeAssistant.Mqtt.Enabled == true' "${jkbms_config_path}" >/dev/null; then
			for secret_name in mqtt-username mqtt-password; do
				[[ -s "${jkbms_secrets_path}/${secret_name}" ]] || fail "JK BMS required MQTT secret file is missing or empty: ${secret_name}"
			done
		fi
		jkbms_remote_config="$(read_env_value "${env_file}" JKBMS_REMOTE_CONFIG_FILE)"
		[[ -n "${jkbms_remote_config}" ]] || fail "JKBMS_REMOTE_CONFIG_FILE is required in the JK BMS .env file."
		jkbms_remote_secrets="$(read_env_value "${env_file}" JKBMS_REMOTE_SECRETS_DIRECTORY)"
		[[ -n "${jkbms_remote_secrets}" ]] || fail "JKBMS_REMOTE_SECRETS_DIRECTORY is required in the JK BMS .env file."
		sync_remote_mounts jkbms "${jkbms_config_path}" "${jkbms_secrets_path}" "${jkbms_remote_config}" "${jkbms_remote_secrets}"
	fi
	if [[ "${gateway}" == smartshunt ]]; then
		local smartshunt_config_path smartshunt_secrets_path smartshunt_remote_config smartshunt_remote_secrets
		preflight_smartshunt_contract "${env_file}" "${compose_dir}" \
			smartshunt_config_path smartshunt_secrets_path smartshunt_remote_config smartshunt_remote_secrets
		sync_remote_mounts smartshunt "${smartshunt_config_path}" "${smartshunt_secrets_path}" "${smartshunt_remote_config}" "${smartshunt_remote_secrets}"
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
		all|davis|eg4|ha-exporter|home-assistant-exporter|jkbms|smartshunt)
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
	# Fail the newly migrated SmartShunt contract before any earlier stack can be changed.
	smartshunt_preflight_config=""
	smartshunt_preflight_secrets=""
	smartshunt_preflight_remote_config=""
	smartshunt_preflight_remote_secrets=""
	preflight_smartshunt_contract "${repo_root}/deploy/pi-gateways/smartshunt/.env" "deploy/pi-gateways/smartshunt" \
		smartshunt_preflight_config smartshunt_preflight_secrets smartshunt_preflight_remote_config smartshunt_preflight_remote_secrets
	for gateway in davis jkbms smartshunt; do
		deploy_target "${gateway}"
	done
else
	deploy_target "${target}"
fi
