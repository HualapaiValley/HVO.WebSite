#!/usr/bin/env bash

set -euo pipefail

container="hvo-log-budget-${RANDOM}-$$"
cleanup() {
	docker rm -f "${container}" >/dev/null 2>&1 || true
}
trap cleanup EXIT

docker create \
	--name "${container}" \
	--log-driver local \
	--log-opt max-size=10m \
	--log-opt max-file=3 \
	--log-opt compress=true \
	--log-opt mode=non-blocking \
	--log-opt max-buffer-size=4m \
	--ulimit core=0:0 \
	alpine:3.22 \
	sh -c 'yes 0123456789abcdef | head -c 100000000; printf "\ncore-limit=%s\n" "$(ulimit -c)"' >/dev/null

docker start "${container}" >/dev/null
exit_code="$(docker wait "${container}")"
[[ "${exit_code}" == 0 ]] || { printf 'Log storm container exited with %s\n' "${exit_code}" >&2; exit 1; }

retained_bytes="$(docker logs "${container}" 2>/dev/null | wc -c)"
[[ "${retained_bytes}" -gt 0 ]] || { printf 'No local logs were retained\n' >&2; exit 1; }
# Allow one active-file margin for framing around the nominal 30 MiB retention.
[[ "${retained_bytes}" -le 41943040 ]] || {
	printf 'Local log budget exceeded: %s bytes retained\n' "${retained_bytes}" >&2
	exit 1
}
core_limit="$(docker inspect "${container}" --format '{{range .HostConfig.Ulimits}}{{if eq .Name "core"}}{{.Soft}}:{{.Hard}}{{end}}{{end}}')"
[[ "${core_limit}" == "0:0" ]] || {
	printf 'Container core limit was not zero\n' >&2
	exit 1
}

printf 'Verified a 100 MB failure storm retained %s bytes within the 40 MiB test ceiling.\n' "${retained_bytes}"
