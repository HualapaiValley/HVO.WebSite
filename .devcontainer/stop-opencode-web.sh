#!/bin/bash
set -euo pipefail

REPO_ROOT="/workspaces/HVO.WebSite"
PORT="${OPENCODE_WEB_PORT:-4096}"
LOG_FILE="/tmp/opencode-web.log"

log() {
	printf '%s %s\n' "$(date -Iseconds)" "$*" >> "$LOG_FILE"
}

current_opencode_pid() {
	pgrep -u "$(id -u)" -f "opencode web.*--port ${PORT}" | tail -n 1 || true
}

stop_all_opencode() {
	local pids
	local pid
	local attempt
	local any_stopped=false

	pids=($(pgrep -u "$(id -u)" -f "opencode web.*--port ${PORT}" 2>/dev/null || true))

	if [[ ${#pids[@]} -eq 0 ]]; then
		echo "No opencode web process found on port ${PORT}."
		return 0
	fi

	for pid in "${pids[@]}"; do
		any_stopped=true
		echo "Stopping opencode web (pid ${pid})..."
		log "Stopping opencode web process pid=${pid}."
		kill "$pid" 2>/dev/null || true

		for attempt in $(seq 1 5); do
			if ! kill -0 "$pid" 2>/dev/null; then
				echo "opencode web stopped (pid ${pid})."
				log "opencode web process pid=${pid} stopped cleanly."
				break
			fi
			sleep 1
		done

		if kill -0 "$pid" 2>/dev/null; then
			echo "Force killing opencode web (pid ${pid})..."
			log "Force killing opencode web process pid=${pid}."
			kill -9 "$pid" 2>/dev/null || true
			echo "opencode web force killed (pid ${pid})."
			log "opencode web process pid=${pid} force killed."
		fi
	done
}

stop_all_opencode
