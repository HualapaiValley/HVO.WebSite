#!/bin/sh

set -eu

root="$1"
staged="$2"
config="$3"
secrets="$4"
runtime="${root}/runtime"
has_runtime=false
[ ! -d "${staged}/runtime" ] || has_runtime=true
backup="${root}/.previous"
restore_enabled=true
config_saved=false
secrets_saved=false
runtime_saved=false
config_installed=false
secrets_installed=false
runtime_installed=false

rm -rf "${backup}"
install -d -m 700 "${backup}"

restore_previous() {
	[ "${restore_enabled}" = true ] || return 0
	restore_enabled=false
	if [ "${config_saved}" = true ] || [ "${config_installed}" = true ]; then
		rm -f "${config}"
		[ "${config_saved}" = false ] || mv "${backup}/gateway.json" "${config}"
	fi
	if [ "${secrets_saved}" = true ] || [ "${secrets_installed}" = true ]; then
		rm -rf "${secrets}"
		[ "${secrets_saved}" = false ] || mv "${backup}/secrets" "${secrets}"
	fi
	if [ "${runtime_saved}" = true ] || [ "${runtime_installed}" = true ]; then
		rm -f "${runtime}/.env"
		[ "${runtime_saved}" = false ] || mv "${backup}/runtime/.env" "${runtime}/.env"
	fi
}

trap restore_previous EXIT
trap 'restore_previous; exit 1' HUP INT TERM
if [ -f "${config}" ]; then
	mv "${config}" "${backup}/gateway.json"
	config_saved=true
fi
if [ -d "${secrets}" ]; then
	mv "${secrets}" "${backup}/secrets"
	secrets_saved=true
fi
if [ "${has_runtime}" = true ] && [ -f "${runtime}/.env" ]; then
	install -d -m 700 "${backup}/runtime"
	mv "${runtime}/.env" "${backup}/runtime/.env"
	runtime_saved=true
fi

mv "${staged}/secrets" "${secrets}"
secrets_installed=true
mv "${staged}/gateway.json" "${config}"
config_installed=true
if [ "${has_runtime}" = true ]; then
	# Preserve the directory inode: existing containers bind this directory.
	# Replacing the directory would leave those containers on the old mount.
	install -d -m 700 "${runtime}"
	mv "${staged}/runtime/.env" "${runtime}/.env"
	runtime_installed=true
	chmod 600 "${runtime}/.env"
	rmdir "${staged}/runtime"
fi

chmod 600 "${config}"
find "${secrets}" -type f -exec chmod 600 {} +
rm -f "${staged}/activate.sh"
rmdir "${staged}"
restore_enabled=false
trap - EXIT HUP INT TERM
