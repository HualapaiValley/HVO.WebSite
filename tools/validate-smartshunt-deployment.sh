#!/usr/bin/env bash

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
compose_file="${repo_root}/deploy/pi-gateways/smartshunt/docker-compose.yml"
config_example="${repo_root}/deploy/pi-gateways/smartshunt/gateway.json.example"
tmp_dir="$(mktemp -d "${TMPDIR:-/tmp}/hvo-smartshunt-deploy.XXXXXX")"
secrets_dir="${tmp_dir}/secrets"
sentinel='smartshunt-ci-secret-must-not-appear'
trap 'rm -rf "${tmp_dir}"' EXIT
fail() { printf 'SmartShunt deployment validation failed: %s\n' "$*" >&2; exit 1; }

command -v docker >/dev/null 2>&1 || fail 'docker is required'
command -v jq >/dev/null 2>&1 || fail 'jq is required'
mkdir -p "${secrets_dir}"
for secret_name in diagnostics-api-key central-ingest-api-key mqtt-username mqtt-password; do printf '%s\n' "${sentinel}" >"${secrets_dir}/${secret_name}"; done
config="${tmp_dir}/gateway.json"
jq '.SmartShunt.Address = "AA:BB:CC:DD:EE:FF"' "${config_example}" >"${config}"
env_file="${tmp_dir}/smartshunt.env"
{
  printf 'SMARTSHUNT_HTTP_PORT=5400\nSMARTSHUNT_CONFIG_FILE=%s\nSMARTSHUNT_SECRETS_DIRECTORY=%s\n' "${config}" "${secrets_dir}"
  printf 'SMARTSHUNT_REMOTE_CONFIG_FILE=/home/test/.local/share/hvo-edge/smartshunt/gateway.json\n'
  printf 'SMARTSHUNT_REMOTE_SECRETS_DIRECTORY=/home/test/.local/share/hvo-edge/smartshunt/secrets\n'
} >"${env_file}"

docker compose --env-file "${env_file}" -f "${compose_file}" config --quiet
resolved="$(docker compose --env-file "${env_file}" -f "${compose_file}" config --format json)"
jq -e '
  (.services | keys) == ["hvo-smartshunt"] and
  .services["hvo-smartshunt"].privileged == true and
  .services["hvo-smartshunt"].restart == "unless-stopped" and
  ([.services["hvo-smartshunt"].environment | keys[] | select(startswith("SmartShunt__") or startswith("Outbox__") or contains("API_KEY"))] | length) == 0 and
  (.services["hvo-smartshunt"].volumes | any(.source == "smartshunt-outbox" and .target == "/app/data")) and
  (.services["hvo-smartshunt"].volumes | any(.type == "bind" and .target == "/app/config/gateway.json" and .read_only == true)) and
  (.services["hvo-smartshunt"].volumes | any(.type == "bind" and .target == "/run/secrets" and .read_only == true)) and
  .services["hvo-smartshunt"].healthcheck.test == ["CMD", "curl", "--fail", "http://localhost:8080/health/live"]
' <<<"${resolved}" >/dev/null || fail 'Compose contract is incorrect'

dry_run="$(env -i PATH="${PATH}" HOME="${HOME}" HVO_PI_GATEWAY_ENV_FILE="${env_file}" bash "${repo_root}/scripts/deploy-pi-gateway.sh" --dry-run --context devpi5 smartshunt 2>&1)"
[[ "${dry_run}" == *'config --quiet'* && "${dry_run}" == *'up -d --wait'* ]] || fail 'dry run omitted validation or rollout'
[[ "${dry_run}" == *'.staging.'* && "${dry_run}" == *'chmod 600'* && "${dry_run}" == *'activate-remote-gateway-config.sh'* ]] || fail 'dry run omitted restrictive staged replacement'
[[ "${dry_run}" != *"${sentinel}"* ]] || fail 'dry run exposed secret values'

invalid="${tmp_dir}/invalid.json"
jq '.Edge.Runtime.GatewayType = "wrong"' "${config}" >"${invalid}"
invalid_env="${tmp_dir}/invalid.env"
sed "s|SMARTSHUNT_CONFIG_FILE=${config}|SMARTSHUNT_CONFIG_FILE=${invalid}|" "${env_file}" >"${invalid_env}"
set +e
invalid_output="$(env -i PATH="${PATH}" HOME="${HOME}" HVO_PI_GATEWAY_ENV_FILE="${invalid_env}" bash "${repo_root}/scripts/deploy-pi-gateway.sh" --dry-run --context devpi5 smartshunt 2>&1)"
invalid_status=$?
set -e
[[ "${invalid_status}" -ne 0 && "${invalid_output}" != *'[dry-run] ssh'* ]] || fail 'invalid contract reached remote mutation'

printf 'SmartShunt deployment artifacts and secret-safe preflight validated.\n'
