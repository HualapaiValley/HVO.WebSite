#!/usr/bin/env bash

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
compose_file="${repo_root}/deploy/pi-gateways/eg4/docker-compose.yml"
two_device_file="${repo_root}/deploy/pi-gateways/eg4/docker-compose.two-device.yml"
tmp_dir="$(mktemp -d "${TMPDIR:-/tmp}/hvo-eg4-deploy.XXXXXX")"
sentinel='eg4-ci-secret-must-not-appear'

cleanup() {
	rm -rf "${tmp_dir}"
}
trap cleanup EXIT

fail() {
	printf 'EG4 deployment validation failed: %s\n' "$*" >&2
	exit 1
}

write_env() {
	local path="$1"
	local first="$2"
	local second="$3"
	local second_enabled="$4"
	{
		printf '%s\n' 'HVO_WEBSITE_PUBLIC_BASE_URL=https://www.hualapaivalleyobservatory.org'
		printf 'EG4_POWER_API_KEY=%s\n' "${sentinel}"
		printf '%s\n' 'EG4_HTTP_PORT=5600'
		printf 'EG4_DEVICE_0_ENABLED=true\n'
		printf 'EG4_DEVICE_0_SOURCE_ID=eg4-6500ex-%s\n' "${first}"
		printf 'EG4_DEVICE_0_DEVICE_ID=inverter-%s\n' "${first}"
		printf 'EG4_DEVICE_0_ALIAS=6500EX Inverter %s\n' "${first^^}"
		printf 'EG4_DEVICE_0_PORT=/dev/hvo/eg4-6500ex-%s\n' "${first}"
		printf 'EG4_MPPT_0_SOURCE_ID=eg4-mppt100-48hv-a\n'
		printf 'EG4_MPPT_0_DEVICE_ID=controller-a\n'
		printf 'EG4_MPPT_0_ALIAS=MPPT100-48HV Controller A\n'
		printf 'EG4_MPPT_0_PORT=/dev/serial/by-id/usb-eg4-mppt-a\n'
		printf 'EG4_DEVICE_1_ENABLED=%s\n' "${second_enabled}"
		printf 'EG4_DEVICE_1_SOURCE_ID=eg4-6500ex-%s\n' "${second}"
		printf 'EG4_DEVICE_1_DEVICE_ID=inverter-%s\n' "${second}"
		printf 'EG4_DEVICE_1_ALIAS=6500EX Inverter %s\n' "${second^^}"
		printf 'EG4_DEVICE_1_PORT=/dev/hvo/eg4-6500ex-%s\n' "${second}"
	} > "${path}"
}

command -v docker >/dev/null 2>&1 || fail 'docker is required'
command -v jq >/dev/null 2>&1 || fail 'jq is required'

base_env="${tmp_dir}/base.env"
two_env="${tmp_dir}/two.env"
reordered_env="${tmp_dir}/reordered.env"
write_env "${base_env}" a b false
write_env "${two_env}" a b true
write_env "${reordered_env}" b a true

docker compose --env-file "${base_env}" -f "${compose_file}" config --quiet
base_config="$(docker compose --env-file "${base_env}" -f "${compose_file}" config --format json)"
jq -e '
	(.services | keys) == ["hvo-eg4"] and
	.services["hvo-eg4"].ports[0].published == "5600" and
	.services["hvo-eg4"].ports[0].target == 8080 and
	.services["hvo-eg4"].devices == [{"source":"/dev/hvo/eg4-6500ex-a","target":"/dev/hvo/eg4-6500ex-a","permissions":"rw"}] and
	.services["hvo-eg4"].environment.Eg4__SimulationEnabled == "false" and
	.services["hvo-eg4"].environment.Eg4__Devices__0__SourceId == "eg4-6500ex-a" and
	.services["hvo-eg4"].environment.Eg4__Devices__0__DeviceId == "inverter-a" and
	.services["hvo-eg4"].environment.Eg4__Devices__0__Port == "/dev/hvo/eg4-6500ex-a" and
	.services["hvo-eg4"].environment.Eg4__Devices__0__UnitId == "0" and
	.services["hvo-eg4"].environment.Eg4__Devices__1__Type == "ChargeControllerMppt10048Hv" and
	.services["hvo-eg4"].environment.Eg4__Devices__1__SourceId == "eg4-mppt100-48hv-a" and
	.services["hvo-eg4"].environment.Eg4__Devices__1__DeviceId == "controller-a" and
	.services["hvo-eg4"].environment.Eg4__Devices__1__Enabled == "false" and
	.services["hvo-eg4"].environment.Eg4__Devices__1__Port == "/dev/serial/by-id/usb-eg4-mppt-a" and
	.services["hvo-eg4"].environment.Eg4__Devices__1__UnitId == "1" and
	(.services["hvo-eg4"].environment | has("Eg4__Devices__2__SourceId") | not) and
	.services["hvo-eg4"].environment.Outbox__DbPath == "/app/data/outbox.db" and
	.services["hvo-eg4"].environment.Outbox__ApiEndpoint == "https://www.hualapaivalleyobservatory.org/api/v1/power/readings" and
	.services["hvo-eg4"].restart == "unless-stopped" and
	.services["hvo-eg4"].logging.driver == "local" and
	.services["hvo-eg4"].logging.options["max-size"] == "10m" and
	.services["hvo-eg4"].logging.options["max-file"] == "3" and
	.services["hvo-eg4"].logging.options.mode == "non-blocking" and
	.services["hvo-eg4"].ulimits.core != null and
	(.services["hvo-eg4"].volumes | any(.source == "eg4-outbox" and .target == "/app/data")) and
	.services["hvo-eg4"].healthcheck.test == ["CMD", "curl", "--fail", "http://localhost:8080/health"]
