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
dashboard_file="$repo_root/deploy/home-assistant/configuration/dashboards/hvo-kasa.yaml"
lovelace_file="$repo_root/deploy/home-assistant/configuration/lovelace.yaml"
configuration_root="$repo_root/deploy/home-assistant/configuration"
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

mapfile -t referenced_entities < <(
    rg --no-filename --only-matching 'entity: [a-z0-9_]+\.[a-z0-9_]+' "$dashboard_file" |
        cut -d' ' -f2 |
        sort -u
)

states="$(curl -fsS \
    -H "Authorization: Bearer ${HOME_ASSISTANT_TOKEN}" \
    -H "Content-Type: application/json" \
    "$ha_url/api/states")"

missing=0
for entity_id in "${referenced_entities[@]}"; do
    if ! jq -e --arg entity_id "$entity_id" '.[] | select(.entity_id == $entity_id)' >/dev/null <<<"$states"; then
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

printf 'Validated %d dashboard entity references.\n' "${#referenced_entities[@]}"
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
        "qm guest exec '$ha_vmid' -- /bin/sh -c $quoted_command")"

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
    target_directory="$(dirname "$target_file")"
    payload="$(base64 -w 0 "$source_file")"
    guest_exec "mkdir -p '$target_directory' && printf '%s' '$payload' | base64 -d > '$target_file'" >/dev/null
}

timestamp="$(date -u +%Y%m%dT%H%M%SZ)"
main_config="$guest_config_root/configuration.yaml"
backup_root="$guest_config_root/.hvo-deploy-$timestamp"
deployment_started=0

rollback() {
    if (( deployment_started == 0 )); then
        return
    fi

    guest_exec "cp '$backup_root/configuration.yaml' '$main_config' && rm -rf '$guest_config_root/hvo' && if [ -f '$backup_root/had-hvo' ]; then cp -a '$backup_root/hvo' '$guest_config_root/hvo'; fi" >/dev/null || true
    deployment_started=0
}
trap rollback ERR INT TERM

guest_exec "mkdir -p '$backup_root' && cp '$main_config' '$backup_root/configuration.yaml' && if [ -d '$guest_config_root/hvo' ]; then cp -a '$guest_config_root/hvo' '$backup_root/hvo' && touch '$backup_root/had-hvo'; fi" >/dev/null
deployment_started=1
deploy_file "$lovelace_file" "$guest_config_root/hvo/lovelace.yaml"
deploy_file "$dashboard_file" "$guest_config_root/hvo/dashboards/hvo-kasa.yaml"
deploy_file "$configuration_root/packages/hvo.yaml" "$guest_config_root/hvo/packages/hvo.yaml"
deploy_file "$configuration_root/templates/hvo.yaml" "$guest_config_root/hvo/templates/hvo.yaml"
deploy_file "$configuration_root/automations/hvo.yaml" "$guest_config_root/hvo/automations/hvo.yaml"

guest_exec "if grep -q '^lovelace: !include hvo/lovelace.yaml$' '$main_config'; then exit 0; elif grep -q '^lovelace:' '$main_config'; then printf 'configuration.yaml already has a different top-level lovelace key; merge hvo/lovelace.yaml manually.\n' >&2; exit 1; else printf '\nlovelace: !include hvo/lovelace.yaml\n' >> '$main_config'; fi" >/dev/null
guest_exec "if grep -q '^  packages: !include_dir_named hvo/packages$' '$main_config'; then exit 0; elif grep -q '^homeassistant:' '$main_config'; then printf 'configuration.yaml already has a homeassistant key without the HVO package include; merge hvo/packages manually.\n' >&2; exit 1; else printf '\nhomeassistant:\n  packages: !include_dir_named hvo/packages\n' >> '$main_config'; fi" >/dev/null

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

set +e
restart_error="$(curl -fsS -X POST \
    -H "Authorization: Bearer ${HOME_ASSISTANT_TOKEN}" \
    -H "Content-Type: application/json" \
    "$ha_url/api/services/homeassistant/restart" 2>&1 >/dev/null)"
restart_status=$?
set -e

# Core may close the request socket as the restart begins.
if (( restart_status != 0 && restart_status != 52 )); then
    rollback
    printf '%s\n' "$restart_error" >&2
    printf 'Home Assistant restart request failed with curl status %d.\n' "$restart_status" >&2
    exit 1
fi

printf 'Home Assistant configuration is valid; waiting for Core restart.\n'
sleep 5
for _ in {1..60}; do
    if curl -fsS \
        -H "Authorization: Bearer ${HOME_ASSISTANT_TOKEN}" \
        "$ha_url/api/" >/dev/null 2>&1; then
        guest_exec "rm -rf '$backup_root'" >/dev/null
        deployment_started=0
        printf 'HVO managed configuration deployed; dashboard is at %s/hvo-kasa/overview.\n' "$ha_url"
        exit 0
    fi
    sleep 2
done

rollback
printf 'Home Assistant did not become ready within 120 seconds; managed files were restored.\n' >&2
exit 1
