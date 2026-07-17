#!/usr/bin/env bash

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
docker_context="${HVO_DOCKER_CONTEXT:-hvo-docker}"
target=""
dry_run=false

usage() {
	printf 'Usage: %s [--dry-run] [--context <docker-context>] <infrastructure|observability|all>\n' "$(basename "$0")"
}

fail() {
	printf 'Error: %s\n' "$*" >&2
	exit 1
}

run_cmd() {
	if [[ "${dry_run}" == true ]]; then
		printf '[dry-run]'
		printf ' %q' "$@"
		printf '\n'
		return 0
	fi

	"$@"
}

deploy_stack() {
	local stack_name="$1"
	local stack_dir="${repo_root}/deploy/hvo-docker/${stack_name}"
	local compose_file="${stack_dir}/compose.yaml"
	local env_file="${stack_dir}/.env"

	[[ -f "${compose_file}" ]] || fail "Compose file not found: ${compose_file}"
	[[ -f "${env_file}" ]] || fail "Environment file not found: ${env_file}. Copy .env.example and configure it first."

	printf 'Deploying %s on Docker context %s\n' "${stack_name}" "${docker_context}"
	run_cmd docker --context "${docker_context}" compose --env-file "${env_file}" -f "${compose_file}" config --quiet
	run_cmd docker --context "${docker_context}" compose --env-file "${env_file}" -f "${compose_file}" up -d
	run_cmd docker --context "${docker_context}" compose --env-file "${env_file}" -f "${compose_file}" ps
}

while (($# > 0)); do
	case "$1" in
		--dry-run)
			dry_run=true
			shift
			;;
		--context)
			[[ $# -ge 2 ]] || fail '--context requires a value.'
			docker_context="$2"
			shift 2
			;;
		infrastructure|observability|all)
			[[ -z "${target}" ]] || fail 'Only one target can be specified.'
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

[[ -n "${target}" ]] || {
	usage
	exit 1
}

command -v docker >/dev/null 2>&1 || fail 'Required command not found: docker'

if [[ "${target}" == all ]]; then
	deploy_stack shared-infrastructure
	deploy_stack observability
else
	case "${target}" in
		infrastructure) deploy_stack shared-infrastructure ;;
		observability) deploy_stack observability ;;
	esac
fi