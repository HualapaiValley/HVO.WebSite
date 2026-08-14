# Weather Underground Publication

The Davis edge collector can publish its latest merged LOOP observation to Weather Underground station `KAZKINGM12`. This is a best-effort current-state projection. It does not open another WeatherLink IP connection, poll the console independently, enter the durable outbox, or become canonical HVO history.

## Protocol And Rate Evidence

Weather Underground's [PWS Upload Protocol](https://support.weather.com/s/article/PWS-Upload-Protocol?language=en_US) documents the HTTPS upload endpoint and query field contract. The rapid-fire form uses `action=updateraw`, `realtime=1`, and `rtfreq` to describe the update frequency. HVO uses `rtfreq=5` with a deterministic five-second publish interval. Each request has a two-second timeout so at most two attempts plus bounded backoff fit within the cadence and cannot create overlapping sends.

The configured endpoint is:

```text
https://rtupdate.wunderground.com/weatherstation/updateweatherstation.php
```

The station ID and upload credential are sent as the protocol's `ID` and `PASSWORD` query parameters. The full URI must never be logged, included in diagnostics, or copied into incident notes because `PASSWORD` contains the station key.

`HVO.Weather.WeatherUndergroundFormatter` 1.0.0 was evaluated for reuse. It was not reused because its contract does not cover the required query-component percent encoding or the rapid-fire `realtime`/`rtfreq` fields. Scheduling, HTTP transport, timeout/failure isolation, secrets, and diagnostics also remain responsibilities of the Davis application.

## Field Mapping

Values retain the Davis/HVO normalized units expected by the PWS protocol. Optional values are omitted when the merged LOOP observation does not contain them; zero is never invented for a missing sensor.

| Merged Davis value | PWS field | Unit or format |
|---|---|---|
| Observation UTC time | `dateutc` | UTC timestamp |
| Outdoor temperature | `tempf` | degrees F |
| Outdoor humidity | `humidity` | percent |
| Indoor temperature | `indoortempf` | degrees F |
| Indoor humidity | `indoorhumidity` | percent |
| Wind direction | `winddir` | degrees |
| Wind speed | `windspeedmph` | mph |
| Two-minute average wind speed | `windspdmph_avg2m` | mph |
| Ten-minute gust | `windgustmph_10m` | mph |
| Ten-minute gust direction | `windgustdir_10m` | degrees |
| Dew point | `dewptf` | degrees F |
| Hourly rain | `rainin` | inches |
| Daily rain | `dailyrainin` | inches |
| Corrected barometer | `baromin` | inHg |
| Solar radiation | `solarradiation` | W/m2 |
| UV index | `UV` | index |

The request also carries non-measurement protocol fields `ID=KAZKINGM12`, `action=updateraw`, `realtime=1`, and `rtfreq=5`.

## Configuration And Credentials

The tracked example is disabled by default:

```json
"WeatherUnderground": {
  "Enabled": false,
  "StationId": "KAZKINGM12",
  "IntervalSeconds": 5,
  "RequestTimeoutSeconds": 2,
  "StationKeySecret": "weather-underground-station-key"
}
```

The upload credential is the Weather Underground **station key**. Azure Key Vault stores it as `WeatherUnderground--StationKey`; `scripts/sync-secrets-from-keyvault.sh --apply` writes it to the ignored local file `deploy/pi-gateways/davis/secrets/weather-underground-station-key` only when the ignored local Davis `gateway.json` exists and has `WeatherUnderground.Enabled=true`. The existing secrets directory is mounted read-only at `/run/secrets`, so the application resolves `/run/secrets/weather-underground-station-key`.

`WeatherUnderground--ApiKey` is a separate query API credential. The PWS upload protocol does not use it. The sync script must never retrieve it, the Davis deployment must never materialize it, and the publisher must never send it. Neither credential belongs in `.env`, Compose environment entries, tracked JSON, command output, logs, diagnostics, or exception text.

## Disabled-First Rollout

1. Copy the tracked `gateway.json.example` to the ignored Davis `gateway.json` if needed, leave `WeatherUnderground.Enabled` set to `false`, and do not create a placeholder key.
2. Run `bash tools/validate-davis-weather-underground-deployment.sh` and `./scripts/deploy-pi-gateway.sh --dry-run --context devpi5 davis`.
3. Deploy the disabled build with `./scripts/deploy-pi-gateway.sh --context devpi5 davis`. Verify Davis acquisition, Home Assistant MQTT, central outbox delivery, and protected diagnostics remain healthy.
4. Change only the ignored local `gateway.json` to `WeatherUnderground.Enabled=true`.
5. Run `./scripts/sync-secrets-from-keyvault.sh --apply`. Confirm the station-key file exists and has mode `0600` without printing its contents. Do not retrieve or create a Weather Underground API-key file.
6. Re-run the Davis deployment dry run. The preflight must reject an absent/empty station-key file, a station other than `KAZKINGM12`, a cadence other than five seconds, an unbounded timeout, or a different secret filename before SSH synchronization.
7. Deploy Davis, then verify the container remains healthy, LOOP and MQTT freshness continue, the outbox drains, and Weather Underground reports fresh observations near the expected five-second cadence.
8. Observe at least several publish intervals. Confirm diagnostics show advancing attempts/successes without any URI or credential disclosure.

## Diagnostics

Protected Davis diagnostics expose the Weather Underground enabled state, last observation UTC, last attempt UTC, last success UTC, consecutive failure count, and sanitized last error. They must not expose the station key, query API key, `PASSWORD`, or full request URI.

For an enabled publisher, verify:

- last attempt advances approximately every five seconds when a current merged reading exists;
- last success remains fresh during normal service;
- consecutive failures return to zero after recovery;
- a timeout, DNS error, HTTP rejection, or Weather Underground outage changes only sanitized delivery health;
- Davis LOOP acquisition, MQTT projection, and central outbox forwarding continue through publication failures;
- recovery publishes the latest eligible reading and does not replay a stale queue.

Use `docker --context devpi5 logs --since 10m hvo-davis` and the protected `/diagnostics/status` endpoint. Search by station ID and outcome only. Never enable HTTP client URI/query logging for this publisher.

## Rollback

1. Set `WeatherUnderground.Enabled=false` in the ignored local Davis `gateway.json`.
2. Run the Davis deployment dry run and deploy the disabled configuration.
3. Verify Weather Underground attempts stop while Davis collection, MQTT, outbox drainage, and central timestamps remain current.
4. Leave the ignored local station-key file in place for a temporary rollback unless secret removal or rotation is explicitly required. Disabling prevents its use; routine sync does not delete operator-managed secret files.
5. If exposure is suspected, disable publication first, rotate `WeatherUnderground--StationKey` in Key Vault and Weather Underground, run secret sync only after the replacement is authoritative, and redeploy. Do not include the old or new value in incident output.

Rollback does not require restoring an image, database, or outbox volume because Weather Underground is an independent, non-durable projection.
