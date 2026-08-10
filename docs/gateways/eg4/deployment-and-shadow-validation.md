# EG4 6500EX Deployment And Shadow Validation

This runbook commissions the read-only EG4 6500EX battery gateway on `devpi5`. It does not enable MPPT, PV, AC, arbitrary PI30 commands, setting writes, or the battery-facing BMS RS485 connection.

## Safety Boundary

- Connect only an inverter RS232/COM monitoring cable exposed as USB HID `0665:5161`.
- Never map the CH340 `1a86:7523` MPPT/BMS cable or `/dev/ttyUSB0` into this container.
- The runtime allowlists identity inquiries and `QPIGS`; it has no command endpoint.
- Keep SolarAssistant, SmartShunt, JK BMS, Davis, and TP-Link/Kasa unchanged during commissioning.
- SmartShunt remains preferred for whole-bus electrical values. SolarAssistant remains preferred for PV, AC, load, and SOC according to the central composition policy.

## Verified Host Baseline

Read-only inspection on 2026-08-10 established:

- `devpi5` is Raspberry Pi 5 `aarch64` with Docker 29.5.0.
- Host port 5600 is available; container port 8080 is used only inside the EG4 stack.
- Docker storage has approximately 69 GB available.
- The public website health and power-ingest route are reachable from the Pi over TLS.
- One 6500EX HID is attached at physical USB path `1-1` as `0665:5161` and currently enumerates as `/dev/hidraw0`.
- The HID exposes no serial number, so stable identity must use fixed physical USB topology.
- Only one inverter HID is currently attached. The second-device overlay must remain disabled until its cable and physical path are verified.

## Stable HID Identity

Never configure `/dev/hidrawN`; enumeration can change after reboot or reconnect.

1. Inspect the current HID without opening it:

```bash
udevadm info --query=property --name=/dev/hidraw0
udevadm info --attribute-walk --name=/dev/hidraw0
```

2. Verify `ID_VENDOR_ID=0665`, `ID_MODEL_ID=5161`, and the physical parent path.
3. Review `deploy/pi-gateways/eg4/99-hvo-eg4.rules.example`.
4. Install only rules whose physical paths have been verified:

```bash
sudo install -m 0644 deploy/pi-gateways/eg4/99-hvo-eg4.rules.example /etc/udev/rules.d/99-hvo-eg4.rules
sudo udevadm control --reload-rules
sudo udevadm trigger --subsystem-match=hidraw
```

5. Verify the stable node and permissions:

```bash
readlink -f /dev/hvo/eg4-6500ex-a
stat -c '%A %U %G %n' /dev/hvo/eg4-6500ex-a
```

The container runs as root but receives only the explicitly mapped HID nodes; it is not privileged. The checked-in rule keeps the host node root-only. Reverify the symlink after unplug/replug and reboot. If a second identical HID is added, assign it a different fixed physical USB path, add a verified rule for `eg4-6500ex-b`, and test with reversed enumeration order.

## Configuration

1. Copy `deploy/pi-gateways/eg4/.env.example` to the ignored `.env` file.
2. Provision an API key with only `ingest:power` and set `EG4_POWER_API_KEY` without committing or printing it.
3. Keep `EG4_DEVICE_1_ENABLED=false` while only one HID exists.
4. Keep the source and device IDs stable; list indexes are configuration positions, not identity.
5. Keep `HVO_WEBSITE_PUBLIC_BASE_URL` on the HTTPS public website route.
6. Leave `OTEL_COLLECTOR_ENDPOINT` empty unless collector reachability has been verified.

The one-device base stack maps only `EG4_DEVICE_0_PORT`. When `EG4_DEVICE_1_ENABLED=true`, `deploy-pi-gateway.sh` automatically adds `docker-compose.two-device.yml`; both stable HID nodes must then exist before container creation.

The deployment intentionally builds the image natively through the remote `devpi5` Docker context. The current registry publishing script runs on x64 and does not publish EG4 until a multi-architecture publishing workflow is implemented.

## Preflight

Check the host before every rollout:

```bash
ssh devpi5 uname -m
docker --context devpi5 version --format '{{.Server.Arch}} {{.Server.Version}}'
docker --context devpi5 ps --format 'table {{.Names}}\t{{.Status}}\t{{.Ports}}'
docker --context devpi5 system df
docker --context devpi5 info --format '{{.DockerRootDir}} {{.LoggingDriver}}'
ssh devpi5 "ss -ltn '( sport = :5600 )'; df -h /var/lib/docker; lsusb; stat -c '%A %U %G %n' /dev/hidraw*"
ssh devpi5 curl --fail https://www.hualapaivalleyobservatory.org/health/live
ssh devpi5 'test "$(curl --silent --show-error --output /dev/null --write-out "%{http_code}" https://www.hualapaivalleyobservatory.org/api/v1/power/readings)" = 405'
```

Confirm port 5600 is unclaimed and both configured `/dev/hvo` nodes resolve to the intended physical devices. Confirm cable isolation, grounding, and USB power suitability physically; software inventory cannot prove them.

Docker Compose gives shell environment variables precedence over `--env-file`. The deploy script refuses EG4 shell overrides by default and reports variable names only. Unset stale variables. Use `--allow-env-overrides` only after intentionally verifying every override.

Run secret-safe validation and dry run:

```bash
docker compose \
  --env-file deploy/pi-gateways/eg4/.env \
  -f deploy/pi-gateways/eg4/docker-compose.yml \
  config --quiet

./scripts/deploy-pi-gateway.sh --dry-run --context devpi5 eg4
```

When device 1 is enabled, include `docker-compose.two-device.yml` in manual Compose checks. Do not save rendered Compose output because service environment values include the API key.

