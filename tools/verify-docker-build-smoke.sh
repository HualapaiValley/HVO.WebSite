#!/usr/bin/env bash

set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd -- "${script_dir}/.." && pwd)"
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
	'if [[ -n "${DOCKER_SMOKE_FAIL_IMAGE:-}" && "$*" == build*"${DOCKER_SMOKE_FAIL_IMAGE}"* ]]; then exit 23; fi' \
	'if [[ "${DOCKER_SMOKE_FAIL_PRUNE:-}" == true && "$*" == "builder prune --all --force" ]] && grep -q "${DOCKER_SMOKE_FAIL_IMAGE}" "${DOCKER_SMOKE_TEST_LOG}"; then exit 42; fi' \
	> "${fake_docker}"
chmod +x "${fake_docker}"

(
	cd "${tmp_dir}"
	DOCKER_COMMAND="${fake_docker}" DOCKER_SMOKE_TEST_LOG="${log_file}" bash "${repo_root}/tools/docker-build-smoke.sh"
)

[[ "$(grep -c '^build ' "${log_file}")" == 6 ]] || { printf 'Expected six Docker smoke builds.\n' >&2; exit 1; }
[[ "$(grep -c '^image rm --force ' "${log_file}")" == 6 ]] || { printf 'Expected every smoke image to be removed.\n' >&2; exit 1; }
[[ "$(grep -c '^builder prune --all --force$' "${log_file}")" == 8 ]] || { printf 'Expected cache cleanup before, between, and after builds.\n' >&2; exit 1; }

: > "${log_file}"
set +e
(
	cd "${tmp_dir}"
	DOCKER_COMMAND="${fake_docker}" DOCKER_SMOKE_TEST_LOG="${log_file}" \
		DOCKER_SMOKE_FAIL_IMAGE="hvo-jkbms-ci:latest" DOCKER_SMOKE_FAIL_PRUNE=true \
		bash "${repo_root}/tools/docker-build-smoke.sh"
)
failure_status="$?"
set -e
[[ "${failure_status}" == 23 ]] || { printf 'Expected cleanup to preserve the Docker build failure.\n' >&2; exit 1; }
grep -q '^image rm --force hvo-jkbms-ci:latest$' "${log_file}" || {
	printf 'Expected the failed build image to be cleaned by the exit trap.\n' >&2
	exit 1
}
[[ "$(grep -c '^builder prune --all --force$' "${log_file}")" == 4 ]] || {
	printf 'Expected cache cleanup to run after the failed build.\n' >&2
	exit 1
}

printf 'Verified bounded Docker smoke build cleanup for all six images.\n'
