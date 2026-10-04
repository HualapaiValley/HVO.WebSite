# Victron SmartShunt Direct Collector

## Authority Decision

Issue #326 selects the existing validated paired public-GATT session as the sole SmartShunt acquisition authority. There is no validated HA/ESPHome evidence for the complete required field set, pairing stability, or update cadence. vNext therefore contains no HA acquisition, private-GATT enrichment, write/sync path, local UI, or competing central writer.

Home Assistant is presentation only. The collector publishes read-only current state and availability through `HVO.Edge.HomeAssistant.Mqtt`; HVO-owned MQTT entities must remain excluded from the HA WebSocket exporter.

## Live Contract

The collector owns one paired BLE connection through BlueZ and the public `6597...` GATT characteristics. Notifications and initial reads update one in-memory latest sample; the public keepalive is sent every configured interval. The worker samples that state every `SampleIntervalSeconds`, rejects samples older than `SampleStaleAfterSeconds`, publishes current HA state, and creates a durable observation every `SnapshotIntervalSeconds`.

| Field | Public UUID | Encoding | Update/storage semantics |
|---|---|---|---|
| Voltage | `6597ed8d-...` | signed hundredths V | Current HA state and central power summary |
| Current | `6597ed8c-...` | signed thousandths A | Source-native: positive charging, negative discharging |
| Power | `6597ed8e-...` | signed W | Source-native: positive charging, negative discharging |
| State of charge | `65970fff-...` | unsigned hundredths % | Current HA state and central power summary |
| Consumed Ah | `6597eeff-...` | signed tenths Ah | Current HA state and typed SmartShunt detail |
| Remaining time | `65970ffe-...` | unsigned minutes | Current HA state and typed SmartShunt detail; `ffff` means unavailable |
| Starter voltage | `6597ed7d-...` | signed hundredths V | Typed SmartShunt detail when device-available |
| Temperature | `65970383-...` | signed deg C | Typed SmartShunt detail when device-available |
| Device availability | session/sample state | boolean | HA retained availability plus standard health diagnostics |

The central `PowerReading` remains the summary contract. `SmartShuntDetailPayload` and `v9.SmartShuntDetailSnapshot` carry consumed Ah, remaining minutes, starter voltage, and temperature independently. Both payloads share source/device/timestamp identity. The local outbox persists one `com.hvo.smartshunt.observation.v1` bundle and marks it sent only after the transactional protected endpoint returns strict per-record accounting.

## Runtime And Deployment

- Configuration: read-only `/app/config/gateway.json`.
- Secrets: individual files under read-only `/run/secrets`.
- Durable state: `/app/data/outbox.db` on the unchanged `smartshunt-outbox` Compose volume.
- Liveness: `GET /health/live`.
- Readiness: `GET /health` and `GET /health/ready`.
- Protected diagnostics: `/diagnostics/health`, `/diagnostics/status`, `/diagnostics/outbox`, and `/diagnostics/outbox/settings`.
- Central endpoint: `POST /api/v1/power/smartshunt-observations/batch`, protected by `PowerIngest` and an exact source claim. Each envelope persists summary and detail atomically.

The website must configure `Seeding:SmartShuntApiKey` and `Seeding:SmartShuntSourceId`. In Azure Key Vault these are `Seeding--SmartShuntApiKey` and `Seeding--SmartShuntSourceId`. The source value must exactly match `SmartShunt:SourceId`; a broad power-ingest key is intentionally forbidden from this endpoint.

Legacy `smartshunt.reading` and `com.hvo.smartshunt.reading.v1` rows are converted in place to typed bundles before the shared outbox initializer runs. Existing summary fields and pending/sent status are preserved; unavailable historical detail remains null. HTTP 401/403 are transient so replacing a credential or correcting source authority can recover queued records.

## Exactly-One-Owner Cutover

This is the historical #326 direct-collector vNext cutover. Future native-HA
replacement is tracked separately by [#352](https://github.com/HualapaiValley/HVO.WebSite/issues/352)
and its parity, exporter and soak gates; it is not this procedure's deployment target.

1. Deploy the website migration and protected observation endpoint first. Verify the dedicated seeded API key owns the configured source.
2. Follow the [canonical SQLite backup contract](sqlite-backup-and-rollback.md): record the current immutable image/runtime contract, inspected volume names, outbox counts and latest central source timestamp. Prepare the utility image and a durable operator-workstation backup directory before shutdown. The `smartshunt_smartshunt-outbox` volume is owned by the `devpi5` Docker daemon; streamed archives and checksums are owned by the workstation, not a remote `/tmp` bind.
3. Confirm no HA/ESPHome SmartShunt integration is acquiring the device and no HA exporter mapping owns `smartshunt-main`.
4. Stop the legacy SmartShunt container. Confirm it no longer owns the Bluetooth connection or writes centrally, and prove no process writes the volume. Archive the entire quiescent volume, retain a durable checksum-verified copy, and verify SQLite integrity plus a disposable restore before calling it a usable checkpoint. Preserve the current named volume; do not proceed with an unqualified checkpoint.
5. Place `gateway.json` and separate secrets on the deployment workstation, then run `./scripts/deploy-pi-gateway.sh --context devpi5 smartshunt`. The script validates and synchronizes mounted files over the Docker context's SSH endpoint.
6. Verify `/health`, protected diagnostics, MQTT availability/state, outbox drainage, and one summary/detail row for the same source timestamp. Reconcile counts before promotion:

```bash
docker --context devpi5 compose --env-file deploy/pi-gateways/smartshunt/.env \
  -f deploy/pi-gateways/smartshunt/docker-compose.yml ps
curl -fsS -H "X-Api-Key: $(<deploy/pi-gateways/smartshunt/secrets/diagnostics-api-key)" \
  http://devPi5:5400/diagnostics/outbox
```
7. Keep the legacy container stopped. Do not run shadow central writes.

## Rollback

1. Stop vNext before starting any legacy process.
2. Follow the [canonical rollback preservation and reconciliation steps](sqlite-backup-and-rollback.md#preserve-current-state-during-rollback). Qualify a new archive of the stopped current volume and keep that volume intact with every post-checkpoint observation. Never use `docker compose down -v` or restore over its files.
3. If the preserved binary can read the current schema, use the current volume. Otherwise restore the qualified pre-cutover archive to a new recovery volume with an explicitly reviewed mount override; retain the original volume and reconcile the post-checkpoint interval with central history before forwarding. An old checkpoint does not recover newer records by itself.
4. Use the immutable image/runtime contract recorded in the durable checkpoint manifest. Start exactly one legacy collector only after schema/source compatibility and the post-checkpoint recovery plan are established. Confirm sole Bluetooth ownership, strict central accounting, queue drain and fresh central timestamps; report incomplete history recovery honestly.
5. Keep HA/ESPHome acquisition/export disabled throughout rollback.

## Bounded Live Validation

Routine CI does not access hardware. Protocol, sign mapping, options, outbox, migration, hosting, MQTT, and API tests are non-live. After deployment, run one bounded two-minute validation only when physical access is explicitly intended:

```bash
SMARTSHUNT_LIVE_ADDRESS=AA:BB:CC:DD:EE:FF \
dotnet test tests/HVO.Hardware.VictronSmartShunt.Tests \
  --filter "TestCategory=Live"
```

Pass criteria: a paired public-GATT session returns voltage, current, power, and SOC within two minutes. Then use deployed diagnostics/MQTT/API observations to confirm consumed Ah, remaining time when device-available, availability transitions, update cadence, and source-native signs. The test performs no writes beyond the validated public keepalive.
