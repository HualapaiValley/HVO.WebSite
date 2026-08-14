#!/usr/bin/env bash
set -euo pipefail

usage() {
    printf 'Usage: %s --check|--apply\n' "$0" >&2
    exit 2
}

[[ $# -eq 1 ]] || usage
mode="$1"
[[ "$mode" == "--check" || "$mode" == "--apply" ]] || usage

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
lovelace_file="$repo_root/deploy/home-assistant/configuration/lovelace.yaml"
configuration_root="$repo_root/deploy/home-assistant/configuration"
frontend_root="$configuration_root/frontend"
managed_entities_file="$configuration_root/managed-entities.txt"
ha_url="${HVO_HOME_ASSISTANT_URL:-http://192.168.1.113}"
proxmox_host="${HVO_PROXMOX_HOST:-root@192.168.1.240}"
ha_vmid="${HVO_HOME_ASSISTANT_VMID:-101}"
guest_config_root="/mnt/data/supervisor/homeassistant"

if [[ -z "${HOME_ASSISTANT_TOKEN:-}" && -f "$repo_root/.env" ]]; then
    set -a
    # shellcheck disable=SC1091
    source "$repo_root/.env"
    set +a
fi

: "${HOME_ASSISTANT_TOKEN:?HOME_ASSISTANT_TOKEN must be set in the environment or root .env}"
if [[ "$ha_url" == http://* && "${HVO_HOME_ASSISTANT_ALLOW_INSECURE:-false}" != "true" ]]; then
    printf 'Refusing to send the Home Assistant token over HTTP. Set HVO_HOME_ASSISTANT_ALLOW_INSECURE=true for the trusted local network.\n' >&2
    exit 1
fi

mapfile -t referenced_entities < <(
    rg --no-filename --only-matching '([a-z0-9_]+_entity: |entity: |entity_id: |^\s+- )[a-z0-9_]+\.[a-z0-9_]+' \
        "$configuration_root/dashboards" "$configuration_root/automations" --glob '*.yaml' |
        sed -E 's/.*(: |- )//' |
        sort -u
)

states="$(curl -fsS \
    -H "Authorization: Bearer ${HOME_ASSISTANT_TOKEN}" \
    -H "Content-Type: application/json" \
    "$ha_url/api/states")"

if ! jq -e '.[] | select(.entity_id == "weather.forecast_home") | .attributes.cloud_coverage | numbers' >/dev/null <<<"$states"; then
    printf 'Commissioned external weather entity weather.forecast_home must expose numeric cloud_coverage.\n' >&2
    exit 1
fi

missing=0
for entity_id in "${referenced_entities[@]}"; do
    if ! jq -e --arg entity_id "$entity_id" '.[] | select(.entity_id == $entity_id)' >/dev/null <<<"$states" \
        && ! grep -Fxq "$entity_id" "$managed_entities_file"; then
        printf 'Missing Home Assistant entity: %s\n' "$entity_id" >&2
        missing=1
    fi
done

if (( missing != 0 )); then
    exit 1
fi

if rg -n 'https?://|\.storage' "$configuration_root" --glob '*.yaml' >/dev/null; then
    printf 'Home Assistant configuration must not contain external resources.\n' >&2
    exit 1
fi
if rg -n 'https?://|\.storage' "$frontend_root" --glob '*.js' >/dev/null; then
    printf 'Home Assistant frontend assets must not contain external resources.\n' >&2
    exit 1
fi

printf 'Validated %d dashboard and automation entity references.\n' "${#referenced_entities[@]}"
"$repo_root/tools/validate-home-assistant-managed-config.sh"

if [[ "$mode" == "--check" ]]; then
    exit 0
fi

guest_exec() {
    local command="$1"
    local quoted_command
    local response
    printf -v quoted_command '%q' "$command"
    response="$(ssh -o BatchMode=yes "$proxmox_host" \
        "qm guest exec '$ha_vmid' -- /bin/sh -c $quoted_command" </dev/null)"

    if ! jq -e '.exitcode == 0' >/dev/null <<<"$response"; then
        jq -r '."err-data" // ."out-data" // "Guest command failed without output."' <<<"$response" >&2
        return 1
    fi

    printf '%s\n' "$response"
}

deploy_file() {
    local source_file="$1"
    local target_file="$2"
    local target_directory
    local payload
    local chunk
    local source_size
    target_directory="$(dirname "$target_file")"
    source_size="$(stat -c %s "$source_file")"
    payload="$(base64 -w 0 "$source_file")"
    guest_exec "mkdir -p '$target_directory' && : > '$target_file.b64'" >/dev/null
    while [[ -n "$payload" ]]; do
        chunk="${payload:0:4000}"
        payload="${payload:4000}"
        guest_exec "printf '%s' '$chunk' >> '$target_file.b64'" >/dev/null
    done
    guest_exec "base64 -d '$target_file.b64' > '$target_file' && rm '$target_file.b64' && test \"\$(wc -c < '$target_file')\" -eq '$source_size'" >/dev/null
}

deploy_directory() {
    local directory="$1"
    local target_root="$2"
    local source_file
    while IFS= read -r -d '' source_file; do
        deploy_file "$source_file" "$target_root/${source_file#"$configuration_root/"}"
    done < <(find "$configuration_root/$directory" -type f -name '*.yaml' -print0)
}

timestamp="$(date -u +%Y%m%dT%H%M%SZ)"
main_config="$guest_config_root/configuration.yaml"
backup_root="$guest_config_root/.hvo-deploy-$timestamp"
staging_root="$guest_config_root/.hvo-candidate-$timestamp"
deployment_started=0

rollback() {
    if (( deployment_started == 0 )); then
        return
    fi

    guest_exec "cp '$backup_root/configuration.yaml' '$main_config' && rm -rf '$guest_config_root/hvo' '$guest_config_root/www/hvo' '$staging_root' && if [ -f '$backup_root/had-hvo' ]; then cp -a '$backup_root/hvo' '$guest_config_root/hvo'; fi && if [ -f '$backup_root/had-www-hvo' ]; then mkdir -p '$guest_config_root/www' && cp -a '$backup_root/www-hvo' '$guest_config_root/www/hvo'; fi && ha core restart" >/dev/null || true
    deployment_started=0
}
cancel() {
    trap - ERR INT TERM
    rollback
    exit 130
}
trap rollback ERR
trap cancel INT TERM

guest_exec "mkdir -p '$backup_root' '$staging_root' && cp '$main_config' '$backup_root/configuration.yaml' && if [ -d '$guest_config_root/hvo' ]; then cp -a '$guest_config_root/hvo' '$backup_root/hvo' && touch '$backup_root/had-hvo'; fi && if [ -d '$guest_config_root/www/hvo' ]; then cp -a '$guest_config_root/www/hvo' '$backup_root/www-hvo' && touch '$backup_root/had-www-hvo'; fi" >/dev/null
deployment_started=1
deploy_file "$lovelace_file" "$staging_root/lovelace.yaml"
deploy_file "$frontend_root/hvo-weather-wind-card.js" "$staging_root/frontend/hvo-weather-wind-card.js"
managed_directories=(dashboards packages templates sensors utility-meters automations)
for directory in "${managed_directories[@]}"; do
    deploy_directory "$directory" "$staging_root"
done
mapfile -t candidate_files < <(
    find "${managed_directories[@]/#/$configuration_root/}" -type f -name '*.yaml' -print |
        while IFS= read -r source_file; do printf '%s\n' "${source_file#"$configuration_root/"}"; done |
        sort
)
printf -v candidate_file_list "'%s' " "${candidate_files[@]}"
guest_exec "for file in $candidate_file_list; do test -s '$staging_root/'\"\$file\" || { printf 'Candidate Home Assistant file is missing or empty: %s\\n' \"\$file\" >&2; exit 1; }; done" >/dev/null
guest_exec "rm -rf '$guest_config_root/hvo' && mv '$staging_root' '$guest_config_root/hvo'" >/dev/null
guest_exec "mkdir -p '$guest_config_root/www' && rm -rf '$guest_config_root/www/hvo' && mv '$guest_config_root/hvo/frontend' '$guest_config_root/www/hvo'" >/dev/null

guest_exec "if grep -q '^lovelace: !include hvo/lovelace.yaml$' '$main_config'; then exit 0; elif grep -q '^lovelace:' '$main_config'; then printf 'configuration.yaml already has a different top-level lovelace key; merge hvo/lovelace.yaml manually.\n' >&2; exit 1; else printf '\nlovelace: !include hvo/lovelace.yaml\n' >> '$main_config'; fi" >/dev/null
guest_exec "if grep -q '^  packages: !include_dir_named hvo/packages$' '$main_config'; then exit 0; elif grep -q '^homeassistant:' '$main_config'; then printf 'configuration.yaml already has a homeassistant key without the HVO package include; merge hvo/packages manually.\n' >&2; exit 1; else printf '\nhomeassistant:\n  packages: !include_dir_named hvo/packages\n' >> '$main_config'; fi" >/dev/null
mapfile -t managed_files < <(
    find "${managed_directories[@]/#/$configuration_root/}" -type f -name '*.yaml' -print |
        while IFS= read -r source_file; do printf 'hvo/%s\n' "${source_file#"$configuration_root/"}"; done |
        sort
)
managed_files+=("hvo/lovelace.yaml")
printf -v managed_file_list "'%s' " "${managed_files[@]}"
verify_deployment="for file in $managed_file_list; do test -s '$guest_config_root/'\"\$file\" || { printf 'Managed Home Assistant file is missing or empty: %s\\n' \"\$file\" >&2; exit 1; }; done; test -s '$guest_config_root/www/hvo/hvo-weather-wind-card.js' || { printf 'Managed weather frontend asset is missing or empty.\\n' >&2; exit 1; }; grep -q '^lovelace: !include hvo/lovelace.yaml$' '$main_config' || { printf 'Managed Lovelace include is missing.\\n' >&2; exit 1; }; grep -q '^  packages: !include_dir_named hvo/packages$' '$main_config' || { printf 'Managed package include is missing.\\n' >&2; exit 1; }"
guest_exec "$verify_deployment" >/dev/null

validation="$(curl -fsS -X POST \
    -H "Authorization: Bearer ${HOME_ASSISTANT_TOKEN}" \
    -H "Content-Type: application/json" \
    "$ha_url/api/config/core/check_config")"

if [[ "$(jq -r '.result' <<<"$validation")" != "valid" ]]; then
    rollback
    printf 'Home Assistant configuration validation failed; all managed files were restored.\n' >&2
    jq . <<<"$validation" >&2
    exit 1
fi

guest_exec "ha core stop" >/dev/null
printf 'Home Assistant configuration is valid; waiting for Core to stop.\n'
stopped=0
for _ in {1..30}; do
    if ! curl -fsS \
        -H "Authorization: Bearer ${HOME_ASSISTANT_TOKEN}" \
        "$ha_url/api/" >/dev/null 2>&1; then
        stopped=1
        break
    fi
    sleep 2
done
if (( stopped == 0 )); then
    rollback
    printf 'Home Assistant did not stop within 60 seconds; managed files were restored and Core restart was requested.\n' >&2
    exit 1
fi

guest_exec "ha core start" >/dev/null
printf 'Home Assistant Core stopped; waiting for verified startup.\n'
for _ in {1..60}; do
    if curl -fsS \
        -H "Authorization: Bearer ${HOME_ASSISTANT_TOKEN}" \
        "$ha_url/api/" >/dev/null 2>&1; then
        sleep 5
        guest_exec "$verify_deployment" >/dev/null
        guest_exec "rm -rf '$backup_root'" >/dev/null
        deployment_started=0
        printf 'HVO managed configuration deployed; dashboards are at %s/hvo-kasa/overview, %s/hvo-energy/energy, %s/hvo-operations/environment, and %s/hvo-weather/overview.\n' "$ha_url" "$ha_url" "$ha_url" "$ha_url"
        exit 0
    fi
    sleep 2
done

rollback
printf 'Home Assistant did not become ready within 120 seconds; managed files were restored and Core restart was requested.\n' >&2
exit 1
