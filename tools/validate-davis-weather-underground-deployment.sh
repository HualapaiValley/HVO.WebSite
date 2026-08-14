#!/usr/bin/env bash

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
compose_file="${repo_root}/deploy/pi-gateways/davis/docker-compose.yml"
config_example="${repo_root}/deploy/pi-gateways/davis/gateway.json.example"
tmp_dir="$(mktemp -d "${TMPDIR:-/tmp}/hvo-davis-wu-deploy.XXXXXX")"
secrets_dir="${tmp_dir}/secrets"
sentinel='davis-wu-ci-secret-must-not-appear'
trap 'rm -rf "${tmp_dir}"' EXIT

fail() {
	printf 'Davis Weather Underground deployment validation failed: %s\n' "$*" >&2
	exit 1
}

write_env_file() {
	local name="$1"
	local config="$2"
	local env_file="${tmp_dir}/${name}.env"
	{
		printf 'DAVIS_HTTP_PORT=5100\n'
		printf 'DAVIS_STATION_HOST=192.0.2.10\n'
		printf 'DAVIS_CONFIG_FILE=%s\n' "${config}"
		printf 'DAVIS_SECRETS_DIRECTORY=%s\n' "${secrets_dir}"
		printf 'DAVIS_REMOTE_CONFIG_FILE=/home/test/.local/share/hvo-edge/davis/gateway.json\n'
		printf 'DAVIS_REMOTE_SECRETS_DIRECTORY=/home/test/.local/share/hvo-edge/davis/secrets\n'
	} >"${env_file}"
	printf '%s\n' "${env_file}"
}

run_deploy_dry_run() {
	local env_file="$1"
	env -i PATH="${PATH}" HOME="${HOME}" HVO_PI_GATEWAY_ENV_FILE="${env_file}" \
		bash "${repo_root}/scripts/deploy-pi-gateway.sh" --dry-run --context devpi5 davis 2>&1
}

expect_contract_failure() {
	local name="$1"
	local filter="$2"
	local invalid_config="${tmp_dir}/${name}.json"
	local invalid_env output status
	jq "${filter}" "${enabled_config}" >"${invalid_config}"
	invalid_env="$(write_env_file "${name}" "${invalid_config}")"
	set +e
	output="$(run_deploy_dry_run "${invalid_env}")"
	status="$?"
	set -e
	[[ "${status}" -ne 0 ]] || fail "invalid ${name} configuration passed preflight"
	[[ "${output}" != *'[dry-run] ssh'* ]] || fail "invalid ${name} configuration reached remote mutation"
	[[ "${output}" != *"${sentinel}"* ]] || fail "invalid ${name} failure exposed a secret"
}

