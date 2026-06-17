#!/usr/bin/env bash

set -euo pipefail

context="${HVO_PI_DOCKER_CONTEXT:-devpi5}"
target=""
action="summary"
remote=false

usage() {
	printf 'Usage: %s [--context <docker-context>] [--remote] <summary|schema|archive> <davis|jkbms|solarassistant|smartshunt|tplinkkasa>\n' "$(basename "$0")"
	printf '\n'
	printf 'Inspects or archives gateway outbox.db files. Archive renames outbox.db* inside the Docker volume; it never deletes files.\n'
}

fail() {
	printf 'Error: %s\n' "$*" >&2
	exit 1
}

volume_for_target() {
	case "$1" in
		davis) printf '%s\n' 'davis_davis-outbox' ;;
		jkbms) printf '%s\n' 'jkbms_jkbms-outbox' ;;
		solarassistant) printf '%s\n' 'solarassistant_solarassistant-outbox' ;;
		smartshunt) printf '%s\n' 'smartshunt_smartshunt-outbox' ;;
		tplinkkasa|tplink-kasa) printf '%s\n' 'tplink-kasa_kasa-data' ;;
		*) fail "Unknown gateway target '$1'." ;;
	esac
}

service_for_target() {
	case "$1" in
		davis) printf '%s\n' 'hvo-davis' ;;
		jkbms) printf '%s\n' 'hvo-jkbms' ;;
		solarassistant) printf '%s\n' 'hvo-solarassistant' ;;
		smartshunt) printf '%s\n' 'hvo-smartshunt' ;;
		tplinkkasa|tplink-kasa) printf '%s\n' 'hvo-tplink-kasa' ;;
		*) fail "Unknown gateway target '$1'." ;;
	esac
}

run_docker() {
	if [[ "${remote}" == true ]]; then
		docker --context "${context}" "$@"
	else
		docker "$@"
	fi
}

run_sqlite() {
	local db_path="$1"
	local sql="$2"
	sqlite3 -readonly -header -column "${db_path}" "${sql}"
}

run_volume_sqlite() {
	local volume="$1"
	local sql="$2"
	run_docker run --rm -v "${volume}:/data:ro" alpine:3.20 sh -c "apk add --no-cache sqlite >/dev/null && sqlite3 -readonly -header -column /data/outbox.db \"${sql}\""
}

summarize() {
	local volume="$1"
	local summary_sql="SELECT Status, FailureKind, COUNT(*) AS Count, MIN(CreatedAtUtc) AS OldestCreatedUtc, MAX(CreatedAtUtc) AS NewestCreatedUtc FROM OutboxRecords GROUP BY Status, FailureKind ORDER BY Status, FailureKind;"
	local forward_sql="SELECT MAX(SentAtUtc) AS LastSentAtUtc, MAX(LastAttemptedAtUtc) AS LastAttemptedAtUtc FROM OutboxRecords;"
	printf 'Outbox summary for volume %s\n' "${volume}"
	run_volume_sqlite "${volume}" "${summary_sql}"
	run_volume_sqlite "${volume}" "${forward_sql}"
}

schema() {
	local volume="$1"
	printf 'Outbox schema for volume %s\n' "${volume}"
	run_volume_sqlite "${volume}" "PRAGMA table_info('OutboxRecords');"
}

archive() {
	local volume="$1"
	local service="$2"
	local stamp
	local running
	running="$(run_docker ps --filter "name=${service}" --format '{{.Names}}' 2>/dev/null || true)"
	if [[ -n "${running}" ]]; then
		fail "Gateway service '${service}' appears to be running (${running//$'\n'/, }). Stop it before archiving live SQLite files."
	fi

	stamp="$(date -u +%Y%m%dT%H%M%SZ)"
	printf 'Archiving /app/data/outbox.db* in volume %s with suffix .archived-%s\n' "${volume}" "${stamp}"
	run_docker run --rm -v "${volume}:/data" alpine:3.20 sh -c "set -e; for file in /data/outbox.db*; do [ -e \"\$file\" ] || continue; mv \"\$file\" \"\$file.archived-${stamp}\"; done; ls -la /data"
}

while (($# > 0)); do
	case "$1" in
		--context)
			[[ $# -ge 2 ]] || fail '--context requires a value.'
			context="$2"
			shift 2
			;;
		--remote)
			remote=true
			shift
			;;
		summary|schema|archive)
			action="$1"
			shift
			;;
		davis|jkbms|solarassistant|smartshunt|tplinkkasa|tplink-kasa)
			target="$1"
			shift
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

[[ -n "${target}" ]] || { usage; exit 1; }
command -v docker >/dev/null 2>&1 || fail 'Required command not found: docker'
volume="$(volume_for_target "${target}")"
service="$(service_for_target "${target}")"

case "${action}" in
	summary) summarize "${volume}" ;;
	schema) schema "${volume}" ;;
	archive) archive "${volume}" "${service}" ;;
esac
