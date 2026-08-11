#!/usr/bin/env bash

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
compose_file="${repo_root}/deploy/pi-gateways/eg4/docker-compose.yml"
two_device_file="${repo_root}/deploy/pi-gateways/eg4/docker-compose.two-device.yml"
mppt_file="${repo_root}/deploy/pi-gateways/eg4/docker-compose.mppt.yml"
config_example="${repo_root}/deploy/pi-gateways/eg4/gateway.json.example"
tmp_dir="$(mktemp -d "${TMPDIR:-/tmp}/hvo-eg4-deploy.XXXXXX")"
secrets_dir="${tmp_dir}/secrets"
sentinel='eg4-ci-secret-must-not-appear'

cleanup() {
	rm -rf "${tmp_dir}"
}
trap cleanup EXIT

fail() {
	printf 'EG4 deployment validation failed: %s\n' "$*" >&2
	exit 1
}

write_profile() {
	local name="$1"
	local second_enabled="$2"
	local mppt_enabled="$3"
	local reordered="$4"
	local config="${tmp_dir}/${name}.json"
	local env_file="${tmp_dir}/${name}.env"

	jq --argjson second_enabled "${second_enabled}" --argjson mppt_enabled "${mppt_enabled}" --argjson reordered "${reordered}" '
		.Eg4.Devices[1].Enabled = $mppt_enabled |
		.Eg4.Devices[1].Port = "/dev/serial/by-id/usb-eg4-mppt-a" |
		.Eg4.Devices[2].Enabled = $second_enabled |
		if $reordered then .Eg4.Devices = [.Eg4.Devices[2], .Eg4.Devices[1], .Eg4.Devices[0]] else . end
	' "${config_example}" >"${config}"
	{
		printf 'EG4_HTTP_PORT=5600\n'
		printf 'EG4_CONFIG_FILE=%s\n' "${config}"
		printf 'EG4_SECRETS_DIRECTORY=%s\n' "${secrets_dir}"
		printf 'EG4_DEVICE_0_PORT=/dev/hvo/eg4-6500ex-a\n'
		printf 'EG4_MPPT_0_ENABLED=%s\n' "${mppt_enabled}"
		printf 'EG4_MPPT_0_PORT=/dev/serial/by-id/usb-eg4-mppt-a\n'
		printf 'EG4_DEVICE_1_ENABLED=%s\n' "${second_enabled}"
		printf 'EG4_DEVICE_1_PORT=/dev/hvo/eg4-6500ex-b\n'
	} >"${env_file}"
	printf '%s\n' "${env_file}"
}

command -v docker >/dev/null 2>&1 || fail 'docker is required'
command -v jq >/dev/null 2>&1 || fail 'jq is required'

mkdir -p "${secrets_dir}"
for secret_name in diagnostics-api-key central-ingest-api-key mqtt-username mqtt-password; do
	printf '%s\n' "${sentinel}" >"${secrets_dir}/${secret_name}"
done

base_env="$(write_profile base false false false)"
two_env="$(write_profile two true false false)"
reordered_env="$(write_profile reordered true false true)"
mppt_env="$(write_profile mppt false true false)"

docker compose --env-file "${base_env}" -f "${compose_file}" config --quiet
base_config="$(docker compose --env-file "${base_env}" -f "${compose_file}" config --format json)"
jq -e '
	(.services | keys) == ["hvo-eg4"] and
	.services["hvo-eg4"].ports[0].published == "5600" and
	.services["hvo-eg4"].ports[0].target == 8080 and
	.services["hvo-eg4"].devices == [{"source":"/dev/hvo/eg4-6500ex-a","target":"/dev/hvo/eg4-6500ex-a","permissions":"rw"}] and
	(.services["hvo-eg4"].environment | keys | all(startswith("Eg4__") or startswith("Outbox__") or contains("API_KEY") | not)) and
	.services["hvo-eg4"].restart == "unless-stopped" and
	.services["hvo-eg4"].logging.driver == "local" and
	.services["hvo-eg4"].logging.options["max-size"] == "10m" and
	.services["hvo-eg4"].logging.options["max-file"] == "3" and
	.services["hvo-eg4"].logging.options.mode == "non-blocking" and
	.services["hvo-eg4"].ulimits.core != null and
	(.services["hvo-eg4"].volumes | any(.source == "eg4-outbox" and .target == "/app/data")) and
	(.services["hvo-eg4"].volumes | any(.type == "bind" and .target == "/app/config/gateway.json" and .read_only == true)) and
	(.services["hvo-eg4"].volumes | any(.type == "bind" and .target == "/run/secrets" and .read_only == true)) and
	.services["hvo-eg4"].healthcheck.test == ["CMD", "curl", "--fail", "http://localhost:8080/health"]
