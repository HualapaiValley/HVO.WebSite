# Home Assistant Exporter Deployment

**Implemented deployment template, disabled in production with no mappings or
source claims.** Configuration preparation does not authorize enablement. Follow
[mounted config/secret commissioning](../README.md#mounted-configuration-and-secrets)
for first-install guards and Key Vault behavior. The sync helper can materialize
the HA token/diagnostics files but intentionally omits the ingest key until exact
source claims are approved.

Only for a separately authorized exporter/source migration, initialize ignored
`gateway.json` from the template without overwriting an existing file, add the
approved HA-owned Kasa/Govee mappings and securely materialize:

- `home-assistant-token`
- `central-ingest-api-key`
- `diagnostics-api-key`

Keep `Enabled` false until the previous writer for every mapped physical source is stopped and its outbox is drained. The exporter reconciles current HA state and future `state_changed` events; it never reads Recorder history. HVO-owned MQTT entities and MQTT-platform mappings are rejected.

Provision the central key through `Seeding--HomeAssistantExporterApiKey` and matching `Seeding--HomeAssistantExporterSources--<index>` Key Vault entries before enabling export. The website seeds both ingest scopes and exact source claims; broad legacy keys cannot write a reserved source.
