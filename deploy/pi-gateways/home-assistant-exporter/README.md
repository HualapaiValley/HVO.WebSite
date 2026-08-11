# Home Assistant Exporter Deployment

Copy `gateway.json.example` to the ignored `gateway.json`, add only approved HA-owned Kasa/Govee mappings, and create these secret files under the ignored `secrets/` directory:

- `home-assistant-token`
- `central-ingest-api-key`
- `diagnostics-api-key`

Keep `Enabled` false until the previous writer for every mapped physical source is stopped and its outbox is drained. The exporter reconciles current HA state and future `state_changed` events; it never reads Recorder history. HVO-owned MQTT entities and MQTT-platform mappings are rejected.

Provision the central key through `Seeding--HomeAssistantExporterApiKey` and matching `Seeding--HomeAssistantExporterSources--<index>` Key Vault entries before enabling export. The website seeds both ingest scopes and exact source claims; broad legacy keys cannot write a reserved source.