' <<<"${base_config}" >/dev/null || fail 'base Compose policy or one-device commissioning profile is incorrect'

for specification in "${two_env}:a:b" "${reordered_env}:b:a"; do
	IFS=':' read -r env_file first second <<<"${specification}"
	docker compose --env-file "${env_file}" -f "${compose_file}" -f "${two_device_file}" config --quiet
	config="$(docker compose --env-file "${env_file}" -f "${compose_file}" -f "${two_device_file}" config --format json)"
	jq -e --arg first "${first}" --arg second "${second}" '
		(.services["hvo-eg4"].devices | map(.source) | sort) == ["/dev/hvo/eg4-6500ex-a", "/dev/hvo/eg4-6500ex-b"] and
		.services["hvo-eg4"].environment.Eg4__Devices__0__SourceId == ("eg4-6500ex-" + $first) and
		.services["hvo-eg4"].environment.Eg4__Devices__0__DeviceId == ("inverter-" + $first) and
		.services["hvo-eg4"].environment.Eg4__Devices__0__Port == ("/dev/hvo/eg4-6500ex-" + $first) and
		.services["hvo-eg4"].environment.Eg4__Devices__0__Enabled == "true" and
		.services["hvo-eg4"].environment.Eg4__Devices__1__SourceId == "eg4-mppt100-48hv-a" and
		.services["hvo-eg4"].environment.Eg4__Devices__1__Enabled == "false" and
		.services["hvo-eg4"].environment.Eg4__Devices__2__SourceId == ("eg4-6500ex-" + $second) and
		.services["hvo-eg4"].environment.Eg4__Devices__2__DeviceId == ("inverter-" + $second) and
		.services["hvo-eg4"].environment.Eg4__Devices__2__Port == ("/dev/hvo/eg4-6500ex-" + $second) and
		.services["hvo-eg4"].environment.Eg4__Devices__2__Enabled == "true"
	' <<<"${config}" >/dev/null || fail 'stable source/device/port identity changed after device reordering'
done

dry_run_output="$(env -i PATH="${PATH}" HOME="${HOME}" HVO_PI_GATEWAY_ENV_FILE="${base_env}" \
	bash "${repo_root}/scripts/deploy-pi-gateway.sh" --dry-run --context devpi5 eg4 2>&1)"
[[ "${dry_run_output}" == *'--project-name eg4'* && "${dry_run_output}" == *'config --quiet'* && "${dry_run_output}" == *'up -d --wait'* ]] ||
	fail 'EG4 deploy dry run omitted Compose validation or rollout commands'
[[ "${dry_run_output}" != *"${sentinel}"* ]] || fail 'EG4 deploy dry run exposed the API key'
[[ "${dry_run_output}" != *'docker-compose.two-device.yml'* ]] || fail 'disabled second device unexpectedly enabled the overlay'

two_dry_run_output="$(env -i PATH="${PATH}" HOME="${HOME}" HVO_PI_GATEWAY_ENV_FILE="${two_env}" \
	bash "${repo_root}/scripts/deploy-pi-gateway.sh" --dry-run --context devpi5 eg4 2>&1)"
[[ "${two_dry_run_output}" == *'docker-compose.two-device.yml'* ]] || fail 'enabled second device omitted the overlay'
[[ "${two_dry_run_output}" != *"${sentinel}"* ]] || fail 'two-device dry run exposed the API key'

set +e
override_output="$(env -i PATH="${PATH}" HOME="${HOME}" HVO_PI_GATEWAY_ENV_FILE="${base_env}" EG4_HTTP_PORT=5999 \
	bash "${repo_root}/scripts/deploy-pi-gateway.sh" --dry-run --context devpi5 eg4 2>&1)"
override_status="$?"
set -e
[[ "${override_status}" -ne 0 ]] || fail 'EG4 deploy accepted an unapproved shell environment override'
[[ "${override_output}" == *'EG4_HTTP_PORT'* ]] || fail 'EG4 override failure omitted the variable name'
[[ "${override_output}" != *'5999'* && "${override_output}" != *"${sentinel}"* ]] || fail 'EG4 override failure exposed a value'

printf 'EG4 deployment artifacts and secret-safe dry run validated.\n'