validate_key_vault_sync() {
	local fixture="${tmp_dir}/sync-fixture"
	local fake_bin="${fixture}/bin"
	local call_log="${fixture}/az-calls"
	local sync_output
	mkdir -p "${fixture}/scripts" "${fixture}/deploy/pi-gateways/davis" "${fake_bin}"
	cp "${repo_root}/scripts/sync-secrets-from-keyvault.sh" "${fixture}/scripts/"
	{
		printf 'SSH_PRIVATE_KEY=%s\n' "${sentinel}"
		printf 'ConnectionStrings__HualapaiValleyObservatory=Server=localhost;Password=fixture;Database=hvo;\n'
	} >"${fixture}/.env"
	{
		printf '%s\n' '#!/usr/bin/env bash'
		printf '%s\n' 'set -euo pipefail'
		printf '%s\n' 'if [[ "$1" == "account" && "$2" == "show" ]]; then exit 0; fi'
		printf '%s\n' 'if [[ "$1" == "keyvault" && "$2" == "show" ]]; then exit 0; fi'
		printf '%s\n' 'if [[ "$1" == "keyvault" && "$2" == "secret" && "$3" == "show" ]]; then'
		printf '%s\n' '  secret_name=""'
		printf '%s\n' '  while (($# > 0)); do'
		printf '%s\n' '    if [[ "$1" == "--name" ]]; then secret_name="$2"; break; fi'
		printf '%s\n' '    shift'
		printf '%s\n' '  done'
		printf '%s\n' '  printf "%s\n" "${secret_name}" >>"${AZ_CALL_LOG:?}"'
		printf '%s\n' '  printf "%s" "${AZ_SECRET_VALUE:?}"'
		printf '%s\n' '  exit 0'
		printf '%s\n' 'fi'
		printf '%s\n' 'exit 1'
	} >"${fake_bin}/az"
	chmod 700 "${fake_bin}/az"

	: >"${call_log}"
	sync_output="$(PATH="${fake_bin}:${PATH}" AZ_CALL_LOG="${call_log}" AZ_SECRET_VALUE="${sentinel}" \
		bash "${fixture}/scripts/sync-secrets-from-keyvault.sh" --apply 2>&1)"
	[[ ! -e "${fixture}/deploy/pi-gateways/davis/secrets/weather-underground-station-key" ]] ||
		fail 'non-materialized Davis config caused station-key materialization'
	! grep -qx 'WeatherUnderground--StationKey' "${call_log}" || fail 'non-materialized Davis config caused station-key retrieval'
	! grep -qx 'WeatherUnderground--ApiKey' "${call_log}" || fail 'Key Vault sync retrieved the query API key'
	[[ "${sync_output}" != *"${sentinel}"* ]] || fail 'Key Vault sync without Davis config exposed a secret value'

	cp "${disabled_config}" "${fixture}/deploy/pi-gateways/davis/gateway.json"
	: >"${call_log}"
	sync_output="$(PATH="${fake_bin}:${PATH}" AZ_CALL_LOG="${call_log}" AZ_SECRET_VALUE="${sentinel}" \
		bash "${fixture}/scripts/sync-secrets-from-keyvault.sh" --apply 2>&1)"
	[[ ! -e "${fixture}/deploy/pi-gateways/davis/secrets/weather-underground-station-key" ]] ||
		fail 'disabled Key Vault sync materialized the station key'
	! grep -qx 'WeatherUnderground--StationKey' "${call_log}" || fail 'disabled Key Vault sync retrieved the station key'
	! grep -qx 'WeatherUnderground--ApiKey' "${call_log}" || fail 'Key Vault sync retrieved the query API key'
	[[ "${sync_output}" != *"${sentinel}"* ]] || fail 'disabled Key Vault sync exposed a secret value'

	cp "${enabled_config}" "${fixture}/deploy/pi-gateways/davis/gateway.json"
	: >"${call_log}"
	sync_output="$(PATH="${fake_bin}:${PATH}" AZ_CALL_LOG="${call_log}" AZ_SECRET_VALUE="${sentinel}" \
		bash "${fixture}/scripts/sync-secrets-from-keyvault.sh" --apply 2>&1)"
	[[ -s "${fixture}/deploy/pi-gateways/davis/secrets/weather-underground-station-key" ]] ||
		fail 'enabled Key Vault sync did not materialize the station key'
	[[ "$(<"${fixture}/deploy/pi-gateways/davis/secrets/weather-underground-station-key")" == "${sentinel}" ]] ||
		fail 'enabled Key Vault sync wrote an unexpected station-key value'
	[[ "$(grep -c '^WeatherUnderground--StationKey$' "${call_log}" || true)" -eq 1 ]] ||
		fail 'enabled Key Vault sync did not retrieve exactly the station key'
	! grep -qx 'WeatherUnderground--ApiKey' "${call_log}" || fail 'enabled Key Vault sync retrieved the query API key'
	[[ "${sync_output}" != *"${sentinel}"* ]] || fail 'enabled Key Vault sync exposed a secret value'
}

command -v docker >/dev/null 2>&1 || fail 'docker is required'
command -v git >/dev/null 2>&1 || fail 'git is required'
command -v jq >/dev/null 2>&1 || fail 'jq is required'
command -v grep >/dev/null 2>&1 || fail 'grep is required'

mkdir -p "${secrets_dir}"
for secret_name in diagnostics-api-key central-ingest-api-key mqtt-username mqtt-password; do
	printf '%s\n' "${sentinel}" >"${secrets_dir}/${secret_name}"
done

disabled_config="${tmp_dir}/disabled.json"
enabled_config="${tmp_dir}/enabled.json"
jq '.' "${config_example}" >"${disabled_config}"
jq '.WeatherUnderground.Enabled = true' "${config_example}" >"${enabled_config}"
disabled_env="$(write_env_file disabled "${disabled_config}")"
enabled_env="$(write_env_file enabled "${enabled_config}")"

