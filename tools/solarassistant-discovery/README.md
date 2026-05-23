# SolarAssistant Discovery Tool

Status: non-deployable discovery helper.

This tool probes SolarAssistant read interfaces and prints sanitized metadata for planning the future `HVO.Gateway.SolarAssistant` service. It does not write to SolarAssistant, the HVO website, or a local outbox.

## Credentials

Set credentials through environment variables only. Do not commit local IPs, passwords, tokens, or raw payload captures.

```bash
export SOLARASSISTANT_HOST="<host-or-ip>"
export SOLARASSISTANT_PASSWORD="<local-password>"
```

The probe also accepts the existing repo `.env` aliases `SOLAR_ASSISTANT_IP`, `SOLAR_ASSISTANT_REST_USERNAME`, `SOLAR_ASSISTANT_REST_PASSWORD`, `SOLAR_ASSISTANT_MQTT_USERNAME`, and `SOLAR_ASSISTANT_MQTT_PASSWORD`.

Optional values:

```bash
export SOLARASSISTANT_USER="admin"
export SOLARASSISTANT_TOKEN="<bearer-token>"
export SOLARASSISTANT_MQTT_USER="<mqtt-user>"
export SOLARASSISTANT_MQTT_PASSWORD="<mqtt-password>"
export SOLARASSISTANT_MQTT_TOPIC="#"
export SOLARASSISTANT_WEBSOCKET_TOPICS="total/*,inverter_1/*,battery_1/*"
```

## Run

```bash
python3 tools/solarassistant-discovery/probe.py
```

The output intentionally summarizes topics, groups, units, events, and sample topic names without printing metric values.

## sacli

SolarAssistant also provides `sacli`, which may be useful for manual diagnostics:

```bash
sacli site <host> metrics --json
sacli site <host> metrics --watch
```

Keep `sacli` credentials outside the repo. Do not commit generated tokens or configured local passwords.

## Current Manual Probe Result

- HTTP port was reachable.
- MQTT port was reachable.
- REST metrics rejected unauthenticated/blank local credentials with `401 Unauthorized`.
- MQTT rejected anonymous access with MQTT return code `5`.
- WebSocket probing requires a configured local password.
- Temporary `sacli` validation with version `0.2.2` confirmed the CLI requires a local password argument or saved credential for direct local access; an empty password is treated as missing.
- This device accepts WebSocket connections on `/api/websocket?password=<password>&vsn=2.0.0`; `/api/socket/websocket` returned `404` during discovery.
- With credentials, REST returned `124` metrics across `total`, `inverter_1`, and `battery_1` prefixes.
- With credentials, WebSocket streamed definitions/data for `104` topics across `total`, `inverter_1`, and `battery_1` prefixes.
- With separate MQTT credentials, MQTT produced retained Home Assistant discovery/config topics and live `solar_assistant/.../state` topics.

Next step: design the first production gateway around REST inventory plus MQTT live metrics, with WebSocket retained as a fallback/diagnostic stream. Rotate temporary development credentials before production use.
