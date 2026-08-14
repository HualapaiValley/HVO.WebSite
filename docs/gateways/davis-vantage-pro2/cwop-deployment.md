# CWOP Publication

The Davis collector can publish the latest shared merged LOOP observation and persisted station position to CWOP station `DW4515`. This best-effort publisher does not connect to the Davis console, queue packets, or affect MQTT, Weather Underground, or central outbox delivery. It is disabled by default.

## Protocol

The implementation follows the APRS-IS [connection specification](https://www.aprs-is.net/Connecting.aspx): it reads the server banner, sends `user ... pass ... vers ...`, validates the login response according to the configured passcode mode, and sends one TNC2 packet using the `TCPIP*` path. Every line uses CRLF and remains below the 512-byte APRS-IS limit. The complete position/weather payload follows the APRS 1.0 weather format and the [APRS 1.2 weather clarification](https://www.aprs.org/aprs12/weather-new.txt). It includes the observation's UTC `DDHHMMz` timestamp.

Generic APRS-IS documentation describes `-1` as receive-only, while the established CWOP upload convention uses `pass -1`: the server acknowledges the CWOP station as `unverified`, after which the client must still send its weather packet. The client permits an unverified acknowledgement only when the configured login uses exactly `pass -1`; malformed responses, station mismatches, and explicit rejection remain failures. If CWOP supplies a registered passcode, store it in Key Vault as `Cwop--AprsIsPasscode`, run `./scripts/sync-secrets-from-keyvault.sh --apply` to materialize `/run/secrets/cwop-passcode`, set `PasscodeSecret` to `cwop-passcode`, and clear `Passcode`. Never commit or log the value.

## Mapping

| Davis source | APRS field |
|---|---|
| Observation UTC | `@DDHHMMz` |
| Station latitude/longitude | uncompressed position with `_` weather symbol |
| Station elevation | `/A=` feet |
| Current wind direction and two-minute average speed | `ddd/sss` |
| Ten-minute gust | `g` mph |
| Outside temperature | `t` degrees F |
| Hour rain | `r` hundredths of an inch |
| Rolling 24-hour rain | `p` hundredths of an inch |
| Daily rain | `P` hundredths of an inch |
| Outside humidity | `h` percent (`00` means 100%) |
| Corrected barometer | `b` tenths of a millibar |
| Solar radiation | `L`/`l` W/m2 |

Unavailable required wind/temperature values use APRS dots; unavailable optional fields are omitted or dotted as required by their fixed format. Invalid latitude, longitude, elevation, or observations older than `StaleAfterSeconds` are rejected locally.

The Davis LOOP2 packet provides two-minute average wind speed but does not provide a matching averaged/dominant direction. It provides current wind direction and a separate ten-minute gust direction. APRS `ddd` therefore uses current `WindDirectionDegrees` as the best protocol-supported direction and deliberately does not substitute gust direction. This limitation is covered by formatter tests.

## Configuration

```json
"Cwop": {
  "Enabled": false,
  "StationId": "DW4515",
  "Host": "cwop.aprs.net",
  "Port": 14580,
  "IntervalSeconds": 300,
  "ConnectTimeoutSeconds": 5,
  "OperationTimeoutSeconds": 5,
  "StaleAfterSeconds": 600,
  "Passcode": "-1",
  "PasscodeSecret": null,
  "SoftwareName": "HVO-Davis",
  "SoftwareVersion": "1.0"
}
```

Failures back off from the configured interval up to one hour, so an uncertain outcome never causes a retry faster than the protocol's five-minute minimum. Each attempt reads the current observation and settings, so recovery never replays stale packets. Protected diagnostics expose enabled state, last observation, attempt, success, consecutive failures, and a sanitized error category.

## Rollout

1. Confirm `DW4515` ownership, the standard CWOP `pass -1` policy, `cwop.aprs.net:14580`, and the five-minute cadence with the current CWOP registration contact.
2. Deploy with `Enabled=false`; verify acquisition, MQTT, Weather Underground, outbox, and diagnostics.
3. If required, materialize `cwop-passcode` through the existing Key Vault-to-secrets workflow and configure `PasscodeSecret` without printing the value.
4. Enable CWOP and deploy once. Verify diagnostics show a recent success and no login rejection.
5. Verify `DW4515` position and freshness through CWOP/MADIS after several intervals and confirm there is no duplicate publisher.

Rollback only requires setting `Enabled=false` and redeploying. No database or queue cleanup is needed.
