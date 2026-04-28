#!/bin/bash
# post-attach.sh — Runs each time VS Code attaches to the container.
# Fetches the private .env gist when it's missing, and sets up a
# one-shot .zshrc hook as a fallback (the credential helper isn't
# always ready during postAttachCommand on first build).
set -e

ENV_GIST="1f014918502877f0c37738fa733dad65"
ENV_FILE="/workspaces/HVO.WebSite/.env"

# ── Ensure .env sourcing + one-shot fetch hook in .zshrc (idempotent) ─
ZSHRC="$HOME/.zshrc"
SOURCE_LINE="[ -f ${ENV_FILE} ] && set -a && source ${ENV_FILE} && set +a"
FETCH_LINE="[ ! -f ${ENV_FILE} ] && [ -f /workspaces/HVO.WebSite/.devcontainer/post-attach.sh ] && bash /workspaces/HVO.WebSite/.devcontainer/post-attach.sh 2>/dev/null"
if ! grep -qF "$SOURCE_LINE" "$ZSHRC" 2>/dev/null; then
	{
		echo ""
		echo "# Auto-fetch and source HVO.WebSite .env"
		echo "$FETCH_LINE"
		echo "$SOURCE_LINE"
	} >> "$ZSHRC"
	echo "Added .env fetch hook and sourcing to $ZSHRC"
fi

# ── Skip fetch if .env already exists ────────────────────────────────
if [ -f "$ENV_FILE" ]; then
	echo ".env already present — skipping fetch."
	exit 0
fi

echo "Fetching .env from private gist..."

# ── Resolve GitHub token ──────────────────────────────────────────────
token="${GH_PAT:-${GH_TOKEN:-${GITHUB_TOKEN:-}}}"
if [ -z "$token" ] && command -v gh >/dev/null 2>&1; then
	token=$(gh auth token 2>/dev/null) || true
fi
if [ -z "$token" ] && command -v git >/dev/null 2>&1; then
	token=$(printf 'protocol=https\nhost=github.com\n' \
		| GIT_TERMINAL_PROMPT=0 git credential fill 2>/dev/null \
		| grep '^password=' | head -1 | cut -d= -f2-) || true
fi
if [ -z "$token" ]; then
	echo "⚠  No GitHub token available yet — .env will be fetched when you open your first terminal."
	exit 0
fi

# ── Fetch private .env gist ──────────────────────────────────────────
env_content=""
if command -v gh >/dev/null 2>&1; then
	env_content=$(GH_TOKEN="$token" gh gist view "$ENV_GIST" --raw --filename .env 2>/dev/null) || true
fi
if [ -z "$env_content" ]; then
	env_content=$(curl -fsSL -H "Authorization: token $token" \
		"https://api.github.com/gists/$ENV_GIST" 2>/dev/null \
		| jq -r '.files[".env"].content // empty') || true
fi
if [ -z "$env_content" ]; then
	echo "⚠  Could not fetch .env from gist $ENV_GIST"
	exit 0
fi

printf '%s\n' "$env_content" > "$ENV_FILE"
chmod 600 "$ENV_FILE"
echo "✅ .env written to $ENV_FILE"

# ── Source .env into current session ─────────────────────────────────
set -a
# shellcheck disable=SC1090
source "$ENV_FILE"
set +a
