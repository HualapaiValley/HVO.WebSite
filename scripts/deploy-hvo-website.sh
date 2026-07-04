#!/usr/bin/env bash

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
docker_context="${HVO_DOCKER_CONTEXT:-hvo-docker}"
env_file="${HVO_WEBSITE_ENV_FILE:-${repo_root}/deploy/hvo-docker/.env}"
dry_run=false
build_images=true
image_tag=""

usage() {
	printf 'Usage: %s [--dry-run] [--context <docker-context>] [--no-build] [--tag <image-tag>]\n' "$(basename "$0")"
	printf '\n'
	printf 'Deploys the HVO website to hvo-docker using Docker Compose.\n'
	printf 'If --tag is provided, uses a pre-built image from the self-hosted registry\n'
	printf 'instead of building locally. The image must already exist on hvo-docker.\n'
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

warn_shell_env_overrides() {
	local compose_file="$1"
	local env_names=()
	local line remainder env_name existing

	while IFS= read -r line; do
		remainder="${line}"
		while [[ "${remainder}" =~ \$\{([A-Za-z_][A-Za-z0-9_]*) ]]; do
			env_name="${BASH_REMATCH[1]}"
			env_names+=("${env_name}")
			remainder="${remainder#*\$\{}"
		done
	done < "${compose_file}"

	for env_name in "${env_names[@]}"; do
		for existing in "${seen_env_names[@]:-}"; do
			[[ "${existing}" == "${env_name}" ]] && continue 2
		done
		seen_env_names+=("${env_name}")

		if [[ -v "${env_name}" ]]; then
			printf 'Warning: shell environment variable %s is set and will override the .env file used by docker compose --env-file.\n' "${env_name}" >&2
		fi
	done
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
		--tag)
			[[ $# -ge 2 ]] || fail '--tag requires a value.'
			image_tag="$2"
			shift 2
			;;
		--no-build)
			build_images=false
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

command -v docker >/dev/null 2>&1 || fail 'Required command not found: docker'

compose_file="${repo_root}/deploy/hvo-docker/docker-compose.yml"
[[ -f "${compose_file}" ]] || fail "Compose file not found: ${compose_file}"

printf 'Target: hvo-website\n'
printf 'Docker context: %s\n' "${docker_context}"

compose_args=(--context "${docker_context}" compose --env-file "${env_file}" -f "${compose_file}")
up_args=(up -d)
seen_env_names=()

warn_shell_env_overrides "${compose_file}"

if [[ -n "${image_tag}" ]]; then
	printf 'Image tag: %s\n' "${image_tag}"
	IMAGE_NAME="registry.hualapaivalleyobservatory.org/hvo-website:${image_tag}"
	tmp_compose="$(mktemp /tmp/hvo-website-override.XXXXXX.yml)"
	cat > "${tmp_compose}" <<YML
services:
  hvo-website:
    image: ${IMAGE_NAME}
    build: !reset null
YML
	compose_args+=(-f "${tmp_compose}")
	trap "rm -f ${tmp_compose}" EXIT
elif [[ "${build_images}" == true ]]; then
	printf 'Building image locally.\n'
	up_args+=(--build)
else
	printf 'Using existing image tag from compose file.\n'
fi

run_cmd docker "${compose_args[@]}" config --quiet
run_cmd docker "${compose_args[@]}" "${up_args[@]}"
run_cmd docker "${compose_args[@]}" ps
