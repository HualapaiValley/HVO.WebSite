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

On each connection, the exporter subscribes to `state_changed` before requesting `get_states`. Events arriving during reconciliation are buffered and applied after the snapshot. Snapshot and event state enter one serialized observation coordinator. A fixed receive-time window collects multi-entity bursts before projecting and persisting their final current state; continuous events do not extend that window indefinitely.

The exporter uses the latest included HA `last_updated` timestamp without adjustment. Identity remains `(SourceId, PayloadType, RecordedAtUtc)` in SQLite and the existing central endpoints. The acknowledgement signature covers the complete serialized typed payload, including source timestamp. An unchanged source value with a newer HA timestamp is a new source observation; no heartbeat timestamp or missed-history backfill is synthesized. Exact replay is suppressed only after proving intended durable content.

All required fields must be present, supported, no older than `RequiredFieldFreshnessSeconds` against current UTC time, no further in the future than `MaxFutureClockSkewSeconds`, and within `MaxFieldSkewSeconds` of one another. Optional fields must meet the same age/future bounds and be within the skew bound of the newest required field; unavailable, missing, stale or skewed optional fields are omitted. Required invalid/unavailable state suppresses the whole projection. Freshness is checked again when the window closes. Per-entity older events are ignored; a same-time changed state can still participate in a not-yet-finalized burst.

Settings in `HomeAssistant:Exporter` (also validated while disabled) are:

| Setting | Default | Allowed range |
|---|---:|---:|
| `CoalescingWindowMilliseconds` | 250 | 1–5000 |
| `RequiredFieldFreshnessSeconds` | 300 | 1–3600 |
| `MaxFieldSkewSeconds` | 30 | 0–300 |
| `MaxFutureClockSkewSeconds` | 30 | 0–300 |

### Compatibility decision: late conflicting content

A bounded window cannot prove that all arbitrarily late events have arrived. The writer distinguishes insertion, exact identical replay, and a conflicting payload under an existing identity. Only insertion or proven identical replay permits acknowledgement. Comparison includes device ID and payload version as well as the serialized typed payload, and includes sent records.

Envelope source/device IDs use the same whitespace trimming as the shared outbox store for insertion, collision lookup and comparison. Existing accepted mappings with padded IDs therefore replay identically after restart. Typed payload bytes are preserved and compared exactly; normalization does not conceal differing payload content, device identity or version.

Conflicts never overwrite pending or sent history, never receive invented timestamps, and are never acknowledged. They remain eligible for bounded retry and authoritative snapshot reconciliation. The exporter exposes a critical `home-assistant-observation-conflict` health alert and a warning with mapping ID/source time (no raw state/credentials). Reconnect does not clear a conflict alert; only successful persistence of a subsequent valid observation for that mapping does. On process restart, the next snapshot is compared to the retained durable row again, re-establishing the conflict before acknowledgement. Required freshness expiry can suspend attempts; later valid source state or a reconnect makes reconciliation eligible again.

The correction preserves public contracts and outbox schema/indexes, so no migration is needed. A later valid source watermark can carry the current corrected values under a new identity. Exporting arbitrary same-time revisions would require an explicitly reviewed central revision contract; operators must not edit sent history or enable another writer to conceal conflicts. This reusable coalescing/freshness/durable-outcome boundary is the prerequisite for #352's future atomic SmartShunt observations, not an implementation or authorization of that physical migration.

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