If OTLP is configured, verify basic HTTP connectivity from the Pi before deployment. A 404 response at the collector root is acceptable; a DNS, connection, or timeout failure is not:

```bash
ssh devpi5 "curl --show-error --connect-timeout 5 --max-time 10 --output /dev/null '${OTEL_COLLECTOR_ENDPOINT%/}/'"
```

## Rollout

After explicit rollout approval:

```bash
./scripts/deploy-pi-gateway.sh --context devpi5 eg4
HVO_CHECK_EG4=true ./scripts/check-deployments.sh
```

The deploy script validates Compose, recreates only the EG4 stack, waits for the device-aware `/health` endpoint, and verifies bounded logging plus zero core limits. EG4 is deliberately excluded from the `all` target during commissioning.

## Verification

Local UI and health:

```bash
curl --fail http://devpi5:5600/health
docker --context devpi5 ps --filter name=hvo-eg4
eg4_container="$(docker --context devpi5 ps -q \
  --filter label=com.docker.compose.project=eg4 \
  --filter label=com.docker.compose.service=hvo-eg4)"
test -n "${eg4_container}"
docker --context devpi5 logs --since 30m "${eg4_container}"
```

Protected diagnostics require the configured key. Load it from the approved deployment secret without printing it. Put the header in a temporary root-readable file so the key does not appear in curl process arguments, then remove it:

```bash
umask 077
eg4_header="$(mktemp)"
trap 'rm -f "${eg4_header}"' EXIT HUP INT TERM
printf 'X-Api-Key: %s\n' "${EG4_POWER_API_KEY}" > "${eg4_header}"
curl --fail --header "@${eg4_header}" http://devpi5:5600/diagnostics/status
curl --fail --header "@${eg4_header}" http://devpi5:5600/diagnostics/devices
curl --fail --header "@${eg4_header}" http://devpi5:5600/diagnostics/outbox
rm -f "${eg4_header}"
trap - EXIT HUP INT TERM
```

Verify container policy and durable outbox:

```bash
docker --context devpi5 inspect "${eg4_container}" --format '{{json .HostConfig.LogConfig}} {{json .HostConfig.Ulimits}}'
./scripts/outbox-maintenance.sh --remote --context devpi5 summary eg4
./scripts/outbox-maintenance.sh --remote --context devpi5 schema eg4
```

Recreate the EG4 container without deleting volumes and confirm pending records and forwarding state survive. Never use `down -v`.

## Shadow Run

Run for 24-72 hours before considering any source-policy change.

Record:

- Stable source/device identity across reconnect, container recreation, and reboot.
- Firmware identity acceptance after each reconnect.
- Healthy/degraded/offline transitions and any HID ownership conflict.
- Outbox pending, retry-exhausted, permanent-failure, and last-sent state.
- Charging, discharging, and idle samples, including a simultaneous zero-charge/zero-discharge frame when naturally observed.
- Timestamp skew between each EG4 branch and the nearest SmartShunt whole-bus sample.
- Direct branch sum compared with SmartShunt whole-bus net flow using the canonical sign: positive discharge, negative charge.

EG4 branch values and SmartShunt whole-bus values are different physical measurements and are not expected to match exactly. Explain differences using update cadence, integer-amp EG4 resolution, wiring and conversion losses, MPPT charging, other DC loads, and standby consumption. MPPT residual analysis remains deferred because no safe controller-monitoring interface is configured.

## Promotion Criteria

- 24-72 hours without command/write traffic or unintended PV/AC ingest.
- Stable `/dev/hvo` aliases after reconnect/reboot and, when present, reordered two-device enumeration.
- Supported model and firmware identity on every enabled inverter.
- Healthy polling with explained transient failures only.
- Durable outbox recovery with no growing normal-operation backlog or permanent failures.
- Successful local SQL retention of distinct `eg4-6500ex-*` source/device rows.
- Website comparison shows each inverter as an `Inverter branch`, never a whole-bus value.
- Branch-versus-bus differences are physically explainable at bounded timestamp skew.
- SolarAssistant, SmartShunt, and JK BMS remain operational and independently deployable.

No EG4 source becomes preferred automatically. Any future composition-policy promotion requires a separate explicit decision and tests.

## Rollback

Rollback affects only EG4:

```bash
eg4_container="$(docker --context devpi5 ps -aq \
  --filter label=com.docker.compose.project=eg4 \
  --filter label=com.docker.compose.service=hvo-eg4)"
test -n "${eg4_container}"
docker --context devpi5 stop "${eg4_container}"
docker --context devpi5 rm "${eg4_container}"
```

Preserve the `eg4_eg4-outbox` volume and the `.env` used for the run. Restore the prior image/configuration if one exists, then recreate only `hvo-eg4`. Do not stop existing gateway containers, run `down -v`, or perform broad volume pruning. If an older image cannot read the current outbox schema, leave the volume untouched and investigate from a read-only copy.

## References

- Docker Compose environment precedence: https://docs.docker.com/compose/how-tos/environment-variables/envvars-precedence/
- Docker Compose multiple-file merge rules: https://docs.docker.com/compose/how-tos/multiple-compose-files/merge/
- Docker Compose `devices`, health check, logging, restart, and ulimit service fields: https://docs.docker.com/reference/compose-file/services/
- Docker Engine device mapping: https://docs.docker.com/engine/containers/run/#runtime-privilege-and-linux-capabilities
- `udev` rule matching, parent attributes, ownership, mode, and symlinks: https://www.freedesktop.org/software/systemd/man/latest/udev.html
- EG4 protocol evidence and exact read-only boundary: `docs/gateways/eg4/6500ex-protocol.md`
