# Home Assistant configuration

This directory contains the agent-managed Home Assistant configuration for the
observatory instance. It intentionally excludes `.storage`, secrets, generated
state, and integration config entries. Core integrations are commissioned only
through supported Home Assistant config flows.

## Managed configuration

The tracked configuration is deployed under `/config/hvo`:

- `configuration/lovelace.yaml` registers HVO YAML dashboards.
- `configuration/packages/hvo.yaml` is the package entry point.
- `configuration/templates/hvo.yaml` contains HVO template entities.
- `configuration/automations/hvo.yaml` contains HVO automations.
- `configuration/esphome/hvo-bluetooth-proxy.yaml` is the ESP32 Bluetooth proxy
  definition and is commissioned separately through ESPHome Device Builder.

Templates and automations are intentionally empty until issue #331 defines
stable entities and reviewed safety behavior. This establishes their supported
source-controlled include paths without enabling speculative automation.

The Davis entity registry migration procedure is documented in
`docs/home-assistant/davis-readable-id-migration.md`.

## Kasa power dashboard

`configuration/dashboards/hvo-kasa.yaml` provides the first HVO presentation
slice using the commissioned TP-Link entities on `192.168.1.0/24`.

- Critical infrastructure is status-only. The dashboard does not provide
  actionable controls for routers, network switches, Proxmox hosts, cameras,
  roof equipment, or telescope equipment.
- Permitted switch and light actions are available only from the tile icon and
  always show a confirmation dialog.
- Parent power-strip controls, LED settings, unnamed outlets, restart buttons,
  and cloud diagnostics are omitted.
- The dashboard uses only built-in Home Assistant cards and remains independent
  of internet-hosted resources or custom frontend packages.

## Core deployment

The deployment script uses the Proxmox QEMU guest agent to copy the tracked
files into the HA OS configuration volume. It never writes `.storage`.

Prerequisites:

- SSH access to the Proxmox host.
- `HOME_ASSISTANT_TOKEN` in the ignored root `.env` or current environment.
- HA OS VM `101` running with the QEMU guest agent.

Validate entity references and the complete managed configuration without
changing HA:

```bash
./scripts/deploy-home-assistant-dashboard.sh --check
```

Deploy, run Home Assistant configuration validation, and restart Core:

```bash
./scripts/deploy-home-assistant-dashboard.sh --apply
```

Optional overrides:

```bash
HVO_PROXMOX_HOST=root@192.168.1.240 \
HVO_HOME_ASSISTANT_VMID=101 \
HVO_HOME_ASSISTANT_URL=http://192.168.1.113 \
./scripts/deploy-home-assistant-dashboard.sh --apply
```

The script validates the candidate files against the repository-pinned Home
Assistant image before connecting to production. It then creates a timestamped
backup under `/mnt/data/supervisor/homeassistant`, deploys the managed tree, and
adds these declarations when they are not already present:

```yaml
lovelace: !include hvo/lovelace.yaml

homeassistant:
  packages: !include_dir_named hvo/packages
```

Home Assistant Core sees the host directory as `/config`. The backup is removed
after a successful restart. A failed file transfer, configuration check, or
restart request restores `configuration.yaml` and the complete prior `/config/hvo`
tree. If a different top-level `lovelace:` or `homeassistant:` key exists,
deployment stops and requires a manual merge instead of creating a duplicate
YAML key. The script never writes `.storage`.

After deployment, run the focused live Playwright test with:

```bash
HVO_HOME_ASSISTANT_URL=http://192.168.1.113 \
HOME_ASSISTANT_TOKEN=<long-lived-token> \
dotnet test tests/HVO.WebSite.PlaywrightTests \
  --filter "FullyQualifiedName~HomeAssistantKasaDashboardPlaywrightTests"
```

The test opens the dashboard at desktop and mobile widths. It does not click a
switch or call any device service.

## One-time commissioning

These steps use Home Assistant and add-on supported interfaces. Never create or
edit config entries by writing `.storage`.

### Service identity and API token

1. In **Settings > People > Users**, create a dedicated `hvo-automation` user.
2. Grant administrator access because exporter startup validates the entity
   registry before accepting mapped entities. Do not use the HA owner account.
3. Sign in as `hvo-automation`, open its profile, and create a long-lived token
   named `HVO agent and exporter`.
4. Store the token as `HomeAssistant--Token` in `hvo-central-kv`. Do not place it
   in Git, issue comments, logs, or Home Assistant YAML.
5. Materialize it only into the ignored root `.env` as
   `HOME_ASSISTANT_TOKEN` or into the exporter's ignored mounted secret file.
6. To rotate it, create and deploy the replacement first, verify API and
   exporter health, then delete the previous token from the service profile and
   replace the Key Vault secret version.

