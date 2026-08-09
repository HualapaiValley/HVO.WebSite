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

verify_observability_storage() {
	local compose_file="$1"
	local env_file="$2"

	if [[ "${dry_run}" == true ]]; then
		printf '[dry-run] verify Loki and collector paths are existing directories on the tank-backed /var/lib/docker XFS mount\n'
		return 0
	fi

	command -v jq >/dev/null 2>&1 || fail 'Required command not found: jq'
	local config_json data_root loki_path collector_path expected_device docker_root docker_endpoint remote_target mount_info owner
	config_json="$(docker --context "${docker_context}" compose --env-file "${env_file}" -f "${compose_file}" config --format json)"
	loki_path="$(jq -r '.services.loki.volumes[] | select(.target == "/loki") | .source' <<<"${config_json}")"
	collector_path="$(jq -r '.services["otel-collector"].volumes[] | select(.target == "/var/lib/otelcol/file_storage") | .source' <<<"${config_json}")"
	expected_device="$(jq -r '.["x-hvo-storage-device"]' <<<"${config_json}")"
	data_root="${loki_path%/loki}"
	docker_root="$(docker --context "${docker_context}" info --format '{{.DockerRootDir}}')"

	[[ -n "${loki_path}" && "${loki_path}" != null ]] || fail 'Could not resolve the Loki data bind source.'
	[[ "${collector_path}" == "${data_root}/otelcol" ]] || fail "Collector bind source '${collector_path}' must be '${data_root}/otelcol'."
	[[ -n "${expected_device}" && "${expected_device}" != null ]] || fail 'Could not resolve the expected observability storage device.'
	[[ "${data_root}" == "${docker_root}"/* ]] || fail "Observability data root '${data_root}' must be below Docker root '${docker_root}'."
	[[ "${data_root}" =~ ^/[A-Za-z0-9._/-]+$ ]] || fail "Observability data root contains unsupported characters: ${data_root}"

	docker_endpoint="$(docker context inspect "${docker_context}" --format '{{.Endpoints.docker.Host}}')"
	if [[ "${docker_endpoint}" == ssh://* ]]; then
		remote_target="${docker_endpoint#ssh://}"
		mount_info="$(ssh -o BatchMode=yes "${remote_target}" findmnt -n -T "${data_root}" -o TARGET,SOURCE,FSTYPE)"
		ssh -o BatchMode=yes "${remote_target}" test -d "${loki_path}" ||
			fail "Missing ${loki_path} on ${remote_target}; provision and migrate storage before deployment."
		ssh -o BatchMode=yes "${remote_target}" test -d "${collector_path}" ||
			fail "Missing ${collector_path} on ${remote_target}; provision it before deployment."
		owner="$(ssh -o BatchMode=yes "${remote_target}" stat -c '%u:%g' "${loki_path}" "${collector_path}" | sort -u)"
	else
		mount_info="$(findmnt -n -T "${data_root}" -o TARGET,SOURCE,FSTYPE)"
		[[ -d "${loki_path}" ]] || fail "Missing ${loki_path}; provision and migrate storage before deployment."
		[[ -d "${collector_path}" ]] || fail "Missing ${collector_path}; provision it before deployment."
		owner="$(stat -c '%u:%g' "${loki_path}" "${collector_path}" | sort -u)"
	fi

	[[ "${mount_info}" == "${docker_root} ${expected_device} xfs" ]] ||
		fail "Expected ${docker_root} to resolve to ${expected_device} as XFS; got '${mount_info}'."
	[[ "${owner}" == "10001:10001" ]] ||
		fail "Loki and collector data directories must both be owned by 10001:10001; got '${owner//$'\n'/, }'."
	printf 'Verified tank-backed observability storage at %s (%s)\n' "${data_root}" "${mount_info}"
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
	if [[ "${stack_name}" == observability ]]; then
		verify_observability_storage "${compose_file}" "${env_file}"
	fi
	run_cmd docker --context "${docker_context}" compose --env-file "${env_file}" -f "${compose_file}" up -d --wait --wait-timeout 120
	run_cmd docker --context "${docker_context}" compose --env-file "${env_file}" -f "${compose_file}" ps
	if [[ "${dry_run}" == false ]]; then
		mapfile -t container_ids < <(docker --context "${docker_context}" compose --env-file "${env_file}" -f "${compose_file}" ps -aq)
		"${repo_root}/tools/verify-running-container-policy.sh" "${docker_context}" "${container_ids[@]}"
	fi
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
