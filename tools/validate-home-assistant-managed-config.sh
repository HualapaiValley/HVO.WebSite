#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source_root="$repo_root/deploy/home-assistant/configuration"
test_root="$repo_root/tests/HomeAssistant.IntegrationEnvironment/home-assistant"
dockerfile="$repo_root/tests/HomeAssistant.IntegrationEnvironment/home-assistant.Dockerfile"
validation_root="$(mktemp -d)"
container_name="hvo-ha-config-validation-$$"

cleanup() {
    docker rm -f "$container_name" >/dev/null 2>&1 || true
    rm -rf "$validation_root"
}
trap cleanup EXIT

mkdir -p "$validation_root/hvo"
cp -R "$source_root/lovelace.yaml" "$source_root/dashboards" "$source_root/packages" \
    "$source_root/templates" "$source_root/automations" "$validation_root/hvo/"
cp "$test_root/managed-configuration.yaml" "$validation_root/configuration.yaml"

read -r from image <"$dockerfile"
if [[ "$from" != "FROM" || -z "$image" ]]; then
    printf 'Unable to read the pinned Home Assistant image from %s.\n' "$dockerfile" >&2
    exit 1
fi

docker run --rm -d --name "$container_name" -e TZ=UTC \
    -v "$validation_root:/config" -v /data "$image" >/dev/null

valid=0
for _ in $(seq 1 60); do
    if [[ "$(docker inspect -f '{{.State.Running}}' "$container_name" 2>/dev/null || true)" != "true" ]]; then
        break
    fi
    if docker exec "$container_name" python -m homeassistant --script check_config --config /config >/dev/null 2>&1; then
        valid=1
        break
    fi
    sleep 1
done

if (( valid == 0 )); then
    docker logs "$container_name" >&2 || true
    printf 'Home Assistant managed configuration is invalid.\n' >&2
    exit 1
fi

printf 'Home Assistant managed configuration is valid.\n'
