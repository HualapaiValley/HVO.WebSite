#!/usr/bin/env bash

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
run_id="hvo-log-recovery-${RANDOM}-$$"
network="${run_id}"
loki_container="${run_id}-loki"
collector_container="${run_id}-collector"
loki_volume="${run_id}-loki"
collector_volume="${run_id}-collector"

cleanup() {
	docker rm -f "${collector_container}" "${loki_container}" >/dev/null 2>&1 || true
	docker network rm "${network}" >/dev/null 2>&1 || true
	docker volume rm "${collector_volume}" "${loki_volume}" >/dev/null 2>&1 || true
}
trap cleanup EXIT

wait_for_url() {
	local url="$1"
	local attempts="${2:-60}"
	for ((attempt = 1; attempt <= attempts; attempt++)); do
		if curl --fail --silent --show-error "${url}" >/dev/null 2>&1; then
			return 0
		fi
		sleep 1
	done
	printf 'Timed out waiting for %s\n' "${url}" >&2
	return 1
}

command -v docker >/dev/null 2>&1 || { printf 'docker is required\n' >&2; exit 1; }
command -v curl >/dev/null 2>&1 || { printf 'curl is required\n' >&2; exit 1; }
command -v jq >/dev/null 2>&1 || { printf 'jq is required\n' >&2; exit 1; }

docker network create "${network}" >/dev/null
docker volume create "${loki_volume}" >/dev/null
docker volume create "${collector_volume}" >/dev/null
docker run --rm -v "${loki_volume}:/data" alpine:3.22 chown 10001:10001 /data
docker run --rm -v "${collector_volume}:/data" alpine:3.22 chown 10001:10001 /data

docker create \
	--name "${loki_container}" \
	--network "${network}" \
	--network-alias loki \
	-p 127.0.0.1::3100 \
	-v "${loki_volume}:/loki" \
	grafana/loki:3.7.2 \
	-config.file=/etc/loki/loki-config.yaml >/dev/null
docker cp "${repo_root}/deploy/hvo-docker/observability/loki/loki-config.yaml" \
	"${loki_container}:/etc/loki/loki-config.yaml"

docker create \
	--name "${collector_container}" \
	--network "${network}" \
	-p 127.0.0.1::4318 \
	-p 127.0.0.1::8888 \
	-p 127.0.0.1::13133 \
	-v "${collector_volume}:/var/lib/otelcol/file_storage" \
	otel/opentelemetry-collector-contrib:0.152.0 \
	--config=/config.yaml >/dev/null
docker cp "${repo_root}/deploy/hvo-docker/observability/config/otelcol-config.yaml" \
	"${collector_container}:/config.yaml"

docker start "${loki_container}" "${collector_container}" >/dev/null
loki_binding="$(docker port "${loki_container}" 3100/tcp)"
collector_http_binding="$(docker port "${collector_container}" 4318/tcp)"
collector_health_binding="$(docker port "${collector_container}" 13133/tcp)"
collector_metrics_binding="$(docker port "${collector_container}" 8888/tcp)"
loki_port="${loki_binding##*:}"
collector_http_port="${collector_http_binding##*:}"
collector_health_port="${collector_health_binding##*:}"
collector_metrics_port="${collector_metrics_binding##*:}"
wait_for_url "http://127.0.0.1:${loki_port}/ready"
wait_for_url "http://127.0.0.1:${collector_health_port}/"

docker stop "${loki_container}" >/dev/null
first_timestamp="$(date +%s%N)"
second_timestamp="$((first_timestamp + 1))"
marker="${run_id}"
payload="{\"resourceLogs\":[{\"resource\":{\"attributes\":[{\"key\":\"service.name\",\"value\":{\"stringValue\":\"hvo-recovery-test\"}}]},\"scopeLogs\":[{\"logRecords\":[{\"timeUnixNano\":\"${first_timestamp}\",\"severityText\":\"INFO\",\"body\":{\"stringValue\":\"${marker}-first\"}},{\"timeUnixNano\":\"${second_timestamp}\",\"severityText\":\"INFO\",\"body\":{\"stringValue\":\"${marker}-second\"}}]}]}]}"

if ! curl --fail --silent --show-error \
	-H 'Content-Type: application/json' \
	--data "${payload}" \
	"http://127.0.0.1:${collector_http_port}/v1/logs" >/dev/null; then
	docker logs "${collector_container}" >&2
	exit 1
fi

# Restarting while Loki is down proves the queue is disk-backed rather than process memory.
sleep 3
metrics="$(curl --fail --silent --show-error "http://127.0.0.1:${collector_metrics_port}/metrics")"
capacity_line="$(grep -E 'otelcol_exporter_queue_capacity.*exporter="otlp_http/loki"' <<<"${metrics}" || true)"
queue_size_line="$(grep -E 'otelcol_exporter_queue_size.*exporter="otlp_http/loki"' <<<"${metrics}" || true)"
[[ -n "${capacity_line}" && -n "${queue_size_line}" ]] || {
	printf 'Collector did not expose the expected log queue metrics\n' >&2
	exit 1
}
capacity="${capacity_line##* }"
queue_size="${queue_size_line##* }"
jq -en --arg capacity "${capacity}" --arg size "${queue_size}" \
	'($capacity | tonumber) == 67108864 and ($size | tonumber) > 0 and ($size | tonumber) <= ($capacity | tonumber)' >/dev/null || {
	printf 'Collector queue was not bounded at 64 MiB with buffered records: size=%s capacity=%s\n' "${queue_size}" "${capacity}" >&2
	exit 1
}
docker restart "${collector_container}" >/dev/null
collector_health_binding="$(docker port "${collector_container}" 13133/tcp)"
collector_health_port="${collector_health_binding##*:}"
if ! wait_for_url "http://127.0.0.1:${collector_health_port}/"; then
	docker logs "${collector_container}" >&2
	exit 1
fi
docker start "${loki_container}" >/dev/null
loki_binding="$(docker port "${loki_container}" 3100/tcp)"
loki_port="${loki_binding##*:}"
wait_for_url "http://127.0.0.1:${loki_port}/ready"

delivered=false
for ((attempt = 1; attempt <= 90; attempt++)); do
	response="$(curl --fail --silent --show-error --get \
		--data-urlencode 'query={service_name="hvo-recovery-test"}' \
		--data-urlencode 'direction=forward' \
		--data-urlencode 'limit=10' \
		"http://127.0.0.1:${loki_port}/loki/api/v1/query_range")"
	values="$(jq -c --arg marker "${marker}" '[.data.result[].values[] | select(.[1] | contains($marker))]' <<<"${response}")"
	if [[ "$(jq 'length' <<<"${values}")" -eq 2 ]]; then
		jq -e --arg first "${first_timestamp}" --arg second "${second_timestamp}" \
			'.[0][0] == $first and .[1][0] == $second' <<<"${values}" >/dev/null
		delivered=true
		break
	fi
	sleep 1
done

[[ "${delivered}" == true ]] || { printf 'Buffered log records did not reach Loki\n' >&2; exit 1; }
printf 'Verified persistent outage recovery, timestamps, and ordering for two log records.\n'
