#!/usr/bin/env bash

set -euo pipefail

docker_context="${1:-hvo-docker}"
container_name="${2:-hvo-docker-hvo-website-1}"
keys_directory="/root/.aspnet/DataProtection-Keys"

fail() {
	printf 'Website Data Protection verification failed: %s\n' "$*" >&2
	exit 1
}

command -v docker >/dev/null 2>&1 || fail 'docker is required'

mount_type="$(docker --context "${docker_context}" inspect "${container_name}" \
	--format '{{range .Mounts}}{{if eq .Destination "/root/.aspnet/DataProtection-Keys"}}{{.Type}}{{end}}{{end}}')"
[[ "${mount_type}" == "volume" ]] || fail "${keys_directory} is not a Docker volume mount"

docker --context "${docker_context}" exec "${container_name}" \
	curl --fail --silent --output /dev/null http://localhost:8080/admin || true

docker --context "${docker_context}" exec "${container_name}" sh -c '
	set -eu
	found=false
	for file in /root/.aspnet/DataProtection-Keys/*.xml; do
		[ -f "$file" ] || continue
		found=true
		grep -q "encryptedSecret" "$file" || exit 2
	done
	[ "$found" = true ]
' || fail 'the mounted key ring is empty or contains an unencrypted key'

if docker --context "${docker_context}" logs "${container_name}" 2>&1 |
	grep -E -q 'UsingEphemeralFileSystemLocationInContainer|NoXMLEncryptorConfiguredKeyMayBePersistedToStorageInUnencryptedForm'; then
	fail 'container logs contain an ephemeral or unencrypted key-ring warning'
fi

printf 'Website Data Protection verification passed for %s.\n' "${container_name}"
