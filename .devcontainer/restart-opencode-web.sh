#!/bin/bash
set -euo pipefail

SCRIPT_DIR="$(dirname "$(realpath "$0")")"

echo "=== Restarting opencode web ==="
bash "${SCRIPT_DIR}/stop-opencode-web.sh"
echo ""
exec bash "${SCRIPT_DIR}/start-opencode-web.sh"
