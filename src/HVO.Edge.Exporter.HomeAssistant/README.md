# HVO.Edge.Exporter.HomeAssistant

Implemented .NET 10 headless executable depending on [Hosting](../HVO.Edge.Hosting/README.md).
[Program](Program.cs) registers the edge runtime and HA exporter, then maps standard
health/protected diagnostics. Production export is intentionally disabled with
no mappings or source claims. Home Assistant still owns Kasa/Govee acquisition
and presentation; exporter enablement is not authorized by this README.

`HomeAssistant:Exporter` config owns enablement, HA ws/wss WebSocket endpoint,
AccessTokenSecret, central ingest endpoint/APIKeySecret, source/entity mappings
and bounded freshness/skew/coalescing/timeouts. Central ingest requires HTTPS
unless the explicit insecure test override is enabled. Disabled mode still
validates its configured central endpoint: empty defaults are not a complete
standalone configuration. Secrets are mounted files resolved at startup.

Enabled mappings are explicit and nonempty: approved `tplink` PowerReading
sources use kasa: prefixes; `govee_ble` WeatherRaw sources use govee: prefixes.
Entity IDs must be unique and cannot be HVO's MQTT projection entities.
Test platform overrides are fixture-only. This bridge currently supports
PowerReading/WeatherRaw, not the future typed SmartShunt atomic bundle.

After exact-SDK locked root restore/build:

```bash
dotnet test tests/HVO.Edge.Exporter.HomeAssistant.Tests --no-build --no-restore --filter "TestCategory!=Integration&TestCategory!=Live"
```

[Exporter rollout/config](../../deploy/pi-gateways/home-assistant-exporter/README.md),
[HA operations](../../deploy/home-assistant/README.md) and
[#320](https://github.com/HualapaiValley/HVO.WebSite/issues/320) own export enablement.
[#352](https://github.com/HualapaiValley/HVO.WebSite/issues/352) additionally requires
native passive key/parity/bundle proof before replacing SmartShunt's current
direct authority. [Exactly-one-writer rollback](../../docs/gateways/sqlite-backup-and-rollback.md)
governs that transition; no deployment or hardware operation is performed here.
