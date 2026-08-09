#!/usr/bin/env bash

set -euo pipefail

require_core=false
if [[ "${1:-}" == --require-core ]]; then
	require_core=true
	shift
fi

[[ $# -ge 2 ]] || {
	printf 'Usage: %s [--require-core] <docker-context> <container-id>...\n' "$(basename "$0")" >&2
	exit 2
}

docker_context="$1"
shift
inspection="$(docker --context "${docker_context}" inspect "$@")"

jq -e 'all(.[].State.Status; . == "running")' <<<"${inspection}" >/dev/null || {
	printf 'One or more expected containers are not running.\n' >&2
	exit 1
}

jq -e 'all(.[].HostConfig.LogConfig;
	.Type == "local" and
	.Config["max-size"] == "10m" and
	.Config["max-file"] == "3" and
	.Config.compress == "true" and
	.Config.mode == "non-blocking" and
	.Config["max-buffer-size"] == "4m")' <<<"${inspection}" >/dev/null || {
	printf 'One or more containers do not use the bounded HVO local-log policy.\n' >&2
	exit 1
}

if [[ "${require_core}" == true ]]; then
	jq -e 'all(.[].HostConfig.Ulimits;
		any(.[]; .Name == "core" and .Soft == 0 and .Hard == 0))' <<<"${inspection}" >/dev/null || {
		printf 'One or more application containers do not disable core dumps.\n' >&2
		exit 1
	}
fi

policy_name="log"
if [[ "${require_core}" == true ]]; then
	policy_name="log/core"
fi
printf 'Verified runtime %s policy for %s container(s).\n' "${policy_name}" "$#"
