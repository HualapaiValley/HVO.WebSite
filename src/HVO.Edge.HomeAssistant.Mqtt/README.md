# HVO.Edge.HomeAssistant.Mqtt

Active .NET 10 library depending on [Hosting](../HVO.Edge.Hosting/README.md) and
MQTTnet. Collectors compose `AddHvoHomeAssistantMqtt` and gateway diagnostics
projection; there is no standalone executable. It owns bounded HA discovery,
current state/availability, reconnect/session behavior, credentials and explicitly
registered command routing. It is not canonical HVO history or hardware acquisition.

The `HomeAssistant:Mqtt` options select enablement, host/port, prefixes and
startup UsernameSecret/PasswordSecret references. Port defaults to 1883,
DiscoveryPrefix to homeassistant and TopicPrefix to hvo; enabled projection needs
valid site identity. Read actual [options and registration source](./) for all
startup validation; use read-only mounted secret files, not credential JSON.

Commands require an explicitly registered handler/topic. Current JK has a bounded
secret-backed settings-password PRESS operation with ACK/readback and device
guards; [JK safety](../../docs/gateways/jk-bms.md) is its owner. That exception
does not create generic control or imply commands are exercised by every HA fixture.

After exact-SDK locked root restore/build:

```bash
dotnet test tests/HVO.Edge.HomeAssistant.Mqtt.Tests --no-build --no-restore --filter "TestCategory!=Integration&TestCategory!=Live"
```

[HA disposable fixture](../../tests/HomeAssistant.IntegrationEnvironment/README.md)
owns separately provisioned integration execution. [HA operations](../../deploy/home-assistant/README.md)
and [runtime](../../docs/architecture/EDGE_VNEXT_RUNTIME.md) own commissioning.
