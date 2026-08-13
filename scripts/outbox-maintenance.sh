#!/usr/bin/env bash

set -euo pipefail

context="${HVO_PI_DOCKER_CONTEXT:-devpi5}"
target=""
action="summary"
remote=false

usage() {
	printf 'Usage: %s [--context <docker-context>] [--remote] <summary|schema|archive|compact> <davis|eg4|ha-exporter|jkbms|solarassistant|smartshunt|tplinkkasa>\n' "$(basename "$0")"
	printf '\n'
	printf 'Inspects, archives, or compacts gateway outbox.db files. Compact requires a stopped gateway and creates a verified local backup first.\n'
}

fail() {
	printf 'Error: %s\n' "$*" >&2
	exit 1
}

volume_for_target() {
	case "$1" in
		davis) printf '%s\n' 'davis_davis-outbox' ;;
		eg4) printf '%s\n' 'eg4_eg4-outbox' ;;
		ha-exporter|home-assistant-exporter) printf '%s\n' 'home-assistant-exporter_home-assistant-exporter-data' ;;
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
		eg4) printf '%s\n' 'hvo-eg4' ;;
		ha-exporter|home-assistant-exporter) printf '%s\n' 'hvo-home-assistant-exporter' ;;
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
	run_volume_sqlite "${volume}" "SELECT page_count AS PageCount, freelist_count AS FreePages, ROUND(100.0 * freelist_count / page_count, 1) AS FreePercent FROM pragma_page_count(), pragma_freelist_count();"
	run_volume_sqlite "${volume}" "PRAGMA auto_vacuum;"
}

require_stopped() {
	local service="$1"
	local running
	running="$(run_docker ps --filter "name=${service}" --format '{{.Names}}' 2>/dev/null || true)"
	if [[ -n "${running}" ]]; then
		fail "Gateway service '${service}' appears to be running (${running//$'\n'/, }). Stop it before modifying live SQLite files."
	fi
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
	require_stopped "${service}"

	stamp="$(date -u +%Y%m%dT%H%M%SZ)"
	printf 'Archiving /app/data/outbox.db* in volume %s with suffix .archived-%s\n' "${volume}" "${stamp}"
	run_docker run --rm -v "${volume}:/data" alpine:3.20 sh -c "set -e; for file in /data/outbox.db*; do [ -e \"\$file\" ] || continue; mv \"\$file\" \"\$file.archived-${stamp}\"; done; ls -la /data"
}

compact() {
	local volume="$1"
	local service="$2"
	local stamp backup_dir backup_path
	require_stopped "${service}"
	stamp="$(date -u +%Y%m%dT%H%M%SZ)"
	backup_dir="artifacts/outbox"
	backup_path="${backup_dir}/${target}-${stamp}.tar.gz"
	mkdir -p "${backup_dir}"

	printf 'Creating local outbox backup %s\n' "${backup_path}"
	run_docker run --rm -v "${volume}:/data:ro" alpine:3.20 sh -c \
		'apk add --no-cache sqlite >/dev/null && test "$(sqlite3 -readonly /data/outbox.db "PRAGMA integrity_check")" = ok && sqlite3 -readonly /data/outbox.db ".backup /tmp/outbox.db" && tar -czf - -C /tmp outbox.db' > "${backup_path}"
	[[ -s "${backup_path}" ]] || fail "Outbox backup is empty: ${backup_path}"
	tar -tzf "${backup_path}" | grep -qx 'outbox.db' || fail "Outbox backup is not readable: ${backup_path}"

	printf 'Compacting outbox in volume %s\n' "${volume}"
	run_docker run --rm -v "${volume}:/data" alpine:3.20 sh -c \
		'apk add --no-cache sqlite >/dev/null && test "$(sqlite3 /data/outbox.db "PRAGMA integrity_check")" = ok && sqlite3 /data/outbox.db "PRAGMA journal_mode=DELETE; PRAGMA auto_vacuum=INCREMENTAL; VACUUM; PRAGMA journal_mode=WAL;" && test "$(sqlite3 /data/outbox.db "PRAGMA integrity_check")" = ok'
	printf 'Compaction complete; verified backup retained at %s\n' "${backup_path}"
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
		summary|schema|archive|compact)
			action="$1"
			shift
			;;
		davis|eg4|ha-exporter|home-assistant-exporter|jkbms|solarassistant|smartshunt|tplinkkasa|tplink-kasa)
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
	compact) compact "${volume}" "${service}" ;;
esac
