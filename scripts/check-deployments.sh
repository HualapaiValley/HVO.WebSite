#!/usr/bin/env bash

set -euo pipefail

docker_context="${HVO_PI_DOCKER_CONTEXT:-devpi5}"
pi_host="${HVO_PI_HOST:-devPi5}"
website_base_url="${HVO_WEBSITE_PUBLIC_BASE_URL:-https://hvo-website.calmsand-72a6c5ac.westus.azurecontainerapps.io}"
observability_base_url="${HVO_OBSERVABILITY_BASE_URL:-http://192.168.1.238}"

failures=0

check_url() {
	local label="$1"
	local url="$2"
	local status
	status="$(curl -fsS -o /dev/null -w '%{http_code}' "${url}" 2>/dev/null || true)"
	if [[ "${status}" =~ ^2[0-9][0-9]$ ]]; then
		printf 'OK   %s %s\n' "${label}" "${status}"
	else
		printf 'FAIL %s %s (%s)\n' "${label}" "${status:-no-response}" "${url}"
		failures=$((failures + 1))
	fi
}

check_url 'website /health/live' "${website_base_url}/health/live"
check_url 'website /health/ready' "${website_base_url}/health/ready"
check_url 'davis /health' "http://${pi_host}:5100/health"
check_url 'jkbms /health' "http://${pi_host}:5200/health"
check_url 'solarassistant /health' "http://${pi_host}:5300/health"
check_url 'smartshunt /health' "http://${pi_host}:5400/health"
check_url 'tplink-kasa /health' "http://${pi_host}:5500/health"
check_url 'loki /ready' "${observability_base_url}:3100/ready"
check_url 'prometheus /-/ready' "${observability_base_url}:9090/-/ready"
check_url 'grafana /api/health' "${observability_base_url}:3000/api/health"

if command -v docker >/dev/null 2>&1; then
	docker --context "${docker_context}" ps --format 'table {{.Names}}\t{{.Image}}\t{{.Status}}\t{{.Ports}}'
fi

if [[ "${failures}" -gt 0 ]]; then
	printf '%s deployment check(s) failed.\n' "${failures}" >&2
	exit 1
fi

printf 'All deployment checks passed.\n'
