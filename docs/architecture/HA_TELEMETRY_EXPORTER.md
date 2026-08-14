# Home Assistant Telemetry Exporter

`HVO.Edge.Exporter.HomeAssistant` implements export of approved Home Assistant-owned current state to canonical HVO history. It uses Home Assistant's documented `/api/websocket` API and never reads Recorder.

Production status: intentionally disabled. There are no production mappings and no production Kasa/Govee source claims. Home Assistant currently owns Kasa and Govee acquisition and presentation only; the exporter is not a canonical production writer.

See [Edge Data Flows](EDGE_DATA_FLOWS.md) for the complete per-source diagrams and authority matrix.

## Authority

- When enabled, configuration is an explicit allowlist of physical Kasa and Govee sources.
- Each mapping assigns a stable HVO source ID and device ID; HA entity IDs are selectors, not canonical identity.
- The exporter verifies each entity's registry platform and rejects MQTT-platform and `sensor.hvo_*` mappings.
- A mapping must not be enabled while another canonical writer owns the same physical observation. The retired direct Kasa collector has already been removed.
- HVO-owned entities projected into HA through MQTT remain owned by their direct gateways and are never exported back to central ingest.

## Observation Semantics

On each connection, the exporter subscribes to `state_changed` before requesting `get_states`. Events arriving during reconciliation are buffered and applied after the snapshot. A complete typed observation is persisted on initial reconciliation and whenever its normalized value tuple changes.

The exporter uses the latest contributing HA `last_updated` timestamp. Reconnect reconciliation with unchanged values is suppressed in memory, and the SQLite key `(SourceId, PayloadType, RecordedAtUtc)` provides durable retry/restart idempotency. No unchanged heartbeat or missed-history backfill is synthesized.

Supported mappings are:

- Kasa power and optional voltage to `PowerReadingPayload` and `/api/v1/power/readings`.
- Govee temperature and humidity to `WeatherRawPayload` and `/api/v1/weather/raw/batch`.

Unknown, unavailable, non-finite, out-of-range, wrong-device-class, and unsupported-unit states are not emitted. Celsius is normalized to Fahrenheit for canonical weather storage.

## Delivery

Observations are committed to the shared SQLite outbox before they are eligible for HTTPS forwarding. The exporter sender groups records by contract and source, treats accepted duplicates as sent, maps per-record validation failures to permanent failures, and leaves timeouts, HTTP 408/429, and server failures available for bounded retry.

Retry-exhausted transient records are automatically requeued on a bounded interval, so an extended central outage drains after recovery. Permanent contract/validation failures remain dead-lettered for operator correction.

Home Assistant and central API credentials are read from mounted secret files. Token values and raw WebSocket messages are never logged.

If production export is approved later, the dedicated central API key must carry `source=<stable-source-id>` claims for every configured mapping in addition to `ingest:power` and/or `ingest:weather`. Central ingest rejects `homeassistant-*` observations when the authenticated key lacks the exact source claim. No such production claims are currently provisioned.

## Future Enablement

1. Obtain explicit approval for each physical source and canonical-history requirement.
2. Create and validate mappings while export remains disabled.
3. Confirm no previous or competing canonical writer exists; if one exists, stop it and drain its durable outbox.
4. Provision only the exact source claims needed by the approved mappings.
5. Record the enablement timestamp, enable the exporter, and verify startup reconciliation, central persistence, and diagnostics.
6. Keep rollback limited to one writer at a time and remove source claims when mappings are disabled.
