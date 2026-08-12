#!/bin/sh

set -eu

root="$1"
staged="$2"
config="$3"
secrets="$4"
backup="${root}/.previous"
activated=false

rm -rf "${backup}"
install -d -m 700 "${backup}"

restore_previous() {
	[ "${activated}" = true ] || return 0
	rm -f "${config}"
	rm -rf "${secrets}"
	[ ! -f "${backup}/gateway.json" ] || mv "${backup}/gateway.json" "${config}"
	[ ! -d "${backup}/secrets" ] || mv "${backup}/secrets" "${secrets}"
}

trap restore_previous EXIT HUP INT TERM
[ ! -f "${config}" ] || mv "${config}" "${backup}/gateway.json"
[ ! -d "${secrets}" ] || mv "${secrets}" "${backup}/secrets"
activated=true

if ! mv "${staged}/secrets" "${secrets}" || ! mv "${staged}/gateway.json" "${config}"; then
	restore_previous
	exit 1
fi

chmod 600 "${config}"
find "${secrets}" -type f -exec chmod 600 {} +
rm -f "${staged}/activate.sh"
rmdir "${staged}"
activated=false
trap - EXIT HUP INT TERM
