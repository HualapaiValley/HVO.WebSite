# Davis registry image handoff

The active Davis collector is on **hvo-docker (amd64)** after
[#462](https://github.com/HualapaiValley/HVO.WebSite/issues/462). The preserved
devpi5 (arm64) collector is stopped with restart disabled. Keep exactly one
acquisition owner; pulling an image does not authorize starting the Pi or
replacing the active container. Kepler/Roy owns the registry-image cutover.

[Publishing](../../../docs/CONTAINER_PUBLISHING.md#davis-ci-publishing) builds
`registry.hualapaivalleyobservatory.org/hvo-davis` in CI for linux/amd64 and
linux/arm64. Never build on hvo-docker or home-docker. The previous #463
`compose.build-host.yml` workaround is retired; do not use host-network builds.

## Select and pull a verified release

1. Require a successful **Publish Davis image** run. Copy its version and
   multi-arch **index** digest into the existing ignored Davis `.env` as
   `HVO_DAVIS_IMAGE_VERSION` and `HVO_DAVIS_IMAGE_DIGEST`. Preserve every existing
   device, path and telemetry setting. First-install placeholders in
   [.env.example](.env.example) are not published versions.
2. Authenticate the target Docker client using the approved registry credential
   handoff and password-stdin. Do not print credentials or the rendered Compose
   environment. The existing registry login hostname is
   `registry.hualapaivalleyobservatory.org`.
3. On the selected target, in its repository checkout, validate and pull only:

   ```bash
   docker compose --env-file deploy/pi-gateways/davis/.env \
     -f deploy/pi-gateways/davis/docker-compose.yml -p davis config --quiet
   docker compose --env-file deploy/pi-gateways/davis/.env \
     -f deploy/pi-gateways/davis/docker-compose.yml -p davis pull hvo-davis
   ```

   [Compose](docker-compose.yml) requires both the version and digest, has no
   `build:` section and lets the target choose its native platform. The amd64
   host and an arm64 Pi use the same index digest. Independently inspect the
   selected image's architecture and RepoDigests after pulling; record the exact
   run, reference, host and architecture in #464. A pull leaves the running
   collector unchanged. It does not verify application health or complete cutover.

## Cutover boundary

This issue's implementation performs no deployment, restart, volume change,
station probe or rollback. The owner must schedule replacement separately,
preserving the existing Compose project `davis`, named `davis_davis-outbox`
volume, mounted configuration/secrets and MTU 1450 runtime network. Preserve
both SQLite databases and later observations under the
[quiescent checkpoint contract](../../../docs/gateways/sqlite-backup-and-rollback.md).
The [gateway operations guide](../../../docs/GATEWAY_OPERATIONS.md) owns health,
diagnostics and delivery checks. The live #462 record and draft #463 own the
core-only rollback rule; optional CWOP/WU/MQTT failures alone do not authorize
rollback or a second acquisition owner.

For an authorized future rollout, `deploy-pi-gateway.sh --pull --no-build davis`
uses the pinned registry image and existing configuration preflight. The Davis
target now forces `--no-build` even when that flag is omitted. The script also
synchronizes mounted files and starts/replaces containers, so it is not a
pull-only check and must wait for Kepler/Roy's cutover authorization.
