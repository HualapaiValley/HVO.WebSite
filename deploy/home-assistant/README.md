# Home Assistant configuration

This directory contains the agent-managed Home Assistant configuration for the
observatory instance. It intentionally excludes `.storage`, secrets, generated
state, and integration config entries. Core integrations are commissioned only
through supported Home Assistant config flows.

Home Assistant is the production acquisition and presentation owner for Kasa and Govee. The HVO HA telemetry exporter is implemented but intentionally disabled, with no production mappings or source claims. The direct HVO TP-Link/Kasa gateway, container, and image have been removed; keep the active HA Kasa dashboards and tests.

## Managed configuration

The tracked configuration is deployed under `/config/hvo`:

- `configuration/lovelace.yaml` registers HVO YAML dashboards.
- `configuration/frontend/` contains locally served HVO Lovelace cards.
- `configuration/packages/hvo.yaml` is the package entry point.
- `configuration/templates/hvo.yaml` contains HVO template entities.
- `configuration/sensors/hvo-energy.yaml` contains presentation-only energy integrators.
- `configuration/utility-meters/hvo-energy.yaml` contains daily energy meters.
- `configuration/automations/hvo.yaml` contains HVO automations.
- `configuration/esphome/hvo-bluetooth-proxy.yaml` is the ESP32 Bluetooth proxy
  definition and is commissioned separately through ESPHome Device Builder.

Issue #331 defines availability-safe power templates and energy helpers. Derived
energy remains a Home Assistant presentation concern and is never exported as
source-native HVO telemetry.

The HVO Operations dashboard groups the commissioned H5074/H5075 environment
sensors, Davis weather, SmartShunt whole-bus battery status, individual JK BMS
banks, and collector diagnostics. Gateway collectors publish a dedicated
diagnostics device every 30 seconds with readable `sensor.hvo_<gateway>_*` and
`binary_sensor.hvo_<gateway>_*` IDs. Health and freshness remain calculated by
the collector-specific diagnostics providers; Home Assistant only presents and
alerts on those authoritative states.

The dedicated `HVO Weather` dashboard at `/hvo-weather/overview` is the
canonical detailed operations weather view. The shorter Weather view in HVO
Operations links to it. The dashboard groups current conditions, a graphical
wind instrument, rain totals, 24-hour and 7-day recorder history, local
astronomy, an external forecast, and subordinate station health. It uses only
built-in cards except for the source-controlled wind card at
`configuration/frontend/hvo-weather-wind-card.js`; the deployment script serves
that asset from `/config/www/hvo` and registers it through the local `/local/hvo`
URL. It has no HACS, CDN, or internet-hosted frontend dependency.

Current, stale, waiting, error, and unavailable presentation is driven by the
collector-owned `sensor.hvo_davis_source_freshness` diagnostic. Entity
`last_updated` age is displayed only as supporting context and never overrides
the collector's authoritative state.

The Davis collector is authoritative for local observations and publishes moon
phase, illumination, moonrise, and moonset using the repository's HVO astronomy
calculations plus the Davis console location and time-zone settings. If console
location is unavailable, those entities remain unavailable instead of showing
invented values. Calculations are cached by local calendar date, console UTC
offset, latitude, and longitude; any relevant setting or date change invalidates
the cache. Deploying a Davis collector image containing those projection changes
is a separate controlled live operation.

`weather.forecast_home` is the commissioned external Home Assistant weather
entity and is deliberately labeled only as external forecast data. It supplies
current cloud coverage and the short hourly forecast; cloud cover is never
inferred from the Davis forecast text. The deployment check requires that entity
and its `cloud_coverage` attribute to exist. Provider identity must be verified
through the supported Home Assistant integration/config-entry UI before it is
named in this documentation or dashboard. If the provider is offline, its card
and `sensor.hvo_forecast_cloud_coverage` become unavailable independently while
the Davis observations continue to render. Forecast refresh may require network
access, but all dashboard code and resources remain local.

The 2-minute wind average, 15-minute rain, and 1-hour rain Davis entities are
disabled by default. Enable them in the Home Assistant entity registry to show
those optional instruments. Optional rain cards are conditionally omitted and
the wind card renders a placeholder when those entities are disabled; no
entity-not-found card is shown. Recorder history must be enabled for
the referenced Davis entities to populate the Trends view; no long-term
statistics metadata is required because the dashboard uses built-in raw
`history-graph` cards.

