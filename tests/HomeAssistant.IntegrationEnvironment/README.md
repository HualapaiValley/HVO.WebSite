# Home Assistant Integration Environment

This disposable environment validates edge MQTT behavior without physical devices or production Home Assistant state. It pins Home Assistant Core, Mosquitto, and WireMock versions in `docker-compose.yml` and uses fresh named volumes on every run.

Run the complete integration category from the repository root:

```bash
./tools/run-home-assistant-integration-tests.sh
```

The runner performs Home Assistant onboarding through supported APIs, validates HA YAML, configures the MQTT integration, validates normal and outage responses from the fake central ingest endpoint, runs all `TestCategory=Integration` tests, revokes the temporary refresh token, and removes containers, networks, volumes, and derived images even when a test fails.

No credentials or generated Home Assistant `.storage` files are retained. Update upstream image pins deliberately and run this command before merging the update.

MQTT command entities remain intentionally excluded until the separate safety contract defines command authorization, idempotency, stale-command rejection, and acknowledgement semantics. Source-specific physical-device behavior belongs in `TestCategory=Live` tests.
