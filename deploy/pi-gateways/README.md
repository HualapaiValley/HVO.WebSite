# Pi Gateway Deployments

Minimal per-gateway Docker Compose deployments for edge hosts. Davis runs on
hvo-docker using the same Compose definition; other active collectors retain
their Pi-class hosts.

## Website upstream policy

Deployed Pi gateways post directly to the internally hosted `HVO.WebSite` API on `hvo-docker`. The public Azure address is only a proxy path into the local environment and is not a gateway ingest dependency.

Practical default:

- Pi deployment: `HVO_WEBSITE_PUBLIC_BASE_URL=http://hvo-docker.hvo.lan`
- Local development/integration override: `HVO_WEBSITE_PUBLIC_BASE_URL=http://<dev-host>`

## API key source of truth

Gateway API keys should be stored in Azure Key Vault and loaded by the website at startup through its configured Key Vault provider.

Required website secret names in `hvo-central-kv`:

- `Seeding--DavisApiKey`
- `Seeding--BmsApiKey`
- `Seeding--PowerApiKey`
- `Seeding--SmartShuntApiKey`
- `Seeding--SmartShuntSourceId`
- `Seeding--WeatherReadApiKey`
- `Seeding--PowerReadApiKey`

Reason:

- The website loads Azure Key Vault into configuration at startup.
- ASP.NET Core configuration maps `--` in Key Vault secret names to `:` in configuration keys.
- `ApiKeySeedService` reads `Seeding:DavisApiKey`, `Seeding:BmsApiKey`, `Seeding:PowerApiKey`, `Seeding:WeatherReadApiKey`, and `Seeding:PowerReadApiKey`.

Current local raw keys already present in the dev environment:

- `DAVIS_API_KEY` for `ingest:weather`
- `BMS_API_KEY` for `ingest:bms`

New power keys generated for rollout:

- `POWER_API_KEY` for `ingest:power`
- `POWER_READ_API_KEY` for `read:power`

Weather read clients:

- `WEATHER_READ_API_KEY` for `read:weather`

Important:

- The database stores only key hashes, not the raw values.
- After a raw key is lost, it cannot be recovered from SQL.
- Keep raw values in approved secret stores and the target's ignored mounted
  secret files. Legacy root/handoff env copies are not current gateway mounts.

## Mounted configuration and secrets

Current collectors require non-secret `gateway.json` mounted read-only at
`/app/config/gateway.json`, secret files read-only at `/run/secrets` and their
durable named volume at `/app/data`. Production startup requires the mounted
JSON. Creation-time environment overrides JSON; the mounted runtime `.env`
then supplies startup overrides as described below. JSON/env reload is disabled
and secrets are resolved at startup, so changed app configuration/credentials
need an approved restart. See the [runtime contract](../../docs/architecture/EDGE_VNEXT_RUNTIME.md).

First establish the [existing root bootstrap and global sync prerequisites](../../docs/development/key-vault-materialization.md),
including its sourced SSH field, parsable SQL string and full vault-read scope.
A gateway `.env`/`gateway.json` alone is insufficient. Then use authenticated
Azure CLI access to `hvo-central-kv`:

```bash
./scripts/sync-secrets-from-keyvault.sh --check
# Writes approved local materializations; does not rotate vault credentials:
./scripts/sync-secrets-from-keyvault.sh --apply
```

`--check` is the default and reads vault values to detect drift without changing
files. `--apply` operates across its fixed root/website/gateway materializations,
not just the gateway being deployed. Review that scope before using it. The
script writes under `deploy/pi-gateways/<gateway>/secrets`, with directory mode
700/file mode 600; it does not discover custom `*_SECRETS_DIRECTORY` values.
Custom local paths or secret filenames require an explicit secure transfer or
materialization plan, followed by preflight. Never print values to verify them.

| Target | Required mounted files / exceptions |
|---|---|
| Davis | `diagnostics-api-key` and `central-ingest-api-key`; `mqtt-username`/`mqtt-password` when MQTT is enabled; station-key/passcode files only for enabled external delivery as described below |
| SmartShunt | `diagnostics-api-key` and `central-ingest-api-key`; MQTT pair when enabled |
| JK BMS | Diagnostics/ingest pair; MQTT pair when enabled; each enabled device's configured six-digit settings-password file. The sync script's known-bank selection reads `gateway.json.example`; altered local device/file mappings require explicit materialization |
| EG4 | Diagnostics/ingest pair and both MQTT files required by deploy preflight |
| HA exporter template | Disabled, no production mappings/claims. Sync can materialize `home-assistant-token` and diagnostics, but deliberately does **not** materialize its ingest key; that requires approved exact website source claims. Token materialization alone does not authorize deployment |

