#!/bin/bash
set -euo pipefail

REPO_ROOT="/workspaces/HVO.WebSite"
PORT="${OPENCODE_WEB_PORT:-4096}"
HOST="${OPENCODE_WEB_HOST:-0.0.0.0}"
LOG_FILE="/tmp/opencode-web.log"
START_RETRIES="${OPENCODE_START_RETRIES:-2}"

export PATH="$HOME/.opencode/bin:$HOME/.local/bin:$PATH"

log() {
	printf '%s %s\n' "$(date -Iseconds)" "$*" >> "$LOG_FILE"
}

ensure_opencode_state_dirs() {
	local local_dir="$HOME/.local"
	local state_dir="$local_dir/state"
	local share_dir="$local_dir/share"
	local opencode_share_dir="$share_dir/opencode"

	mkdir -p "$local_dir" "$share_dir" 2>/dev/null || sudo mkdir -p "$local_dir" "$share_dir"
	for dir in "$state_dir" "$opencode_share_dir"; do
		mkdir -p "$dir" 2>/dev/null || sudo mkdir -p "$dir"
		if [ ! -w "$dir" ] || [ ! -O "$dir" ]; then
			sudo chown -R "$(id -u)":"$(id -g)" "$dir"
		fi
	done
}

ensure_xdg_open() {
	if command -v xdg-open >/dev/null 2>&1; then
		return 0
	fi

	mkdir -p "$HOME/.local/bin"
	printf '%s\n' '#!/bin/sh' 'if [ -n "${BROWSER:-}" ]; then' '	"$BROWSER" "$@" >/dev/null 2>&1 &' 'fi' 'exit 0' > "$HOME/.local/bin/xdg-open"
	chmod +x "$HOME/.local/bin/xdg-open"
}

wait_for_listener() {
	local attempts=10
	local delay_seconds=1
	local attempt

	for attempt in $(seq 1 "$attempts"); do
		if ss -ltn "sport = :${PORT}" | tail -n +2 | grep -q .; then
			return 0
		fi
		sleep "$delay_seconds"
	done

	return 1
}

http_status_code() {
	curl -sS -o /dev/null -w '%{http_code}' "http://127.0.0.1:${PORT}" 2>/dev/null || true
}

wait_for_http_ready() {
	local attempts=10
	local delay_seconds=1
	local attempt
	local status_code

	for attempt in $(seq 1 "$attempts"); do
		status_code="$(http_status_code)"
		if [[ -n "$status_code" && "$status_code" != "000" ]]; then
			echo "$status_code"
			return 0
		fi
		sleep "$delay_seconds"
	done

	return 1
}

current_opencode_pid() {
	pgrep -u "$(id -u)" -f "opencode web.*--port ${PORT}" | tail -n 1 || true
}

stop_existing_opencode() {
	local pid
	local attempt

	pid="$(current_opencode_pid)"
	if [[ -z "$pid" ]]; then
		return 0
	fi

	log "Stopping stale opencode web process pid=${pid}."
	kill "$pid" 2>/dev/null || true

	for attempt in $(seq 1 5); do
		if ! kill -0 "$pid" 2>/dev/null; then
			return 0
		fi
		sleep 1
	done

	log "Force killing stale opencode web process pid=${pid}."
	kill -9 "$pid" 2>/dev/null || true
}

if ! command -v opencode >/dev/null 2>&1; then
	log "opencode is not installed; skipping opencode web startup."
	exit 0
fi

ensure_opencode_state_dirs
ensure_xdg_open

cd "$REPO_ROOT"
if [ -z "${OPENCODE_SERVER_PASSWORD:-}" ]; then
	log "OPENCODE_SERVER_PASSWORD is not set; starting opencode web without authentication."
fi

existing_pid="$(current_opencode_pid)"
if [[ -n "$existing_pid" ]]; then
	http_status="$(wait_for_http_ready || true)"
	if [[ -n "$http_status" ]]; then
		log "opencode web already healthy on ${HOST}:${PORT} (pid ${existing_pid}, http ${http_status})."
		exit 0
	fi

	log "opencode web process pid=${existing_pid} exists but is not responding on ${HOST}:${PORT}."
	stop_existing_opencode
fi

attempt=1
while [[ "$attempt" -le "$START_RETRIES" ]]; do
	log "Starting opencode web on ${HOST}:${PORT} (attempt ${attempt}/${START_RETRIES}, version $(opencode --version 2>/dev/null || echo unknown))."
	setsid -f opencode web --hostname "$HOST" --port "$PORT" </dev/null >> "$LOG_FILE" 2>&1

	if wait_for_listener; then
		http_status="$(wait_for_http_ready || true)"
		if [[ -n "$http_status" ]]; then
			started_pid="$(current_opencode_pid)"
			log "opencode web is healthy on ${HOST}:${PORT} (pid ${started_pid:-unknown}, http ${http_status})."
			exit 0
		fi
	fi

	log "opencode web startup attempt ${attempt}/${START_RETRIES} did not reach a healthy HTTP state on ${HOST}:${PORT}."
	stop_existing_opencode
	attempt=$((attempt + 1))
done

log "opencode web failed after ${START_RETRIES} attempts; inspect this log for startup output."
exit 1
