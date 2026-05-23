# SolarAssistant Discovery Tool

Status: non-deployable discovery helper.

This tool probes SolarAssistant read interfaces and prints sanitized metadata for planning the future `HVO.Gateway.SolarAssistant` service. It does not write to SolarAssistant, the HVO website, or a local outbox.

## Credentials

Set credentials through environment variables only. Do not commit local IPs, passwords, tokens, or raw payload captures.

```bash
export SOLARASSISTANT_HOST="<host-or-ip>"
export SOLARASSISTANT_PASSWORD="<local-password>"
```

The probe also accepts alternate env var names used by local deployment configuration: `SOLAR_ASSISTANT_IP`, `SOLAR_ASSISTANT_REST_USERNAME`, `SOLAR_ASSISTANT_REST_PASSWORD`, `SOLAR_ASSISTANT_MQTT_USERNAME`, and `SOLAR_ASSISTANT_MQTT_PASSWORD`.

Optional values:

```bash
export SOLARASSISTANT_USER="admin"
export SOLARASSISTANT_TOKEN="<bearer-token>"
export SOLARASSISTANT_MQTT_USER="<mqtt-user>"
export SOLARASSISTANT_MQTT_PASSWORD="<mqtt-password>"
export SOLARASSISTANT_MQTT_TOPIC="solar_assistant/#"
export SOLARASSISTANT_MQTT_SECONDS="15"
export SOLARASSISTANT_MQTT_MAX_PACKETS="1000"
export SOLARASSISTANT_WEBSOCKET_SCHEME="ws"
export SOLARASSISTANT_WEBSOCKET_PORT="80"
export SOLARASSISTANT_WEBSOCKET_TOPICS="total/*,inverter_1/*,battery_1/*"
```

The default MQTT subscription is `solar_assistant/#`; use `SOLARASSISTANT_MQTT_TOPIC="#"` only when you intentionally want to inspect all broker topic names.

## Run

```bash
python3 tools/solarassistant-discovery/probe.py
```

Run this helper on a host with Python 3 available. The repo devcontainer is not the required runtime for this non-deployable probe.

The output intentionally summarizes topics, groups, units, events, Home Assistant discovery metadata, and sample topic names without printing metric/state values.

WebSocket discovery sends the local SolarAssistant password in the WebSocket URL query string because that is the interface SolarAssistant exposes. Run it only on trusted local networks, prefer `SOLARASSISTANT_WEBSOCKET_SCHEME=wss` and port `443` if your installation supports TLS, and assume URLs may be visible in local diagnostic logs.

MQTT discovery output is grouped into:

- `mqtt_db_candidates`: fields already aligned with the current normalized HVO power snapshot shape.
- `mqtt_review`: power-system fields that may deserve central persistence after unit/sign/cadence review.
- `mqtt_local_only`: operator/device metadata that is useful on the local gateway but should not automatically become historical website data.

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

The production gateway now includes a read-only MQTT inventory subscriber that exposes sanitized Home Assistant discovery metadata and state-topic availability through `/mqtt-inventory`. WebSocket remains a fallback/diagnostic stream. Rotate temporary development credentials before production use.
