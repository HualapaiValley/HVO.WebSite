# EG4 6500EX Deployment And Shadow Validation

This runbook commissions read-only EG4 6500EX and MPPT100-48HV telemetry on `devpi5`. It never enables arbitrary PI30 commands, setting writes, Modbus writes, firmware operations, or the inverter battery-facing BMS RS485 connection.

## Safety Boundary

- Connect only an inverter RS232/COM monitoring cable exposed as USB HID `0665:5161`.
- Never map enumerated `/dev/ttyUSB0`; use the verified MPPT `/dev/serial/by-id/...` identity only.
- The inverter runtime allowlists identity, `QPIGS`, `QPGS0`, `Q1`, and firmware-conditional `QPIGS2`. The MPPT runtime permits only unit-1 function-`0x03` registers 200-217. Neither has a command endpoint.
- Keep SolarAssistant, SmartShunt, JK BMS, Davis, and TP-Link/Kasa unchanged during commissioning.
- The direct EG4 collector owns 6500EX and MPPT100 acquisition. SolarAssistant remains an independent comparison source during migration and is never a runtime input. JK BMS/SmartShunt remain authoritative for SOC and whole-bus measurements.

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

1. Copy `.env.example` to ignored `.env` and `gateway.json.example` to ignored `gateway.json`.
2. Create the ignored `secrets` directory with root-readable files named `diagnostics-api-key`, `central-ingest-api-key`, `mqtt-username`, and `mqtt-password`. Do not place raw credentials in `.env` or `gateway.json`.
3. Provision the central key with only `ingest:power`. Issue #330 transfers exact source claims during one-writer cutover.
4. Keep the second inverter disabled in both `.env` and `gateway.json` while only one HID exists.
5. Set `EG4_MPPT_0_PORT` to the verified stable `/dev/serial/by-id` identity. Enable the MPPT in both `.env` and `gateway.json` only when its overlay and fixed read profile are ready.
6. Keep source and device IDs stable; list indexes are configuration positions, not identity.
7. Configure the local HA Mosquitto address and mounted MQTT secret names in `gateway.json`.
8. Leave `OTEL_COLLECTOR_ENDPOINT` empty unless collector reachability has been verified.

The base stack maps only `EG4_DEVICE_0_PORT`. When `EG4_MPPT_0_ENABLED=true`, `deploy-pi-gateway.sh` adds `docker-compose.mppt.yml` and maps only the configured stable serial path. When `EG4_DEVICE_1_ENABLED=true`, it also adds `docker-compose.two-device.yml`; both stable HID nodes must then exist before container creation.

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

Confirm port 5600 is unclaimed and every enabled `/dev/hvo` node resolves to the intended physical device. Confirm cable isolation, grounding, and USB power suitability physically; software inventory cannot prove them.

Docker Compose gives shell environment variables precedence over `--env-file`. The deploy script refuses EG4 shell overrides by default and reports variable names only. Unset stale variables. Use `--allow-env-overrides` only after intentionally verifying every override.

Run secret-safe validation and dry run:

```bash
docker compose \
  --env-file deploy/pi-gateways/eg4/.env \
  -f deploy/pi-gateways/eg4/docker-compose.yml \
  config --quiet

./scripts/deploy-pi-gateway.sh --dry-run --context devpi5 eg4
```

Include every enabled overlay in manual Compose checks. Verify that every enabled `gateway.json` device has exactly one rendered Compose device mapping and that disabled devices are not exposed.

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

Headless health and process state:

```bash
curl --fail http://devpi5:5600/health
docker --context devpi5 ps --filter name=hvo-eg4
eg4_container="$(docker --context devpi5 ps -q \
  --filter label=com.docker.compose.project=eg4 \
  --filter label=com.docker.compose.service=hvo-eg4)"
test -n "${eg4_container}"
docker --context devpi5 logs --since 30m "${eg4_container}"
```

Protected diagnostics require the dedicated diagnostics key. Build the header from the mounted secret file without printing it, then remove the temporary header:

```bash
umask 077
eg4_header="$(mktemp)"
trap 'rm -f "${eg4_header}"' EXIT HUP INT TERM
printf 'X-Api-Key: %s\n' "$(<deploy/pi-gateways/eg4/secrets/diagnostics-api-key)" > "${eg4_header}"
curl --fail --header "@${eg4_header}" http://devpi5:5600/diagnostics/status
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
- Stable HA MQTT device identity, canonical battery signs, device availability, and recovery after broker/HA restarts.
- Charging, discharging, and idle samples, including a simultaneous zero-charge/zero-discharge frame when naturally observed.
- External MPPT daylight samples, expected nighttime silence, temperature channels, and diagnostic-state history.
- Direct EG4 tracker observations compared with SolarAssistant only as independent migration evidence; the collector never reads SolarAssistant.
- Timestamp skew between each EG4 branch and the nearest SmartShunt whole-bus sample.
- Direct branch sum compared with SmartShunt whole-bus net flow using the canonical sign: positive discharge, negative charge.

EG4 branch values and SmartShunt whole-bus values are different physical measurements and are not expected to match exactly. Explain differences using update cadence, integer-amp inverter resolution, wiring and conversion losses, MPPT charging, other DC loads, and standby consumption. Never add SolarAssistant's aggregate PV to individual tracker powers.

## Promotion Criteria

- 24-72 hours without command/write traffic, fabricated nighttime zeroes, or duplicate PV aggregation.
- Stable `/dev/hvo` aliases after reconnect/reboot and, when present, reordered two-device enumeration.
- Supported model and firmware identity on every enabled inverter.
- Healthy polling with explained transient failures only.
- Durable outbox recovery with no growing normal-operation backlog or permanent failures.
- Successful local SQL retention of distinct `eg4-6500ex-*` source/device rows.
- Website comparison shows each inverter as an `Inverter branch`, never a whole-bus value.
- MPPT history preserves each independently measured direct tracker without using SolarAssistant as an input.
- Branch-versus-bus differences are physically explainable at bounded timestamp skew.
- SolarAssistant, SmartShunt, and JK BMS remain operational and independently deployable.

Direct EG4 is the target authority, but source-claim transfer and retirement of overlapping writers remain the controlled cutover in issue #330.

## Production Cutover Evidence

Issue #354 cut production over to the headless vNext collector on 2026-08-12:

- The previous image and quiescent outbox state were recorded before replacement.
- The complete `eg4_eg4-outbox` volume was archived to a checksum-verified, tar-readable backup without deleting or recreating the named volume.
- The quiescent checkpoint contained 13,728 sent records and no pending or failed records.
- The vNext collector resumed with both the 6500EX and MPPT100-48HV online, retained stable source/device identities, and continued central forwarding through the same outbox.
- A bounded container restart recovered both devices, MQTT availability, and deterministic Home Assistant entity IDs.
- Home Assistant power entities use watts, `device_class: power`, and `state_class: measurement`. Battery current and power retain the canonical EG4 sign convention: positive discharge and negative charge.
- The 6500EX MQTT device exposes aggregate PV, both MPPT channels, AC input/output, active/apparent load, operating mode, load percentage, fault/status fields, four temperature channels, firmware, charge/fan/parallel state, and output/charger diagnostics.
- The MPPT100-48HV MQTT device exposes aggregate and tracker PV, battery output, both controller temperatures, controller-estimated SOC, and the validated read-only diagnostic-register values. Opaque registers remain named diagnostics and are not presented as decoded alarms.

These are instantaneous measurements and operational state. Do not derive durable cumulative energy history inside the gateway from sampled watts. Home Assistant energy helpers may integrate power for presentation, while source-native monotonic energy counters remain preferred whenever a device provides trustworthy reset semantics.

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

Preserve the `eg4_eg4-outbox` volume, `.env`, `gateway.json`, and mounted secrets. Restore the prior image/configuration if one exists, then recreate only `hvo-eg4`. Do not run `down -v` or perform broad volume pruning. Before the vNext upgrade, stop acquisition and drain legacy reading/detail rows because the new bundle forwarder selects only `com.hvo.eg4.observation.v1`; never strand old payload types in place.

## References

- Docker Compose environment precedence: https://docs.docker.com/compose/how-tos/environment-variables/envvars-precedence/
- Docker Compose multiple-file merge rules: https://docs.docker.com/compose/how-tos/multiple-compose-files/merge/
- Docker Compose `devices`, health check, logging, restart, and ulimit service fields: https://docs.docker.com/reference/compose-file/services/
- Docker Engine device mapping: https://docs.docker.com/engine/containers/run/#runtime-privilege-and-linux-capabilities
- `udev` rule matching, parent attributes, ownership, mode, and symlinks: https://www.freedesktop.org/software/systemd/man/latest/udev.html
- EG4 protocol evidence and exact read-only boundary: `docs/gateways/eg4/6500ex-protocol.md`
