#!/usr/bin/env bash

set -euo pipefail

docker_command="${DOCKER_COMMAND:-docker}"
smoke_image=""

cleanup() {
	if [[ -n "${smoke_image}" ]]; then
		"${docker_command}" image rm --force "${smoke_image}" >/dev/null 2>&1 || true
		smoke_image=""
	fi
	"${docker_command}" builder prune --all --force >/dev/null
}
trap cleanup EXIT

build_smoke_image() {
	local dockerfile="$1"
	local image="$2"

	smoke_image="${image}"
	"${docker_command}" build --file "${dockerfile}" --tag "${image}" .
	cleanup
}

# GitHub-hosted runners have limited ephemeral storage. Keep only one smoke image's
# layers at a time rather than accumulating all gateway images on the runner.
cleanup
build_smoke_image src/HVO.WebSite.v9/Dockerfile hvo-website-ci:latest
build_smoke_image src/HVO.Hardware.DavisVantagePro2/Dockerfile hvo-davis-ci:latest
build_smoke_image src/HVO.Hardware.JkBms/Dockerfile hvo-jkbms-ci:latest
build_smoke_image src/HVO.Gateway.SolarAssistant/Dockerfile hvo-solarassistant-ci:latest
build_smoke_image src/HVO.Hardware.VictronSmartShunt/Dockerfile hvo-smartshunt-ci:latest
build_smoke_image src/HVO.Gateway.TplinkKasa/Dockerfile hvo-tplinkkasa-ci:latest