Dashboard deployment is collector-first: the four Davis astronomy entities are
required live entity references and are intentionally absent from
`managed-entities.txt`. Deploy and verify the collector projection before
running or applying the dashboard deployment. The deployment check must fail if
those required entities have not appeared through MQTT Discovery.

Critical automations create persistent notifications for source unavailability,
stale/error freshness, critical gateway health, outbox backlogs above 10 rows,
whole-bus state of charge below 20%, JK BMS alarms, and commissioned Govee
availability/battery conditions. Stable notification IDs update an existing
alert instead of producing an unbounded notification list. H5179 remains
hardware/RF-blocked and is intentionally absent until it is commissioned.

## Off-grid energy model

The Energy presentation has no grid source. The 6500EX supplies native lifetime
PV generation (`QET`) and AC output/load (`QLT`) counters. The external
MPPT100-48HV PV input is integrated from its direct power sensor. SmartShunt
charge and discharge are integrated separately from its signed whole-bus power.
The external MPPT may sleep and remain unavailable after dark; unavailable data
is never replaced with zero.

`HVO 6500EX AC Load` remains the Energy parent for the selected Kasa
breakdowns. This name identifies its source counter without claiming an
independently verified site or fleet total. The instantaneous 6500EX local
load and parallel-system load are distinct dashboard signals; the lifetime QLT
counter's exact fleet/site aggregation semantics have not been independently
proven.

The dashboard has responsive Overview, Generation, Usage, Batteries, 6500EX,
MPPT100, SmartShunt, and JK Banks and Health views. It shows all three physical
PV inputs, the native 6500EX two-tracker subtotal, and an availability-safe
all-three total. The total is unavailable unless every tracker is numeric.
Daily energy aggregation adds the QET-derived 6500EX daily meter to the external
controller's integrated daily meter, never the combined counter and its children.
JK bank counters remain comparison/fallback candidates and are not combined
with the SmartShunt whole-bus meter.

All five daily utility meters read persisted, non-resetting lifetime cumulative
sources: native QET/QLT or HA integration-derived cumulative energy. They set
`periodically_resetting: false` so Home Assistant calculates from its last valid
source state after an unavailable interval instead of losing the reconnect
increment. `always_available` remains unset; source unavailability still
propagates, including to the availability-safe combined daily helper.

This setting is prospective. It does not repair a deficit already accumulated
in the current local day. After deployment, either wait for the next local
midnight cycle before final acceptance or separately review an explicit,
supported `utility_meter` calibration using a fresh recorder-derived target.
Recorder completion lag means operators must not guess a calibration value, and
this change intentionally adds no mutating calibration mode.

The managed Energy preferences register the validated 6500EX combined solar
counter and the external MPPT integration helper as two separate solar sources,
plus the SmartShunt battery and `HVO 6500EX AC Load`. Distinct physical Kasa
parents are nested device-consumption breakdowns through `included_in_stat`;
child outlets are excluded to prevent overlap. The managed parents are the
control-room, telescope, workshop, observatory-amenities, TPRA workshop strips,
and the power-room heater. Live device-registry evidence identifies each by a
different TP-Link device ID and hardware identifier. Daylight validation confirmed the
external helper exposes cumulative `kWh` statistics and increases with direct
MPPT power; it may still be unavailable after dark when the controller sleeps.

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
export HVO_HOME_ASSISTANT_ALLOW_INSECURE=true
./scripts/deploy-home-assistant-dashboard.sh --apply
```

Optional overrides:

```bash
HVO_PROXMOX_HOST=root@192.168.1.240 \
HVO_HOME_ASSISTANT_VMID=101 \
HVO_HOME_ASSISTANT_URL=http://192.168.1.113 \
HVO_HOME_ASSISTANT_ALLOW_INSECURE=true \
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

Use `HomeAssistantEnergyDashboardPlaywrightTests`,
`HomeAssistantOperationsDashboardPlaywrightTests`, or
`HomeAssistantWeatherDashboardPlaywrightTests` in the same filter to check the
other dashboards. These tests open the dashboards at desktop and mobile widths.
They do not click a switch or call any device service. The non-live
`HomeAssistantWeatherWindCardPlaywrightTests` exercises compass directions,
missing values, stale state, and 320-pixel sizing with deterministic states.

