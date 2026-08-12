#!/usr/bin/env bash

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
env_file="${repo_root}/.env"
gist_id="f343db002d980ebe5fcc51413b0b7227"
dry_run=false

usage() {
	printf 'Usage: %s [--dry-run]\n' "$(basename "$0")"
	printf '\n'
	printf 'Caches %s in the private gist used by the devcontainer bootstrap.\n' "${env_file}"
	printf 'Azure Key Vault hvo-central-kv remains the authoritative secret source.\n'
	printf 'Default gist id: %s\n' "${gist_id}"
}

fail() {
	printf 'Error: %s\n' "$*" >&2
	exit 1
}

require_command() {
	command -v "$1" >/dev/null 2>&1 || fail "Required command not found: $1"
}

resolve_github_token() {
	local token="${GH_PAT:-${GH_TOKEN:-${GITHUB_TOKEN:-}}}"

	if [[ -z "${token}" ]] && command -v gh >/dev/null 2>&1; then
		token="$(gh auth token 2>/dev/null || true)"
	fi

	if [[ -z "${token}" ]] && command -v git >/dev/null 2>&1; then
		token="$(printf 'protocol=https\nhost=github.com\n\n' | git credential fill 2>/dev/null | awk -F= '/^password=/{print $2; exit}')"
	fi

	[[ -n "${token}" ]] || fail 'Could not resolve a GitHub token for gist update.'
	printf '%s' "${token}"
}

while (($# > 0)); do
	case "$1" in
		--dry-run)
			dry_run=true
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

[[ -f "${env_file}" ]] || fail "Expected environment file at ${env_file}"

if [[ "${dry_run}" == false ]]; then
	"${repo_root}/scripts/sync-secrets-from-keyvault.sh" --check || \
		fail 'Local credentials drift from hvo-central-kv. Run sync-secrets-from-keyvault.sh --apply first.'
fi

require_command jq
require_command curl

if [[ "${dry_run}" == true ]]; then
	printf 'Would sync %s to gist %s\n' "${env_file}" "${gist_id}"
	exit 0
fi

github_token="$(resolve_github_token)"
payload_file="$(mktemp)"
trap 'rm -f "${payload_file}"' EXIT

jq -n --arg content "$(<"${env_file}")" '{files: {".env": {content: $content}}}' > "${payload_file}"

curl -fsS \
	-X PATCH \
	-H "Authorization: token ${github_token}" \
	-H 'Content-Type: application/json' \
	-d @"${payload_file}" \
	"https://api.github.com/gists/${gist_id}" >/dev/null

printf 'Synced %s to gist %s\n' "${env_file}" "${gist_id}"
