#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
compose_file="$repo_root/tests/HomeAssistant.IntegrationEnvironment/docker-compose.yml"
allowed_projects=(
    tests/HVO.Edge.HomeAssistant.Mqtt.Tests/HVO.Edge.HomeAssistant.Mqtt.Tests.csproj
    tests/HVO.Edge.Exporter.HomeAssistant.Tests/HVO.Edge.Exporter.HomeAssistant.Tests.csproj
    tests/HVO.Tools.HomeAssistantEntityMigration.Tests/HVO.Tools.HomeAssistantEntityMigration.Tests.csproj
)
projects=("${allowed_projects[@]}")
prebuilt=0
ha_only=0
coverage=()
configuration=Debug
results_root="$repo_root/TestResults/integration/home-assistant"
projects_seen=0
while (( $# )); do
    case "$1" in
        --ha-only) [[ "$ha_only" == 0 ]] || { printf 'Repeated --ha-only.\n' >&2; exit 2; }; ha_only=1; shift ;;
        --prebuilt) [[ "$prebuilt" == 0 ]] || { printf 'Repeated --prebuilt.\n' >&2; exit 2; }; prebuilt=1; shift ;;
        --coverage) (( ${#coverage[@]} == 0 )) || { printf 'Repeated --coverage.\n' >&2; exit 2; }; coverage=(--collect:"XPlat Code Coverage"); shift ;;
        --projects)
            [[ "$projects_seen" == 0 ]] || { printf 'Repeated --projects.\n' >&2; exit 2; }
            projects_seen=1; projects=(); shift
            while (( $# )) && [[ "$1" != --* ]]; do projects+=("$1"); shift; done
            (( ${#projects[@]} )) || { printf '--projects requires at least one HA project.\n' >&2; exit 2; }
            ;;
        --configuration|--results-directory)
            flag="$1"; shift
            (( $# )) && [[ "$1" != --* ]] || { printf '%s requires a value.\n' "$flag" >&2; exit 2; }
            if [[ "$flag" == --configuration ]]; then configuration="$1"; else results_root="$1"; fi
            shift ;;
        *) printf 'Unknown HA runner argument: %s\n' "$1" >&2; exit 2 ;;
    esac
done
[[ "$configuration" == Debug || "$configuration" == Release ]] || { printf 'Invalid configuration.\n' >&2; exit 2; }
declare -A selected_projects=()
for project in "${projects[@]}"; do
    allowed=0
    for candidate in "${allowed_projects[@]}"; do [[ "$project" != "$candidate" ]] || allowed=1; done
    (( allowed )) || { printf 'Not an allowlisted HA project: %s\n' "$project" >&2; exit 2; }
    [[ -z "${selected_projects[$project]:-}" ]] || { printf 'Repeated HA project: %s\n' "$project" >&2; exit 2; }
    selected_projects[$project]=1
done
# Keep the trusted pre-adoption no-argument call complete: simulators and HA.
# Planned CI explicitly requests --ha-only and reuses preparation with --prebuilt.
if (( prebuilt == 0 )); then
    if (( ha_only == 0 )); then
        dotnet restore "$repo_root/HVO.WebSite.sln" --locked-mode --nologo
        dotnet build "$repo_root/HVO.WebSite.sln" -c "$configuration" --no-restore --nologo
    else
        for project in "${projects[@]}"; do
            dotnet restore "$repo_root/$project" --locked-mode --nologo
            dotnet build "$repo_root/$project" -c "$configuration" --no-restore --nologo
        done
    fi
fi
if (( ha_only == 0 )); then
    simulator_results="$repo_root/TestResults/integration/simulators"
    mkdir -p "$simulator_results"
    find "$simulator_results" -type f -name '*.trx' -delete
    dotnet test "$repo_root/HVO.WebSite.sln" -c "$configuration" --no-build --no-restore --nologo -v minimal \
        --filter "TestCategory=Integration&TestCategory!=HomeAssistantIntegration&TestCategory!=SqlServerIntegration&TestCategory!=Browser&TestCategory!=Live" \
        --settings "$repo_root/integration.runsettings" --logger trx "${coverage[@]}" --results-directory "$simulator_results"
    # No-match assemblies may emit valid zero-result reports, but the complete
    # legacy invocation must contain actual passing simulator tests.
    python3 "$repo_root/tools/ci-results.py" "$simulator_results" --allow-empty-reports
fi
curl() { command curl --connect-timeout 5 --max-time 20 "$@"; }
project_name="hvo-ha-integration-${GITHUB_RUN_ID:-local}-$$"
docker_config="$(mktemp -d)"
printf '{}\n' >"$docker_config/config.json"
export DOCKER_CONFIG="$docker_config"

cleanup() {
    exit_status=$?
    trap - EXIT
    if (( exit_status != 0 )); then
        # Preserve fixture state before teardown; never print credentials/env.
        docker compose -p "$project_name" -f "$compose_file" ps --all >&2 || true
        docker compose -p "$project_name" -f "$compose_file" logs --no-color --tail 80 mosquitto >&2 || true
    fi
    if ! docker compose -p "$project_name" -f "$compose_file" down --volumes --remove-orphans --rmi local; then
        printf 'Failed to remove the Home Assistant integration environment.\n' >&2
        if (( exit_status == 0 )); then exit_status=1; fi
    fi
    remaining_resources="$(
        docker ps -aq --filter "label=com.docker.compose.project=$project_name"
        docker volume ls -q --filter "label=com.docker.compose.project=$project_name"
        docker network ls -q --filter "label=com.docker.compose.project=$project_name"
    )"
    if [[ -n "$remaining_resources" ]]; then
        printf 'Home Assistant integration resources remain after cleanup.\n' >&2
        if (( exit_status == 0 )); then exit_status=1; fi
    fi
    rm -rf "$docker_config"
    exit "$exit_status"
}
trap cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM

"$repo_root/tools/validate-home-assistant-managed-config.sh"
docker compose version
docker compose -p "$project_name" -f "$compose_file" up -d --wait --wait-timeout 240
ha_address="$(docker compose -p "$project_name" -f "$compose_file" port home-assistant 8123)"
broker_address="$(docker compose -p "$project_name" -f "$compose_file" port mosquitto 1883)"
ingest_address="$(docker compose -p "$project_name" -f "$compose_file" port fake-ingest 8080)"
ha_port="${ha_address##*:}"
broker_port="${broker_address##*:}"
ingest_port="${ingest_address##*:}"
ha_url="http://127.0.0.1:$ha_port"
ingest_url="http://127.0.0.1:$ingest_port"
client_id="$ha_url/"
docker compose -p "$project_name" -f "$compose_file" exec -T home-assistant \
    python -m homeassistant --script check_config --config /config

ready=0
ready_deadline=$((SECONDS + 240))
while (( SECONDS < ready_deadline )); do
    if curl --max-time 2 -fsS "$ha_url/api/onboarding" >/dev/null 2>&1; then
        ready=1
        break
    fi
    sleep 2
done
if (( ready == 0 )); then
    printf 'Home Assistant did not become ready.\n' >&2
    docker compose -p "$project_name" -f "$compose_file" logs >&2
    exit 1
fi

owner_password="$(openssl rand -hex 24)"
user_response="$(curl -fsS -X POST "$ha_url/api/onboarding/users" \
    -H 'Content-Type: application/json' \
    --data "{\"name\":\"HVO Test Owner\",\"username\":\"hvo-test\",\"password\":\"$owner_password\",\"client_id\":\"$client_id\",\"language\":\"en\"}")"
auth_code="$(jq -er '.auth_code' <<<"$user_response")"
token_response="$(curl -fsS -X POST "$ha_url/auth/token" \
    -H 'Content-Type: application/x-www-form-urlencoded' \
    --data-urlencode 'grant_type=authorization_code' \
    --data-urlencode "code=$auth_code" \
    --data-urlencode "client_id=$client_id")"
access_token="$(jq -er '.access_token' <<<"$token_response")"
refresh_token="$(jq -er '.refresh_token' <<<"$token_response")"

curl -fsS -X POST "$ha_url/api/onboarding/core_config" \
    -H "Authorization: Bearer $access_token" -H 'Content-Type: application/json' --data '{}' >/dev/null
curl -fsS -X POST "$ha_url/api/onboarding/analytics" \
    -H "Authorization: Bearer $access_token" -H 'Content-Type: application/json' --data '{}' >/dev/null
flow="$(curl -fsS -X POST "$ha_url/api/config/config_entries/flow" \
    -H "Authorization: Bearer $access_token" -H 'Content-Type: application/json' \
    --data '{"handler":"mqtt"}')"
flow_id="$(jq -er '.flow_id' <<<"$flow")"
flow_result="$(curl -fsS -X POST "$ha_url/api/config/config_entries/flow/$flow_id" \
    -H "Authorization: Bearer $access_token" -H 'Content-Type: application/json' \
    --data '{"broker":"mosquitto","port":1883,"protocol":"5","other_settings":{"transport":"tcp","set_client_cert":false,"set_ca_cert":"off"}}')"
if [[ "$(jq -r '.type' <<<"$flow_result")" != "create_entry" ]]; then
    jq . <<<"$flow_result" >&2
    exit 1
fi

curl -fsS "$ha_url/api/config/config_entries/entry?domain=mqtt" \
    -H "Authorization: Bearer $access_token" | jq -e 'any(.[]; .domain == "mqtt" and .state == "loaded")' >/dev/null
set_ingest_state() {
    curl -fsS -X POST "$ingest_url/__test/central-ingest/$1" >/dev/null
}
ingest_status() {
    curl -sS -o /dev/null -w '%{http_code}' -X POST \
        "$ingest_url/api/telemetry" -H 'Content-Type: application/json' --data '{"test":true}'
}
[[ "$(ingest_status)" == "201" ]]
set_ingest_state "unavailable"
[[ "$(ingest_status)" == "503" ]]
set_ingest_state "available"
[[ "$(ingest_status)" == "201" ]]

for project in "${projects[@]}"; do
    project_results="$results_root/$(basename "$project" .csproj)"
    # Reject stale reports as evidence for a selected invocation.
    mkdir -p "$project_results"
    find "$project_results" -type f -name '*.trx' -delete
    HVO_HA_TEST_URL="$ha_url" \
    HVO_HA_TEST_TOKEN="$access_token" \
    HVO_HA_TEST_INGEST_URL="$ingest_url" \
    HVO_HA_TEST_BROKER_HOST="127.0.0.1" \
    HVO_HA_TEST_BROKER_PORT="$broker_port" \
    HVO_HA_TEST_COMPOSE_FILE="$compose_file" \
    HVO_HA_TEST_COMPOSE_PROJECT="$project_name" \
    dotnet test "$repo_root/$project" -c "$configuration" --no-build --no-restore --nologo -v minimal \
        --filter "TestCategory=HomeAssistantIntegration&TestCategory!=Live" \
        --settings "$repo_root/integration.runsettings" \
        --logger trx "${coverage[@]}" \
        --results-directory "$project_results"
    python3 "$repo_root/tools/ci-results.py" "$project_results"

    ha_address="$(docker compose -p "$project_name" -f "$compose_file" port home-assistant 8123)"
    ha_port="${ha_address##*:}"
    ha_url="http://127.0.0.1:$ha_port"
done

ha_address="$(docker compose -p "$project_name" -f "$compose_file" port home-assistant 8123)"
ha_port="${ha_address##*:}"
ha_url="http://127.0.0.1:$ha_port"
curl -fsS -X POST "$ha_url/auth/revoke" \
    -H 'Content-Type: application/x-www-form-urlencoded' --data-urlencode "token=$refresh_token" >/dev/null