Diagnostics is configured by `Edge:Runtime:DiagnosticsApiKeySecret`; central
ingest uses the separate `central-ingest-api-key`. Vault mappings are Davis
`Seeding--DavisApiKey`/`Edge--Davis--DiagnosticsApiKey`, JK
`Seeding--BmsApiKey`/`Edge--JkBms--DiagnosticsApiKey`, EG4
`Seeding--PowerApiKey`/`Edge--Eg4--DiagnosticsApiKey`, and SmartShunt
`Seeding--SmartShuntApiKey`/`Edge--SmartShunt--DiagnosticsApiKey`.
The MQTT pair comes from `HomeAssistant--MqttUsername`/`HomeAssistant--MqttPassword`.

Credential rotation is a separate approved vault/website/gateway lifecycle:
materialize the intended version, synchronize the actual mounted paths, restart
the affected runtime and verify auth/source claims/queue continuity before
retiring an old credential. Updating an unused `.env` key is insufficient. See
the [sender recovery matrix](../../docs/gateways/common-gateway-standards.md#sender-http-outcome-and-recovery-matrix)
before assuming old failures will retry.

## Current targets

- `deploy/pi-gateways/davis`
- `deploy/pi-gateways/eg4`
- `deploy/pi-gateways/jkbms`
- `deploy/pi-gateways/smartshunt`

Each gateway is deployed independently so Pi rollouts do not depend on the main repo-level compose stack.

`deploy/pi-gateways/home-assistant-exporter` retains the implemented exporter deployment template, but the exporter is intentionally disabled in production with no mappings or source claims. Do not deploy or enable it without a separately approved source-authority change. The retired direct SolarAssistant and TP-Link/Kasa deployment stacks, containers, images, and Docker volumes have been removed. A checksum-verified SolarAssistant archive is retained outside Docker storage.

## Collector registry publishing and pins

All four active collectors follow the Davis pattern: CI builds and pushes one
semver image index for **linux/amd64 and linux/arm64**, using Buildx/QEMU on the
self-hosted AMD runner. Never build images on a Pi, hvo-docker or home-docker.
Publishing does not deploy or restart a collector. Govee BLE proxy publishing
is outside this repository's Dockerfile scope.

| Collector | Publish workflow / tag prefix | Required settings in its ignored `.env` |
|---|---|---|
| Davis | [Publish Davis image](../../.github/workflows/publish-davis.yml), `davis-v` | `HVO_DAVIS_IMAGE_VERSION`, `HVO_DAVIS_IMAGE_DIGEST` |
| JK BMS | [Publish JK BMS image](../../.github/workflows/publish-jkbms.yml), `jkbms-v` | `HVO_JKBMS_IMAGE_VERSION`, `HVO_JKBMS_IMAGE_DIGEST` |
| SmartShunt | [Publish SmartShunt image](../../.github/workflows/publish-smartshunt.yml), `smartshunt-v` | `HVO_SMARTSHUNT_IMAGE_VERSION`, `HVO_SMARTSHUNT_IMAGE_DIGEST` |
| EG4 | [Publish EG4 image](../../.github/workflows/publish-eg4.yml), `eg4-v` | `HVO_EG4_IMAGE_VERSION`, `HVO_EG4_IMAGE_DIGEST` |

1. Follow [CI publishing](../../docs/CONTAINER_PUBLISHING.md#pi-collector-ci-publishing)
   to publish a new version. Stable tags must be on main; a separately authorized
   prerelease tag can qualify a draft candidate. Each workflow also supports a
   `version` dispatch on main after that workflow is merged. It rejects duplicate
   versions and never moves `latest`.
2. Require a successful publish run. Verify the run's source SHA, both Linux
   architectures and the multi-arch **index** digest. From an approved build or
   operator workstation authenticated to the registry, repeat the read-only
   check (JK BMS example):

   ```bash
   image=registry.hualapaivalleyobservatory.org/hvo-jkbms
   version=0.0.0-ci.466.1
   digest=sha256:REPLACE_WITH_PUBLISHED_INDEX_DIGEST
   docker buildx imagetools inspect "$image@$digest"
   test "$(docker buildx imagetools inspect "$image:$version" --format '{{.Manifest.Digest}}')" = "$digest"
   ```

   Expect exactly linux/amd64 and linux/arm64; do not substitute a child-platform
   digest. The successful workflow performs the same registry readback and
   records the complete reference in its summary. Do not print credentials.
3. Record the previous immutable image/runtime reference before editing the
   existing ignored collector `.env`. Set its version and digest pair from that
   run, preserving device identity, mounted config/secrets and telemetry values.
   The checked-in `.env.example` placeholders are not published versions.
   Compose resolves to `registry.hualapaivalleyobservatory.org/hvo-<name>:<version>@<digest>`,
   requires both values, contains no `build:` and selects the target's native
   architecture from the same index.
4. Validate with `docker compose --env-file deploy/pi-gateways/jkbms/.env -f
   deploy/pi-gateways/jkbms/docker-compose.yml config --quiet`. For EG4 include
   each enabled device overlay. With separate pull authorization, use the same
   files and target context with `pull hvo-jkbms` (or the named collector).
   Authenticate using the approved credential handoff and password-stdin.
   Verify the selected architecture/RepoDigests without printing a full rendered
   environment. A pull alone leaves the running container unchanged.
5. Replacement requires its own rollout authorization and the collector's
   existing preflight/checkpoint/health/ingest gates. The deploy helper forces
   `--no-build` for Davis, JK BMS, SmartShunt and EG4, including when omitted.
   It synchronizes files and replaces containers; it is not a pull-only command.

For an authorized rollback, stop the candidate and preserve a new quiescent
checkpoint of the complete current outbox volume and later observations under
the [SQLite recovery contract](../../docs/gateways/sqlite-backup-and-rollback.md).
Restore the recorded previous version/digest pair in the existing `.env`, verify
the previous image's schema/payload compatibility, then pull and replace only
the named collector under its normal rollout gates. Keep the same Compose
project, `/app/data` named volume, device mappings and mounted files. If the
older binary cannot consume current data, use the contract's isolated recovery
volume and reconcile the post-checkpoint interval. Never overwrite/prune the
current volume, move a published tag or start a second acquisition owner.

## Runtime configuration without image rebuilds

All four collectors read a **non-secret runtime `.env` at process startup**.
Compose maps the daemon-host `*_REMOTE_RUNTIME_DIRECTORY` read-only to
`/app/config/runtime` and sets `HVO_EDGE_RUNTIME_ENV_FILE=/app/config/runtime/.env`.
The directory mount allows an operator to atomically replace `.env`; a single
file bind could retain the old inode. Docker must not create a missing host
directory automatically. The deploy helper stages the selected local `.env`
beside JSON/secrets, applies directory `0700` / file `0600`, and restores the
previous complete set if activation fails. This is future-rollout behavior;
#466 performs no deployment or restart.

The file supports flat `KEY=value` lines, `#`/`;` comment lines and double-quoted
values. It is parsed as data: **no `export`, sections, shell expressions,
variable expansion or command substitution**. Duplicate setting names, including
aliases such as `DAVIS_STATION_HOST` plus `Station__Host`, fail startup without
printing file contents. Keys containing `Password`, `Token`, `Secret`, `ApiKey`
or `ConnectionString` are rejected case-insensitively after ignoring separators,
including unknown flat credential keys. Neither key nor value appears in the
generic error. Keep secret-file references in mounted `gateway.json` and raw
credentials in mounted secret files. Ordinary single-underscore Compose
`*_SECRETS_DIRECTORY` path fields remain allowed ignored metadata.

Startup precedence is app defaults → mounted `gateway.json` → creation-time
environment → mounted runtime `.env`. The last provider maps supported
`Section__Key` names to .NET configuration, including indexed arrays such as
`JkBms__Devices__0__Alias`. `DAVIS_STATION_HOST` maps to `Station:Host` for existing
Davis env compatibility. Other Compose variables (pins, ports, paths and overlay
switches) are ignored by the app loader. The mounted JSON selector remains fixed.
Existing option validation and sole-source/device-identity rules still apply.
This precedence is intentional: a selected runtime setting wins over real
environment values from `docker run -e` or Compose `environment:`. Omitting or
removing a runtime key falls through to the container's original environment,
then JSON/app defaults. The loader does not synthesize deployment defaults.

Examples of optional app overrides (omit to retain environment/JSON/app defaults):

| Variable | Purpose | Default / requirement |
|---|---|---|
| `Outbox__BatchSize` | Delivery batch size | Optional; shared default 50, valid range 1–500 |
| `Station__SocketTimeoutSeconds` | Davis socket timeout | Optional; default 8 seconds, valid range 4–60 |
| `JkBms__HciAdapter` | Default JK BlueZ adapter | Optional; default `hci0`; preserve per-device ownership |
| `SmartShunt__Adapter` | Direct SmartShunt BlueZ adapter | Optional; default `hci0`; preserve paired ownership |

There are **no application command-line flags** in the four Compose files or
Dockerfile launchers to convert. `ENTRYPOINT ["dotnet", "<collector>.dll"]` is
the executable/assembly launch contract. Docker health checks' `CMD curl --fail`
and their fixed localhost 8080 paths belong to Docker, not .NET configuration;
retain `/health` for Davis/EG4 and `/health/live` for JK/SmartShunt. Container
port 8080, Production mode, `/app/data` and mounted-config/secret locations stay
aligned with those contracts. Dockerfile defaults such as `ASPNETCORE_URLS`,
`OUTBOX__DATABASEPATH` and `HVO_EDGE_CONFIG_FILE` are already configuration values,
not collector arguments. Avoid overriding durable paths or host/probe ports
without separately reviewing the complete runtime contract.

### Variable reference

Every active assignment in the checked-in env examples is documented below;
the examples also annotate each variable inline. `None` means no usable default:
replace the placeholder before validation or an authorized rollout.

| Common variable | Purpose | Default | Required |
|---|---|---|---|
| `OTEL_EXPORTER_OTLP_ENDPOINT` | Base URL for logs, traces and metrics | `http://192.168.1.238:4318` when unset/empty | No |
| `OTEL_EXPORTER_OTLP_PROTOCOL` | OTLP transport; `http/protobuf` or `grpc` | `http/protobuf` when unset/empty | No |
| `OTEL_SERVICE_NAME` | Telemetry service identity | `hvo-davis`, `hvo-jkbms`, `hvo-smartshunt`, `hvo-eg4` respectively when unset/empty | No |

These are **Compose/env-example defaults**, used during container creation.
The runtime loader supplies only values present in its file. Removing a runtime
setting exposes the original container environment; explicitly empty values
remain empty. In particular, an empty runtime base endpoint disables that base
exporter, while empty protocol/service values are not suitable overrides: use a
supported protocol and nonempty identity. For gRPC,
select the collector's configured gRPC listener (normally port 4317) together
with `grpc`. The former `OTEL_COLLECTOR_ENDPOINT` alias is no longer used by these
four Compose definitions; migrate to `OTEL_EXPORTER_OTLP_ENDPOINT`. Verify the
collector's LAN listener/bind and reachability before a future authorized rollout.

| Davis variable | Purpose | Default | Required |
|---|---|---|---|
| `DAVIS_HTTP_PORT` | Host HTTP publication | 5100 | No |
| `HVO_DAVIS_IMAGE_VERSION` | Published semver | None | Yes |
| `HVO_DAVIS_IMAGE_DIGEST` | Immutable multi-arch index digest | None | Yes |
| `DAVIS_STATION_HOST` | WeatherLink host / `Station:Host` | None | Yes |
| `DAVIS_CONFIG_FILE` | Workstation JSON for preflight/sync | `./gateway.json` | No |
| `DAVIS_SECRETS_DIRECTORY` | Workstation secret directory | `./secrets` | No |
| `DAVIS_REMOTE_CONFIG_FILE` | Absolute daemon-host JSON file | None; example `/home/roys/.local/share/hvo-edge/davis/gateway.json` | Yes |
| `DAVIS_REMOTE_SECRETS_DIRECTORY` | Absolute daemon-host secrets beside JSON | None; example `/home/roys/.local/share/hvo-edge/davis/secrets` | Yes |
| `DAVIS_REMOTE_RUNTIME_DIRECTORY` | Daemon-host startup `.env` directory | `/home/roys/.local/share/hvo-edge/davis/runtime` | No; must be beside JSON |

| JK BMS variable | Purpose | Default | Required |
|---|---|---|---|
| `JKBMS_HTTP_PORT` | Host HTTP publication | 5200 | No |
| `HVO_JKBMS_IMAGE_VERSION` | Published semver | None | Yes |
| `HVO_JKBMS_IMAGE_DIGEST` | Immutable multi-arch index digest | None | Yes |
| `JKBMS_CONFIG_FILE` | Workstation JSON for preflight/sync | `./gateway.json` | No |
| `JKBMS_SECRETS_DIRECTORY` | Workstation secret directory | `./secrets` | No |
| `JKBMS_REMOTE_CONFIG_FILE` | Absolute daemon-host JSON file | None; example `/home/roys/.local/share/hvo-edge/jkbms/gateway.json` | Yes |
| `JKBMS_REMOTE_SECRETS_DIRECTORY` | Absolute daemon-host secrets beside JSON | None; example `/home/roys/.local/share/hvo-edge/jkbms/secrets` | Yes |
| `JKBMS_REMOTE_RUNTIME_DIRECTORY` | Daemon-host startup `.env` directory | `/home/roys/.local/share/hvo-edge/jkbms/runtime` | No; must be beside JSON |

| SmartShunt variable | Purpose | Default | Required |
|---|---|---|---|
| `SMARTSHUNT_HTTP_PORT` | Host HTTP publication | 5400 | No |
| `HVO_SMARTSHUNT_IMAGE_VERSION` | Published semver | None | Yes |
| `HVO_SMARTSHUNT_IMAGE_DIGEST` | Immutable multi-arch index digest | None | Yes |
| `SMARTSHUNT_CONFIG_FILE` | Workstation JSON for preflight/sync | `./gateway.json` | No |
| `SMARTSHUNT_SECRETS_DIRECTORY` | Workstation secret directory | `./secrets` | No |
| `SMARTSHUNT_REMOTE_CONFIG_FILE` | Absolute daemon-host JSON file | None; example `/home/roys/.local/share/hvo-edge/smartshunt/gateway.json` | Yes |
| `SMARTSHUNT_REMOTE_SECRETS_DIRECTORY` | Absolute daemon-host secrets beside JSON | None; example `/home/roys/.local/share/hvo-edge/smartshunt/secrets` | Yes |
| `SMARTSHUNT_REMOTE_RUNTIME_DIRECTORY` | Daemon-host startup `.env` directory | `/home/roys/.local/share/hvo-edge/smartshunt/runtime` | No; must be beside JSON |

| EG4 variable | Purpose | Default | Required |
|---|---|---|---|
| `EG4_HTTP_PORT` | Host HTTP publication | 5600 | No |
| `HVO_EG4_IMAGE_VERSION` | Published semver | None | Yes |
| `HVO_EG4_IMAGE_DIGEST` | Immutable multi-arch index digest | None | Yes |
| `EG4_CONFIG_FILE` | Workstation JSON for preflight/sync | `./gateway.json` | No |
| `EG4_SECRETS_DIRECTORY` | Workstation secret directory | `./secrets` | No |
| `EG4_REMOTE_CONFIG_FILE` | Absolute daemon-host JSON file | None; example `/home/roys/.local/share/hvo-edge/eg4/gateway.json` | Yes |
| `EG4_REMOTE_SECRETS_DIRECTORY` | Absolute daemon-host secrets beside JSON | None; example `/home/roys/.local/share/hvo-edge/eg4/secrets` | Yes |
| `EG4_REMOTE_RUNTIME_DIRECTORY` | Daemon-host startup `.env` directory | `/home/roys/.local/share/hvo-edge/eg4/runtime` | No; must be beside JSON |
| `EG4_DEVICE_0_PORT` | First inverter stable HID node | `/dev/hvo/eg4-6500ex-a` | No |
| `EG4_MPPT_0_ENABLED` | Select MPPT overlay; match JSON enabled state | `false` | No |
| `EG4_MPPT_0_PORT` | Stable MPPT adapter identity | None | Only when MPPT enabled |
| `EG4_DEVICE_1_ENABLED` | Select second-inverter overlay; match JSON | `false` | No |
| `EG4_DEVICE_1_PORT` | Second inverter stable HID node | `/dev/hvo/eg4-6500ex-b` | Only when second inverter enabled |

### Applying a future authorized setting change

First adoption of the new loader/mount needs a newly published image and an
authorized container replacement. Existing one-off images and the earlier
`0.0.0-ci.466.1` qualification images do not contain this added loader. Preserve
their exact source/digest evidence; do not retag them.

After adoption, edit the actual daemon-host `runtime/.env` (or atomically replace
it within the mounted directory), preserve mode `0600`, and synchronize the
corresponding ignored workstation `.env` so a later deployment will not undo the
change. With explicit restart authorization, restart only the existing named
collector, then verify its health, source freshness, protected diagnostics,
outbox and telemetry. This uses the same image/container/volumes; no image build
or deployment helper is needed for an app-setting update. Files are not hot-loaded.

Editing **only** the Compose workstation `.env` followed by `docker restart`
does not update a container's environment or its remote runtime file. Image pins,
ports, mount paths, device mappings, privilege and overlay selection are Docker
creation settings and still require an authorized no-build recreation. The
deployment helper also synchronizes files and recreates containers; never run it
as a restart-only or pull-only check. No command here authorizes a live restart.

## Davis deployment notes

Davis uses the [versioned registry image and pull-only handoff](davis/README.md).
Its current acquisition owner is hvo-docker (amd64); the stopped Pi is a possible
arm64 fallback only through an authorized sole-owner cutover. Set
`HVO_DAVIS_IMAGE_VERSION` and `HVO_DAVIS_IMAGE_DIGEST` from a successful CI publish
before rendering or pulling Compose. Never build on hvo-docker or home-docker.

- The move from devpi5 to hvo-docker is tracked in
  [#462](https://github.com/HualapaiValley/HVO.WebSite/issues/462). hvo-docker is
  the acquisition authority; the preserved Pi must stay stopped. Reuse this
  directory's Compose file as project `davis`. The existing one-off amd64 image
  stays in service until Kepler/Roy cuts over to the registry image. The
  [hvo-docker runbook](../hvo-docker/davis/README.md) records Roy's final
  core-only rollback rule, 15-minute collection/ingest gate and diagnostic-only
  treatment of CWOP/WU/MQTT failures.
  Never start collectors on both hosts. Keep the Pi container/image/volume and
  `outbox.db.old`; exclude that old database from the transfer.
- Weather Underground publication is disabled by default in `davis/gateway.json.example` and targets station `KAZKINGM12` on a five-second cadence with a two-second request timeout.
- The upload station key is stored in Key Vault as `WeatherUnderground--StationKey`. It is materialized as the ignored `davis/secrets/weather-underground-station-key` file only when the ignored local Davis `gateway.json` enables publication.
- `WeatherUnderground--ApiKey` is a separate query credential and is never retrieved or mounted for PWS upload. No Weather Underground credential belongs in `.env` or Compose environment entries.
- The existing secrets-directory bind remains read-only at `/run/secrets`. Davis preflight validates the complete section and requires a non-empty station-key file only when enabled, before SSH synchronization or container changes.
- Run `bash tools/validate-davis-weather-underground-deployment.sh` for deterministic disabled/enabled, secret, Compose, ignore, and API-key-isolation checks.
- Follow `docs/gateways/davis-vantage-pro2/weather-underground-deployment.md` for protocol mapping, diagnostics, enablement, verification, and rollback.

## Common workflow

1. Initialize `.env` and `gateway.json` only on first install, refusing existing
   files/symlinks. For example, from the repository root for Davis:

```bash
set -euo pipefail
test ! -e deploy/pi-gateways/davis/.env && test ! -L deploy/pi-gateways/davis/.env
test ! -e deploy/pi-gateways/davis/gateway.json && test ! -L deploy/pi-gateways/davis/gateway.json
(umask 077; set -o noclobber; cat deploy/pi-gateways/davis/.env.example > deploy/pi-gateways/davis/.env)
(umask 077; set -o noclobber; cat deploy/pi-gateways/davis/gateway.json.example > deploy/pi-gateways/davis/gateway.json)
chmod 600 deploy/pi-gateways/davis/.env deploy/pi-gateways/davis/gateway.json
```

2. Keep device endpoints such as `DAVIS_STATION_HOST` in `.env`; fill mounted gateway configuration and create the required files under the configured local secrets directory.
3. Set the absolute `*_REMOTE_CONFIG_FILE` and `*_REMOTE_SECRETS_DIRECTORY` paths from `.env.example`. For SSH Docker contexts, the deploy script copies the local files to those daemon-host paths with owner-only permissions before Compose starts the container.
   The `*_REMOTE_RUNTIME_DIRECTORY` must be `runtime` beside that remote JSON;
   the helper stages the selected local `.env` there as the startup file.
   Set the collector's required image version/digest pair using the registry
   procedure above before Compose validation. Preserve existing `.env` files.
4. Validate syntax and expected non-secret mount/probe facts. `config --quiet`
   alone does not establish effective values. Shell exports override `--env-file`:

```bash
set -euo pipefail
docker compose --env-file deploy/pi-gateways/davis/.env \
  -f deploy/pi-gateways/davis/docker-compose.yml config --quiet
docker compose --env-file deploy/pi-gateways/davis/.env \
  -f deploy/pi-gateways/davis/docker-compose.yml config --format json |
  jq -e '.services["hvo-davis"] as $s |
    $s.environment.DOTNET_ENVIRONMENT == "Production" and
    any($s.volumes[]; .target == "/app/config/gateway.json" and .read_only == true) and
    any($s.volumes[]; .target == "/run/secrets" and .read_only == true) and
    any($s.volumes[]; .target == "/app/data" and .type == "volume") and
    $s.healthcheck.test[-1] == "http://localhost:8080/health"' >/dev/null
```

5. Run the named gateway's deployment preflight/dry run after device identities,
   mounts, checkpoint and secret files are satisfied. `--dry-run` still validates
   local prerequisites, but prints rather than executes SSH/container writes.
   Inspect that output for the intended daemon-host paths.

```bash
./scripts/deploy-pi-gateway.sh --dry-run --context hvo-docker --pull --no-build davis
```

6. Only with rollout authorization, deploy the selected collector on its owning
   host. The first Davis host migration must follow the stopped-copy runbook
   above before this ordinary deployment workflow is applicable:

```bash
# Davis: only on the separately authorized target after the registry handoff.
./scripts/deploy-pi-gateway.sh --context hvo-docker --pull --no-build davis
./scripts/deploy-pi-gateway.sh --context devpi5 --pull --no-build eg4
./scripts/deploy-pi-gateway.sh --context devpi5 --pull --no-build jkbms
./scripts/deploy-pi-gateway.sh --context devpi5 --pull --no-build smartshunt
```

Deploy only the named active collector being changed. The HA exporter is not a production target.

To verify deployed endpoints after rollout:

```bash
./scripts/check-deployments.sh
```

## Current telemetry status

- All gateway hosts emit compact structured logs to stdout and conditionally export logs, traces, and metrics to the shared collector.
- The collector is Pi-reachable at `http://192.168.1.238:4318` using `http/protobuf` only when the observability stack sets `HVO_OBSERVABILITY_BIND_ADDRESS=192.168.1.238`; verify the resolved bind and connectivity before a rollout using each stack's `OTEL_EXPORTER_OTLP_ENDPOINT` default or override.
- Use `docker logs <container>` for short-term local diagnostics and Grafana/Loki for centralized logs. Gateway applications do not create `/app/logs` or manage local log files.
- Every gateway has a nominal 34 MB local budget: three compressed 10 MB files plus a 4 MB non-blocking buffer. When full, new stdout records are dropped rather than blocking device polling.
- Core dumps are disabled in every gateway container. The deploy script verifies both logging and core policies after recreation.
- Direct OTLP logging buffers at most 5,000 events and retries for ten minutes. Longer outages can lose central records; use bounded local `docker logs` for incident reconstruction.
- Davis, JK BMS, EG4, and SmartShunt retain independent acquisition, diagnostics, and shared-outbox forwarding when OTLP is unavailable.

## JK BMS deployment notes

- The JK BMS container needs `privileged: true` and the host D-Bus mount `/run/dbus/system_bus_socket` so BlueZ access works from Docker on the Pi.
- Copy `gateway.json.example` to ignored `gateway.json`; mount diagnostics, central-ingest, and optional MQTT credentials as files in the ignored `secrets` directory.
- The Compose project, service name, and `jkbms-outbox` volume are unchanged so the production outbox remains attached during cutover.
- Use `JKBMS_HCI_ADAPTER=hci0` as the default unless a specific Pi host proves another adapter is more stable.
- The deployment preflight validates the mounted vNext identity/outbox contract and required secrets before container recreation.
- Follow `docs/gateways/jkbms/deployment-and-endurance.md`; never run the physical endurance check as part of routine PR validation.

## SmartShunt deployment notes

- The paired direct public-GATT collector is the sole acquisition authority and central writer. Home Assistant receives only the collector's read-only MQTT projection; do not enable an HA/ESPHome acquisition or exporter mapping for this device.
- Copy `gateway.json.example` to ignored `gateway.json`; mount diagnostics, central-ingest, and optional MQTT credentials as separate files in the ignored `secrets` directory.
- Root Compose also requires those mounted files. The API key remains exclusively in `secrets/central-ingest-api-key`.
- The Compose project, service name, and `smartshunt-outbox` volume remain unchanged, preserving queued legacy summaries during migration to the shared outbox schema.
- The deployment preflight validates direct authority, source identity, the public-GATT MAC address, shared outbox path/type, and required secret files before SSH synchronization and recreation.
- Follow `docs/gateways/victron-smartshunt.md` for exactly-one-owner cutover, rollback, and the optional bounded `TestCategory=Live` check.

## Recommended workflow

1. Develop website/API changes in the repo devcontainer and validate them against `hvo-docker`.
2. Deploy the website/API update to `hvo-docker` when the gateway contract is ready.
3. Deploy the gateway container to the Pi with `HVO_WEBSITE_PUBLIC_BASE_URL` pointed at `http://hvo-docker.hvo.lan`.
4. Keep the public Azure proxy path out of gateway ingestion so local telemetry does not depend on external routing.

## Internal rollout sequence

1. Confirm the website container on `hvo-docker` can read secrets from `https://hvo-central-kv.vault.azure.net/`.
2. Store the raw API keys in Key Vault using the secret names listed above.
3. Restart or roll a new website revision so startup seeding runs again.
4. Verify the keys against the live internal website API.
5. Synchronize the approved gateway key into its actual mounted
   `secrets/central-ingest-api-key`, with the separate diagnostics credential and
   any target-specific files; perform preflight, deploy and verify continuity.

This sequence is an authorized rollout, not a hardware-free development quick
start. Preserve exactly one acquisition authority and central writer per source.
#352 governs any SmartShunt migration; #385 owns the permanent Govee proxy path,
and #320 governs HA-owned history/source authority. Keep the exporter disabled
without approved mappings/claims. Use the
[SQLite checkpoint/restore contract](../../docs/gateways/sqlite-backup-and-rollback.md)
and retain retired recovery archives.

## Current local ports

- Davis headless health and protected diagnostics: `http://hvo-docker:5100`
- JK BMS headless health and protected diagnostics: `http://<pi-host>:5200`
- SmartShunt headless health and protected diagnostics: `http://<pi-host>:5400`
- EG4 headless health and protected diagnostics: `http://<pi-host>:5600`

Ports 5300 and 5500 are no longer assigned to direct SolarAssistant or TP-Link/Kasa services. Port 5700 is reserved by the disabled HA exporter template and has no production service.

## EG4 deployment notes

- Follow `docs/gateways/eg4/deployment-and-shadow-validation.md` before rollout.
- Copy `gateway.json.example` to ignored `gateway.json`; mount diagnostics, central-ingest, and MQTT credentials as files in the ignored `secrets` directory.
- Map only stable `/dev/hvo/eg4-6500ex-*` HID nodes and the verified MPPT `/dev/serial/by-id/...` path; never map enumerated `/dev/hidrawN` or `/dev/ttyUSBN` names.
- The MPPT serial device remains isolated by default. `EG4_MPPT_0_ENABLED=true` selects `docker-compose.mppt.yml`, which maps only that stable path and permits only the fixed unit-1 function-`0x03` read of registers 200-217.
- The second-inverter overlay remains selected only when `EG4_DEVICE_1_ENABLED=true` and both stable inverter HID nodes exist.
- Production simulation and all command/write paths are disabled. The gateway publishes battery branches, independent MPPT detail, and read-only inverter AC/load/temperature/status detail.
- Direct current state is projected to Home Assistant through MQTT Discovery. MQTT is not the canonical historical-delivery path.
- Set `HVO_CHECK_EG4=true` when `check-deployments.sh` should require EG4 health.