The deterministic pinned-Home-Assistant suite loads the exact tracked
automation YAML and uses MQTT simulator entities to exercise offline, stale,
battery, gateway-health, and outbox-backlog transitions:

```bash
./tools/run-home-assistant-integration-tests.sh
```

## Energy preferences

The dashboard deployment does not mutate Energy preferences. Check or apply the
managed off-grid manifest transactionally through the supported WebSocket API:

```bash
set -a && source .env && set +a
export HVO_HOME_ASSISTANT_ALLOW_INSECURE=true
dotnet run --project tools/HVO.Tools.HomeAssistantEntityMigration -- --backup
dotnet run --project tools/HVO.Tools.HomeAssistantEntityMigration -- --energy-check
dotnet run --project tools/HVO.Tools.HomeAssistantEntityMigration -- --energy-apply
dotnet run --project tools/HVO.Tools.HomeAssistantEntityMigration -- --energy-audit
```

The check reports managed-entry drift by name. Validation inspects solar,
battery, device-consumption, and water categories so submeter errors cannot be
missed. Before check/apply, the tool also verifies that every managed cumulative
entity is recorder `has_sum` eligible, every managed rate/SOC entity is recorder
`has_mean` eligible, and their current state and metadata match the Energy role.
`included_in_stat` is validated as an acyclic graph whose parents are configured
device-consumption statistics. The apply operation preserves unrelated preferences, removes the known
legacy workshop child-plug entry, and restores the previous preferences if save,
readback, or Energy validation fails; rollback is also read back and a combined
failure is reported if restoration cannot be verified. The external MPPT
was added only after daylight established valid cumulative `kWh` metadata and
Home Assistant Energy validation accepted the second solar source.

These checks use only supported Home Assistant 2026.8 WebSocket responses:
`get_states`, `recorder/list_statistic_ids`, `energy/get_prefs`, and
`energy/validate`. Recorder eligibility confirms metadata capability, not the
completeness or historical accuracy of stored samples. Current rate/SOC checks
accept normal `unknown` and `unavailable` states so nighttime and transient
outages do not block operations. Numeric values must be finite, numeric SOC must
remain within 0-100, and other malformed states fail validation. Home Assistant's own `energy/validate` remains authoritative for
additional platform validation semantics.

`--energy-audit` is read-only. It obtains the configured timezone through
`get_config`, then requests `recorder/statistic_during_period` with the server's
`calendar: { period: day }` and `change` semantics for each cumulative source.
Home Assistant therefore resolves local midnight and clamps the current day to
now; recorder combines hourly and short-term statistics for this partial-day
change. The audit separately requests five-minute sum rows through
`recorder/statistics_during_period` and rejects data older than 15 minutes.

The audit compares QET to the 6500EX PV daily meter, external integrated energy
to its daily meter, QLT to the AC daily meter, and both SmartShunt directional
integrations to their daily meters. It also compares the combined solar helper
to the two solar daily meters. Native QET/QLT comparisons start with a 0.1 kWh
precision allowance; integration comparisons start with 0.001 kWh. To account
only for bounded recorder completion lag, each source tolerance adds
`nameplate kW * age of newest statistics row in hours`. Combined-current
agreement uses 0.002 kWh. Missing, stale, nonnumeric, or materially different
values fail the command.

When recorder change materially exceeds a daily meter, the audit reports the
cumulative source and utility-meter entity IDs and identifies unavailable-gap
loss as the likely mechanism. This remains a failure; the diagnostic does not
widen tolerance or conceal an already accumulated deficit.

PR acceptance evidence for Energy changes should include the concise audit
table, a passing `--energy-check`, and the live desktop/mobile Playwright result.
During pre-deployment review, record expected preference drift or missing new
helpers explicitly rather than presenting the audit as passed.

For issue #380 rollout, deploy the managed YAML first, wait for
`sensor.hvo_all_pv_power` and `sensor.hvo_total_pv_energy_daily` to become
available, then create a backup and run `--energy-apply`. Follow with
`--energy-check`, confirm `energy/validate` has empty arrays in every category,
and run `HomeAssistantEnergyDashboardPlaywrightTests` at desktop and phone
widths. Do not judge daily reconciliation across a local-midnight reset or while
the five-minute QET/QLT counters are between updates.

