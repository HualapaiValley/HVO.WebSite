#!/usr/bin/env bash

set -euo pipefail

tmp_dir="$(mktemp -d "${TMPDIR:-/tmp}/hvo-docker-smoke.XXXXXX")"
cleanup() {
	rm -rf "${tmp_dir}"
}
trap cleanup EXIT

fake_docker="${tmp_dir}/docker"
log_file="${tmp_dir}/docker.log"
printf '%s\n' \
	'#!/usr/bin/env bash' \
	'printf "%s\n" "$*" >> "${DOCKER_SMOKE_TEST_LOG}"' \
	'if [[ -n "${DOCKER_SMOKE_FAIL_IMAGE:-}" && "$*" == *"${DOCKER_SMOKE_FAIL_IMAGE}"* ]]; then exit 1; fi' \
	> "${fake_docker}"
chmod +x "${fake_docker}"

DOCKER_COMMAND="${fake_docker}" DOCKER_SMOKE_TEST_LOG="${log_file}" bash tools/docker-build-smoke.sh

[[ "$(rg -c '^build ' "${log_file}")" == 6 ]] || { printf 'Expected six Docker smoke builds.\n' >&2; exit 1; }
[[ "$(rg -c '^image rm --force ' "${log_file}")" == 6 ]] || { printf 'Expected every smoke image to be removed.\n' >&2; exit 1; }
[[ "$(rg -c '^builder prune --all --force$' "${log_file}")" == 8 ]] || { printf 'Expected cache cleanup before, between, and after builds.\n' >&2; exit 1; }

: > "${log_file}"
if DOCKER_COMMAND="${fake_docker}" DOCKER_SMOKE_TEST_LOG="${log_file}" \
	DOCKER_SMOKE_FAIL_IMAGE="hvo-jkbms-ci:latest" bash tools/docker-build-smoke.sh; then
	printf 'Expected the scripted Docker build failure to propagate.\n' >&2
	exit 1
fi
rg -q '^image rm --force hvo-jkbms-ci:latest$' "${log_file}" || {
	printf 'Expected the failed build image to be cleaned by the exit trap.\n' >&2
	exit 1
}
[[ "$(rg -c '^builder prune --all --force$' "${log_file}")" == 4 ]] || {
	printf 'Expected cache cleanup to run after the failed build.\n' >&2
	exit 1
}

printf 'Verified bounded Docker smoke build cleanup for all six images.\n'