The token grants administrative API access. Keep its canonical copy in Key
Vault and restrict local materializations to the systems that run deployment or
export services.

### Mosquitto and MQTT

1. Install the official Mosquitto broker app and enable start on boot and the
   watchdog.
2. Create a dedicated external login named `hvo-edge`; do not reuse a Home
   Assistant interactive user.
3. Store its username and password as `HomeAssistant--MqttUsername` and
   `HomeAssistant--MqttPassword` in `hvo-central-kv`.
4. Confirm the HA MQTT integration is loaded, then verify authenticated publish,
   subscribe, retained discovery, broker restart, HA restart, and retained
   discovery cleanup.

The pinned integration environment exercises MQTT and HA reconnect behavior.
Production credential validation is a bounded live check because secrets are
not available to CI.

### TP-Link Kasa

1. Add TP-Link Smart Home through **Settings > Devices & services**.
2. Commission only observatory devices on `192.168.1.0/24`. Do not add routed
   home networks `192.168.2.0/24` or `192.168.9.0/24`.
3. Do not invoke switch, light, LED, outlet, or restart actions during discovery.
4. Confirm each config entry is loaded and preserve entity IDs referenced by
   `configuration/dashboards/hvo-kasa.yaml`.
5. Run `./scripts/deploy-home-assistant-dashboard.sh --check` after entity
   renames. Missing dashboard entities fail validation.

The observatory instance currently has 15 loaded TP-Link parent entries and 57
registered parent/child devices. Kasa remains an HA-owned source; central-writer
cutover is handled separately by issue #330.

### ESPHome Bluetooth proxy and Govee H5179

1. Place a supported ESP32 development board near the H5179. The proxy must
   receive its BLE advertisements reliably from the intended permanent location.
2. In ESPHome Device Builder, create `home-dev-bluetooth-proxy`, select the actual
   board, and use `configuration/esphome/hvo-bluetooth-proxy.yaml` as the
   reviewed definition. Change `esp32.board` if the selected hardware is not an
   `esp32dev` board.
3. Add the values listed in `secrets.yaml.example` through the ESPHome secrets
   editor. Use generated unique API, OTA, and fallback credentials. The reviewed
   definition prefers HVO Wi-Fi and retains HOME Wi-Fi only as a commissioning
   fallback; remove or rotate the HOME credential after permanent placement.
   Keep the management credentials in `hvo-central-kv` as
   `HomeAssistant--EspHomeProxyApiEncryptionKey`,
   `HomeAssistant--EspHomeProxyOtaPassword`, and
   `HomeAssistant--EspHomeProxyFallbackPassword`. Wi-Fi passwords are stored as
   `obs-wifi-hvo-password` and `obs-wifi-home-express-is-password`; SSIDs remain
   local configuration. Materialized values must not be committed.
4. Install the first image over USB. Subsequent reviewed updates may use OTA.
5. Add the discovered proxy through the native ESPHome integration and verify it
   remains available after both ESP32 and HA restarts.
6. Wait for Home Assistant's supported Govee Bluetooth integration to discover
   the H5179 through the proxy. The H5179 Wi-Fi address is not used by this path.
7. Confirm temperature and humidity entities have stable values and entity
   registry platform `govee_ble`. Record their entity IDs before enabling an
   explicit `govee:` exporter mapping.
8. Do not enable the exporter source until any prior canonical writer is stopped,
   drained, and its source reservation is transferred.

The previously observed H5074 is a separate historical device observation; the
owner-confirmed commissioning target for this issue is H5179.

The ESP32-D0WDQ6 at `192.168.2.196` is commissioned as `Home Dev Bluetooth Proxy`
for transport validation on the HOME network. Its encrypted native API, remote
scanner registration, advertisement forwarding, three connection slots, HA
restart recovery, and ESP32 restart recovery have been verified. This does not
satisfy H5179 commissioning: move the proxy into reliable RF range on HVO Wi-Fi,
confirm the `govee_ble` temperature and humidity entities, and then remove or
rotate its HOME Wi-Fi fallback credential.

The temporary `Home Dev Temporary iBeacon Monitor` integration provides passive
transport monitoring without pairing, sending commands, or consuming a proxy
connection slot. It allowlists only the selected beacon UUID. The beacon rotates
BLE addresses, so HA may show transient address-specific entities until its
iBeacon coordinator consolidates the address family. Remove the integration from
**Settings > Devices & services** after validation; removing it also removes its
temporary devices and entities.

## Recovery notes

A Home Assistant Core/app backup restores Mosquitto, ESPHome, integrations, and
managed Core configuration, but a full HA OS restore does not recreate the
host-level off-node backup mount or its schedule. Re-add and test that mount and
schedule after disaster recovery. Then validate MQTT authentication, retained
discovery cleanup, Kasa availability, ESPHome proxy availability, and the Govee
entities before enabling export or automation.
