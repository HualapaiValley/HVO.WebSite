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

	if [ ! -d "$local_dir" ]; then
		mkdir -p "$local_dir"
	fi

	if [ ! -w "$local_dir" ] || [ ! -O "$local_dir" ]; then
		sudo chown -R "$(id -u)":"$(id -g)" "$local_dir"
	fi

	mkdir -p "$state_dir" "$opencode_share_dir"
}

ensure_xdg_open() {
	if command -v xdg-open >/dev/null 2>&1; then
		return 0
	fi

	mkdir -p "$HOME/.local/bin"
	printf '%s\n' '#!/bin/sh' 'if [ -n "${BROWSER:-}" ]; then' '	"$BROWSER" "$@" >/dev/null 2>&1 &' 'fi' 'exit 0' > "$HOME/.local/bin/xdg-open"
	chmod +x "$HOME/.local/bin/xdg-open"
}

if ! command -v opencode >/dev/null 2>&1; then
	echo "opencode is not installed; skipping opencode web startup." >> "$LOG_FILE"
	exit 0
fi

if pgrep -u "$(id -u)" -f "opencode web.*--port ${PORT}" >/dev/null 2>&1; then
	exit 0
fi

if [ -z "${OPENCODE_SERVER_PASSWORD:-}" ]; then
	echo "OPENCODE_SERVER_PASSWORD is not set; skipping opencode web startup." >> "$LOG_FILE"
	exit 0
fi

ensure_opencode_state_dirs
ensure_xdg_open

cd "$REPO_ROOT"
echo "Starting opencode web on ${HOST}:${PORT}." >> "$LOG_FILE"
nohup opencode web --hostname "$HOST" --port "$PORT" >> "$LOG_FILE" 2>&1 &