' <<<"${base_config}" >/dev/null || fail 'base Compose policy or mounted configuration profile is incorrect'

for env_file in "${two_env}" "${reordered_env}"; do
	docker compose --env-file "${env_file}" -f "${compose_file}" -f "${two_device_file}" config --quiet
	config="$(docker compose --env-file "${env_file}" -f "${compose_file}" -f "${two_device_file}" config --format json)"
	jq -e '(.services["hvo-eg4"].devices | map(.source) | sort) == ["/dev/hvo/eg4-6500ex-a", "/dev/hvo/eg4-6500ex-b"]' \
		<<<"${config}" >/dev/null || fail 'two-device overlay changed stable physical mappings'
done

docker compose --env-file "${mppt_env}" -f "${compose_file}" -f "${mppt_file}" config --quiet
mppt_config="$(docker compose --env-file "${mppt_env}" -f "${compose_file}" -f "${mppt_file}" config --format json)"
jq -e '(.services["hvo-eg4"].devices | map(.source) | sort) == ["/dev/hvo/eg4-6500ex-a", "/dev/serial/by-id/usb-eg4-mppt-a"]' \
	<<<"${mppt_config}" >/dev/null || fail 'MPPT overlay changed the fixed serial mapping'

dry_run_output="$(env -i PATH="${PATH}" HOME="${HOME}" HVO_PI_GATEWAY_ENV_FILE="${base_env}" \
	bash "${repo_root}/scripts/deploy-pi-gateway.sh" --dry-run --context devpi5 eg4 2>&1)"
[[ "${dry_run_output}" == *'--project-name eg4'* && "${dry_run_output}" == *'config --quiet'* && "${dry_run_output}" == *'up -d --wait'* ]] ||
	fail 'EG4 deploy dry run omitted Compose validation or rollout commands'
[[ "${dry_run_output}" != *"${sentinel}"* ]] || fail 'EG4 deploy dry run exposed a mounted secret'
[[ "${dry_run_output}" != *'docker-compose.two-device.yml'* ]] || fail 'disabled second device unexpectedly enabled the overlay'

two_dry_run_output="$(env -i PATH="${PATH}" HOME="${HOME}" HVO_PI_GATEWAY_ENV_FILE="${reordered_env}" \
	bash "${repo_root}/scripts/deploy-pi-gateway.sh" --dry-run --context devpi5 eg4 2>&1)"
[[ "${two_dry_run_output}" == *'docker-compose.two-device.yml'* ]] || fail 'enabled second device omitted the overlay'
[[ "${two_dry_run_output}" != *"${sentinel}"* ]] || fail 'two-device dry run exposed a mounted secret'

mppt_dry_run_output="$(env -i PATH="${PATH}" HOME="${HOME}" HVO_PI_GATEWAY_ENV_FILE="${mppt_env}" \
	bash "${repo_root}/scripts/deploy-pi-gateway.sh" --dry-run --context devpi5 eg4 2>&1)"
[[ "${mppt_dry_run_output}" == *'docker-compose.mppt.yml'* ]] || fail 'enabled MPPT omitted the overlay'

set +e
override_output="$(env -i PATH="${PATH}" HOME="${HOME}" HVO_PI_GATEWAY_ENV_FILE="${base_env}" EG4_HTTP_PORT=5999 \
	bash "${repo_root}/scripts/deploy-pi-gateway.sh" --dry-run --context devpi5 eg4 2>&1)"
override_status="$?"
set -e
[[ "${override_status}" -ne 0 ]] || fail 'EG4 deploy accepted an unapproved shell environment override'
[[ "${override_output}" == *'EG4_HTTP_PORT'* ]] || fail 'EG4 override failure omitted the variable name'
[[ "${override_output}" != *'5999'* && "${override_output}" != *"${sentinel}"* ]] || fail 'EG4 override failure exposed a value'

printf 'EG4 deployment artifacts, config/device agreement, and secret-safe dry run validated.\n'
