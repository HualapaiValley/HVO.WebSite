#!/usr/bin/env bash

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

fail() {
	printf 'Observability policy validation failed: %s\n' "$*" >&2
	exit 1
}

command -v docker >/dev/null 2>&1 || fail 'docker is required'
command -v jq >/dev/null 2>&1 || fail 'jq is required'
command -v rg >/dev/null 2>&1 || fail 'rg is required'

# Compose interpolation needs non-secret placeholders; no service is started.
export AZURE_KEYVAULT_URI="https://example.test/"
export AZURE_TENANT_ID="test"
export AZURE_CLIENT_ID="test"
export AZURE_CLIENT_SECRET="test"
export DAVIS_API_KEY="test"
export BMS_API_KEY="test"
export POWER_API_KEY="test"
export ConnectionStrings__HualapaiValleyObservatory="Server=example"
export OTEL_COLLECTOR_ENDPOINT="http://collector:4318"
export HVO_WEBSITE_PUBLIC_BASE_URL="http://website"
export DAVIS_STATION_HOST="example"
export DAVIS_STATION_PORT="22222"
export GRAFANA_ADMIN_USER="admin"
export GRAFANA_ADMIN_PASSWORD="test"
export HVO_OBSERVABILITY_DATA_ROOT="/var/lib/docker/hvo-observability"
export MSSQL_SA_PASSWORD="test"
export REDIS_PASSWORD="test"
export MINIO_ROOT_USER="test"
export MINIO_ROOT_PASSWORD="test"
export RABBITMQ_DEFAULT_USER="test"
export RABBITMQ_DEFAULT_PASS="test"
export RABBITMQ_ERLANG_COOKIE="test"

app_compose_files=(
	"docker-compose.yml"
	"deploy/hvo-docker/docker-compose.yml"
	"deploy/pi-gateways/davis/docker-compose.yml"
	"deploy/pi-gateways/jkbms/docker-compose.yml"
	"deploy/pi-gateways/solarassistant/docker-compose.yml"
	"deploy/pi-gateways/smartshunt/docker-compose.yml"
	"deploy/pi-gateways/tplink-kasa/docker-compose.yml"
)

for relative_file in "${app_compose_files[@]}"; do
	compose_file="${repo_root}/${relative_file}"
	config="$(docker compose -f "${compose_file}" config --format json 2>/dev/null)"
	jq -e '
		all(.services[];
			.logging.driver == "local" and
			.logging.options["max-size"] == "10m" and
			.logging.options["max-file"] == "3" and
			.logging.options.compress == "true" and
			.logging.options.mode == "non-blocking" and
			.logging.options["max-buffer-size"] == "4m" and
			.ulimits.core != null and
			([.volumes[]? | select(.target == "/app/logs")] | length) == 0)
		' <<<"${config}" >/dev/null || fail "${relative_file} does not resolve to the bounded local-log/core policy"
done

for relative_file in \
	"deploy/hvo-docker/observability/compose.yaml" \
	"deploy/hvo-docker/shared-infrastructure/compose.yaml"; do
	config="$(docker compose -f "${repo_root}/${relative_file}" config --format json 2>/dev/null)"
	jq -e 'all(.services[];
		.logging.driver == "local" and
		.logging.options["max-size"] == "10m" and
		.logging.options["max-file"] == "3" and
		.logging.options.mode == "non-blocking" and
		.logging.options["max-buffer-size"] == "4m")' <<<"${config}" >/dev/null ||
		fail "${relative_file} contains an unbounded service log"
done

observability="$(docker compose -f "${repo_root}/deploy/hvo-docker/observability/compose.yaml" config --format json 2>/dev/null)"
jq -e 'all(.services[].image; endswith(":latest") | not)' <<<"${observability}" >/dev/null ||
	fail 'observability images must be version-pinned'
jq -e '
	.services.loki.volumes | any(.target == "/loki" and .type == "bind" and .source == "/var/lib/docker/hvo-observability/loki" and .bind.create_host_path == false)
	' <<<"${observability}" >/dev/null || fail 'Loki storage is not a fail-closed tank-backed bind mount'
jq -e '
	.services["otel-collector"].volumes | any(.target == "/var/lib/otelcol/file_storage" and .type == "bind" and .source == "/var/lib/docker/hvo-observability/otelcol" and .bind.create_host_path == false)
	' <<<"${observability}" >/dev/null || fail 'collector storage is not a fail-closed tank-backed bind mount'

if rg -q 'WriteTo\.File|CompactJsonFormatter|Path\.Combine\([^)]*,[[:space:]]*"logs"\)' "${repo_root}/src/HVO.WebSite.v9"; then
	fail 'website application-owned file logging is still enabled'
fi

rg -q 'queue_size:[[:space:]]+67108864' "${repo_root}/deploy/hvo-docker/observability/config/otelcol-config.yaml" ||
	fail 'collector log queue is not explicitly capped at 64 MiB'
rg -q 'retention_period:[[:space:]]+720h' "${repo_root}/deploy/hvo-docker/observability/loki/loki-config.yaml" ||
	fail 'Loki central retention is not explicitly set to 30 days'
rg -q 'HvoLogRecordsDropped' "${repo_root}/deploy/hvo-docker/observability/prometheus/alerts.yml" ||
	fail 'required dropped-log alert is missing'

printf 'Observability policy validation passed for all checked-in services.\n'
