#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
compose_file="$repo_root/tests/HomeAssistant.IntegrationEnvironment/docker-compose.yml"
project_name="hvo-ha-integration-${GITHUB_RUN_ID:-local}-$$"
docker_config="$(mktemp -d)"
printf '{}\n' >"$docker_config/config.json"
export DOCKER_CONFIG="$docker_config"

cleanup() {
    exit_status=$?
    trap - EXIT
    if ! docker compose -p "$project_name" -f "$compose_file" down --volumes --remove-orphans --rmi local; then
        printf 'Failed to remove the Home Assistant integration environment.\n' >&2
        (( exit_status == 0 )) && exit_status=1
    fi
    remaining_resources="$(
        docker ps -aq --filter "label=com.docker.compose.project=$project_name"
        docker volume ls -q --filter "label=com.docker.compose.project=$project_name"
        docker network ls -q --filter "label=com.docker.compose.project=$project_name"
    )"
    if [[ -n "$remaining_resources" ]]; then
        printf 'Home Assistant integration resources remain after cleanup.\n' >&2
        (( exit_status == 0 )) && exit_status=1
    fi
    rm -rf "$docker_config"
    exit "$exit_status"
}
trap cleanup EXIT

"$repo_root/tools/validate-home-assistant-managed-config.sh"
docker compose -p "$project_name" -f "$compose_file" up -d --wait
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
for _ in $(seq 1 120); do
    if curl -fsS "$ha_url/api/onboarding" >/dev/null 2>&1; then
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

HVO_HA_TEST_URL="$ha_url" \
HVO_HA_TEST_TOKEN="$access_token" \
HVO_HA_TEST_BROKER_HOST="127.0.0.1" \
HVO_HA_TEST_BROKER_PORT="$broker_port" \
HVO_HA_TEST_COMPOSE_FILE="$compose_file" \
HVO_HA_TEST_COMPOSE_PROJECT="$project_name" \
dotnet test "$repo_root/HVO.WebSite.sln" \
    -c Debug --nologo -v minimal --filter "TestCategory=Integration&TestCategory!=HomeAssistantIntegration" \
    --logger trx --collect:"XPlat Code Coverage" \
    --results-directory "$repo_root/TestResults"

for project in \
    "$repo_root/tests/HVO.Edge.HomeAssistant.Mqtt.Tests/HVO.Edge.HomeAssistant.Mqtt.Tests.csproj" \
    "$repo_root/tests/HVO.Edge.Exporter.HomeAssistant.Tests/HVO.Edge.Exporter.HomeAssistant.Tests.csproj"; do
    HVO_HA_TEST_URL="$ha_url" \
    HVO_HA_TEST_TOKEN="$access_token" \
    HVO_HA_TEST_INGEST_URL="$ingest_url" \
    HVO_HA_TEST_BROKER_HOST="127.0.0.1" \
    HVO_HA_TEST_BROKER_PORT="$broker_port" \
    HVO_HA_TEST_COMPOSE_FILE="$compose_file" \
    HVO_HA_TEST_COMPOSE_PROJECT="$project_name" \
    dotnet test "$project" -c Debug --nologo -v minimal \
        --filter "TestCategory=HomeAssistantIntegration" \
        --logger trx --collect:"XPlat Code Coverage" \
        --results-directory "$repo_root/TestResults"

    ha_address="$(docker compose -p "$project_name" -f "$compose_file" port home-assistant 8123)"
    ha_port="${ha_address##*:}"
    ha_url="http://127.0.0.1:$ha_port"
done

ha_address="$(docker compose -p "$project_name" -f "$compose_file" port home-assistant 8123)"
ha_port="${ha_address##*:}"
ha_url="http://127.0.0.1:$ha_port"
curl -fsS -X POST "$ha_url/auth/revoke" \
    -H 'Content-Type: application/x-www-form-urlencoded' --data-urlencode "token=$refresh_token" >/dev/null