docker compose --env-file "${disabled_env}" -f "${compose_file}" config --quiet
resolved="$(docker compose --env-file "${disabled_env}" -f "${compose_file}" config --format json)"
jq -e '
	(.services | keys) == ["hvo-davis"] and
	(.services["hvo-davis"].volumes | any(.type == "bind" and .target == "/run/secrets" and .read_only == true)) and
	([.services["hvo-davis"].environment | keys[] | select(test("WeatherUnderground|WEATHER_UNDERGROUND|API.?KEY"; "i"))] | length) == 0
' <<<"${resolved}" >/dev/null || fail 'Compose secret mount is not read-only or a credential entered the environment'

disabled_output="$(run_deploy_dry_run "${disabled_env}")"
[[ "${disabled_output}" == *'config --quiet'* && "${disabled_output}" == *'up -d --wait'* ]] ||
	fail 'disabled profile did not reach the secret-safe deployment dry run'
[[ "${disabled_output}" != *"${sentinel}"* ]] || fail 'disabled dry run exposed a mounted secret'

set +e
missing_output="$(run_deploy_dry_run "${enabled_env}")"
missing_status="$?"
set -e
[[ "${missing_status}" -ne 0 ]] || fail 'enabled profile accepted a missing station-key file'
[[ "${missing_output}" == *'weather-underground-station-key'* && "${missing_output}" != *'[dry-run] ssh'* ]] ||
	fail 'missing station key did not fail before remote mutation'
[[ "${missing_output}" != *"${sentinel}"* ]] || fail 'missing-secret failure exposed another mounted secret'

printf '%s\n' "${sentinel}" >"${secrets_dir}/weather-underground-station-key"
enabled_output="$(run_deploy_dry_run "${enabled_env}")"
[[ "${enabled_output}" == *'config --quiet'* && "${enabled_output}" == *'up -d --wait'* ]] ||
	fail 'enabled profile with a station key did not pass deployment preflight'
[[ "${enabled_output}" != *"${sentinel}"* ]] || fail 'enabled dry run exposed the station key'

expect_contract_failure invalid-enabled '.WeatherUnderground.Enabled = "true"'
expect_contract_failure invalid-station-id '.WeatherUnderground.StationId = "WRONG"'
expect_contract_failure invalid-interval '.WeatherUnderground.IntervalSeconds = 6'
expect_contract_failure invalid-timeout '.WeatherUnderground.RequestTimeoutSeconds = 3'
expect_contract_failure invalid-secret-name '.WeatherUnderground.StationKeySecret = "../station-key"'
expect_contract_failure unexpected-api-key '.WeatherUnderground.ApiKeySecret = "weather-underground-api-key"'

git -C "${repo_root}" check-ignore -q deploy/pi-gateways/davis/gateway.json || fail 'local Davis gateway.json is not ignored'
git -C "${repo_root}" check-ignore -q deploy/pi-gateways/davis/secrets/weather-underground-station-key ||
	fail 'Davis station-key file is not ignored'
if git -C "${repo_root}" check-ignore -q deploy/pi-gateways/davis/gateway.json.example; then
	fail 'tracked Davis gateway.json.example is unexpectedly ignored'
fi
if grep -Ern 'WeatherUnderground--ApiKey' "${repo_root}/scripts/sync-secrets-from-keyvault.sh" \
	"${repo_root}/scripts/deploy-pi-gateway.sh" "${repo_root}/deploy/pi-gateways/davis" >/dev/null; then
	fail 'deployment artifacts reference the Weather Underground query API key'
fi
if grep -Ern 'WeatherUnderground.*(Key|Secret)|WEATHER_UNDERGROUND.*(KEY|SECRET)' \
	"${repo_root}/deploy/pi-gateways/davis/docker-compose.yml" "${repo_root}/deploy/pi-gateways/davis/.env.example" >/dev/null; then
	fail 'Weather Underground credentials are present in Compose or environment configuration'
fi

validate_key_vault_sync

printf 'Davis Weather Underground disabled/enabled preflight, secret sync, Compose, ignore, and API-key isolation contracts validated.\n'
