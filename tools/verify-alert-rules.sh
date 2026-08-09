#!/usr/bin/env bash

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
container="hvo-alert-rules-${RANDOM}-$$"
cleanup() {
	docker rm -f "${container}" >/dev/null 2>&1 || true
}
trap cleanup EXIT

docker create \
	--name "${container}" \
	--entrypoint /bin/promtool \
	prom/prometheus:v3.11.3 \
	test rules /alerts.test.yml >/dev/null
docker cp "${repo_root}/deploy/hvo-docker/observability/prometheus/alerts.yml" "${container}:/alerts.yml"
docker cp "${repo_root}/deploy/hvo-docker/observability/prometheus/alerts.test.yml" "${container}:/alerts.test.yml"
docker start --attach "${container}"
