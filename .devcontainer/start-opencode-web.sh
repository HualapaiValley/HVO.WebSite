#!/bin/bash
set -euo pipefail

REPO_ROOT="/workspaces/HVO.WebSite"
PORT="${OPENCODE_WEB_PORT:-4096}"
HOST="${OPENCODE_WEB_HOST:-0.0.0.0}"
LOG_FILE="/tmp/opencode-web.log"

export PATH="$HOME/.opencode/bin:$HOME/.local/bin:$PATH"

if ! command -v opencode >/dev/null 2>&1; then
	echo "opencode is not installed; skipping opencode web startup." >> "$LOG_FILE"
	exit 0
fi

if pgrep -u "$(id -u)" -f "opencode web.*--port ${PORT}" >/dev/null 2>&1; then
	exit 0
fi

if [ -z "${OPENCODE_SERVER_PASSWORD:-}" ]; then
	export OPENCODE_SERVER_PASSWORD="changeme"
	echo "OPENCODE_SERVER_PASSWORD is not set; using temporary default 'changeme'. Change this before exposing port ${PORT} beyond your dev machine." >> "$LOG_FILE"
fi

cd "$REPO_ROOT"
echo "Starting opencode web on ${HOST}:${PORT}." >> "$LOG_FILE"
nohup opencode web --hostname "$HOST" --port "$PORT" >> "$LOG_FILE" 2>&1 &
