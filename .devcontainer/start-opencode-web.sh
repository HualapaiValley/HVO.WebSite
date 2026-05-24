#!/bin/bash
set -euo pipefail

REPO_ROOT="/workspaces/HVO.WebSite"
PORT="${OPENCODE_WEB_PORT:-4096}"
HOST="${OPENCODE_WEB_HOST:-0.0.0.0}"
LOG_FILE="/tmp/opencode-web.log"

export PATH="$HOME/.opencode/bin:$HOME/.local/bin:$PATH"

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

if ! command -v opencode >/dev/null 2>&1; then
	echo "opencode is not installed; skipping opencode web startup." >> "$LOG_FILE"
	exit 0
fi

if pgrep -u "$(id -u)" -f "opencode web.*--port ${PORT}" >/dev/null 2>&1; then
	exit 0
fi

ensure_opencode_state_dirs
ensure_xdg_open

cd "$REPO_ROOT"
if [ -z "${OPENCODE_SERVER_PASSWORD:-}" ]; then
	echo "OPENCODE_SERVER_PASSWORD is not set; starting opencode web without authentication." >> "$LOG_FILE"
fi
echo "Starting opencode web on ${HOST}:${PORT}." >> "$LOG_FILE"
setsid -f opencode web --hostname "$HOST" --port "$PORT" </dev/null >> "$LOG_FILE" 2>&1

if wait_for_listener; then
	echo "opencode web is listening on ${HOST}:${PORT}." >> "$LOG_FILE"
else
	echo "opencode web failed to bind ${HOST}:${PORT}; inspect this log for startup output." >> "$LOG_FILE"
	exit 1
fi