The dashboard deploy transaction retains its on-host backup after Core restarts
until both aggregate helpers reappear with the expected metadata. When every
source is numeric, post-restart validation requires all-three PV agreement
within 1 W and combined daily agreement within 0.002 kWh. If any source is not
numeric, its aggregate helper must be exactly `unavailable`; stale numeric
helpers are rejected. A failure restores the
prior managed tree while the backup still exists.

## One-time commissioning

These steps use Home Assistant and add-on supported interfaces. Never create or
edit config entries by writing `.storage`.

### Service identity and API token

1. In **Settings > People > Users**, create a dedicated `hvo-automation` user.
2. Grant administrator access because the managed deployment and migration
   tooling uses protected Home Assistant APIs. Do not use the HA owner account.
3. Sign in as `hvo-automation`, open its profile, and create a long-lived token
   named `HVO automation`.
4. Store the token as `HomeAssistant--Token` in `hvo-central-kv`. Do not place it
   in Git, issue comments, logs, or Home Assistant YAML.
5. Materialize it only into the ignored root `.env` as
   `HOME_ASSISTANT_TOKEN`. Materialize it into the exporter's ignored mounted
   secret file only after a separately approved exporter enablement.
6. To rotate it, create and deploy the replacement first, verify API and managed
   tooling access, then delete the previous token from the service profile and
   replace the Key Vault secret version.

The token grants administrative API access. Keep its canonical copy in Key
Vault and restrict local materializations to approved deployment and migration
tools. The disabled exporter must not receive a production token as routine
configuration.

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
registered parent/child devices. Kasa remains an HA-owned acquisition and
presentation source. No Kasa exporter mapping or production source claim is
enabled.

### Bluetooth proxy and Govee sensors

1. Place a supported ESP32 development board near the Govee sensors. The proxy must
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
   each sensor through the proxy. H5074 and H5075 require active scan responses;
   the proxy does not pair with or establish GATT connections to them. An H5179
   Wi-Fi address is not used by this path.
7. Confirm temperature and humidity entities have stable values and entity
   registry platform `govee_ble`. Record their entity IDs for operations, but do
   not create or enable a production exporter mapping without separate approval.
8. Keep the HA exporter disabled; no production Govee source claim is provisioned.

The observatory currently has native `govee_ble` entries for H5074 `8D05` and
H5075 `48D9`. Each exposes stable temperature, humidity, battery, and signal
strength entities. The canonical measurement IDs are:

- `sensor.h5074_8d05_temperature`
- `sensor.h5074_8d05_humidity`
- `sensor.h5075_48d9_temperature`
- `sensor.h5075_48d9_humidity`

Both integrations recovered after Home Assistant and proxy restarts. H5179 did
not advertise during the commissioning window and remains a later target when
present and within RF range.

The ESP32-D0WDQ6 at `192.168.2.196` is commissioned as `Home Dev Bluetooth Proxy`
for transport validation on the HOME network. Its encrypted native API, remote
scanner registration, advertisement forwarding, three connection slots, HA
restart recovery, and ESP32 restart recovery have been verified. Move this
tracked proxy into reliable HVO RF range and then remove or rotate its HOME
Wi-Fi fallback credential before treating the proxy deployment as permanent.

Commissioning used a temporary Linux ESPHome-compatible bridge on the isolated
Pi USB controller `hci1`. It was pinned and locally constrained to zero GATT
connection slots; JK BMS and SmartShunt remained on `hci0`. This proved native
Govee transport and restart recovery but is not tracked production architecture.
Do not reproduce or promote the temporary bridge without separate review.

The earlier `Home Dev Temporary iBeacon Monitor` was removed after native Govee
commissioning; no iBeacon duplicate of H5074 should remain.

The temporary bridge also exposes SmartShunt Instant Readout advertisements to
HA. Keep the native Victron integration and HA exporter mapping disabled while
the direct collector owns `smartshunt-main`. Issue #352 governs any future
exactly-one-writer migration.

## Recovery notes

A Home Assistant Core/app backup restores Mosquitto, ESPHome, integrations, and
managed Core configuration, but a full HA OS restore does not recreate the
host-level off-node backup mount or its schedule. Re-add and test that mount and
schedule after disaster recovery. Then validate MQTT authentication, retained
discovery cleanup, Kasa availability, ESPHome proxy availability, and the Govee
entities before restoring automations. Confirm the HA exporter remains disabled
unless a separate approved source-authority change says otherwise.
