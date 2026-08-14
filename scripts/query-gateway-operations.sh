#!/usr/bin/env bash

set -euo pipefail

hours="${HVO_GATEWAY_QUERY_HOURS:-2}"
workspace_id="${LOG_ANALYTICS_WORKSPACE_ID:-}"
output="table"

usage() {
	printf 'Usage: %s --workspace <id> [--hours <n>] [--output table|json]\n' "$(basename "$0")"
	printf '\n'
	printf 'Runs standard Log Analytics queries for gateway ingest, health, outbox, and errors.\n'
}

fail() {
	printf 'Error: %s\n' "$*" >&2
	exit 1
}

run_query() {
	local title="$1"
	local query="$2"
	printf '\n## %s\n' "${title}"
	az monitor log-analytics query \
		-w "${workspace_id}" \
		--analytics-query "${query}" \
		--output "${output}"
}

while (($# > 0)); do
	case "$1" in
		--workspace)
			[[ $# -ge 2 ]] || fail '--workspace requires a value.'
			workspace_id="$2"
			shift 2
			;;
		--hours)
			[[ $# -ge 2 ]] || fail '--hours requires a value.'
			hours="$2"
			shift 2
			;;
		--output)
			[[ $# -ge 2 ]] || fail '--output requires a value.'
			output="$2"
			shift 2
			;;
		-h|--help)
			usage
			exit 0
			;;
		*)
			fail "Unknown argument '$1'."
			;;
	esac
done

[[ -n "${workspace_id}" ]] || fail 'Set LOG_ANALYTICS_WORKSPACE_ID or pass --workspace.'
command -v az >/dev/null 2>&1 || fail 'Required command not found: az'

run_query 'Gateway ingest requests' "AppRequests | where TimeGenerated > ago(${hours}h) | where Name has_any ('weather', 'bms', 'power', 'gateway-status', 'device-inventory', 'inverter-detail') | summarize Count=count(), Failed=countif(Success == false) by Name, ResultCode | order by Failed desc, Count desc"
run_query 'Gateway trace severity' "AppTraces | where TimeGenerated > ago(${hours}h) | where Message has_any ('outbox', 'gateway', 'Davis', 'EG4', 'Eg4', 'JkBms', 'SmartShunt') | summarize Count=count() by SeverityLevel | order by SeverityLevel desc"
run_query 'Outbox and SQLite errors' "AppTraces | where TimeGenerated > ago(${hours}h) | where Message has_any ('outbox', 'Outbox', 'SQLite', 'database is locked', 'FailureKind') | summarize Count=count(), Latest=max(TimeGenerated) by SeverityLevel, Message | order by Latest desc"
run_query 'Gateway health/status signals' "AppTraces | where TimeGenerated > ago(${hours}h) | where Message has_any ('gateway health', 'gateway-status', 'health evaluation', 'poll failed', 'poll timeout') | summarize Count=count(), Latest=max(TimeGenerated) by SeverityLevel, Message | order by Latest desc"
