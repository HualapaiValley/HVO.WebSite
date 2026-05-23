# SolarAssistant Discovery Findings

Status: sanitized live discovery from the local SolarAssistant instance. Do not add raw credentials, tokens, local secret files, or unsanitized payload captures to this document.

## Interfaces Observed

| Interface | Result | Notes |
|-----------|--------|-------|
| REST `/api/v1/metrics` | 124 metrics | Prefixes: `inverter_1` 102, `total` 17, `battery_1` 5 |
| MQTT `#` | 90 unique topics | 48 Home Assistant discovery entities and 42 `solar_assistant/.../state` topics |
| WebSocket `/api/websocket` | 104 definition/data topics | Prefixes: `inverter_1` 82, `total` 17, `battery_1` 5 |

MQTT Home Assistant discovery identified one device:

| Name | Manufacturer | Model |
|------|--------------|-------|
| Axpert Max | Voltronic | Axpert Max |

## MQTT Discovery Shape

Home Assistant discovery exposed:

| Component | Count |
|-----------|------:|
| `sensor` | 34 |
| `select` | 14 |

Observed device classes: `energy`, `power`, `voltage`, `current`, `battery`, `temperature`.

Observed state classes: `measurement`, `total_increasing`.

Observed units: `kWh`, `W`, `V`, `A`, `%`, `Hz`, `VA`, degrees Fahrenheit.

## Current DB Candidates

These align with fields already present in the v9 `PowerReading` model or simple aliases of those fields:

| HVO field | SolarAssistant topics |
|-----------|-----------------------|
| `PvPowerW` | `total/pv_power` |
| `LoadPowerW` | `total/load_power` |
| `GridPowerW` | `total/grid_power` |
| `BatteryPowerW` | `total/battery_power` |
| `SystemPowerW` | `total/system_power` |
| `BatteryStateOfChargePercent` | `total/battery_state_of_charge` |
| `BatteryVoltageV` | `total/battery_voltage`, `battery_1/voltage` |
| `BatteryCurrentA` | `total/battery_current`, `battery_1/current` |
| `BatteryCapacityKwh` | `total/battery_capacity`, `battery_1/capacity` |
| `GridVoltageV` | `total/grid_voltage`, `inverter_1/grid_voltage` |
| `GridFrequencyHz` | `total/grid_frequency`, `inverter_1/grid_frequency` |
| `OutputVoltageV` | `total/ac_output_voltage`, `inverter_1/ac_output_voltage` |
| `OutputFrequencyHz` | `total/ac_output_frequency`, `inverter_1/ac_output_frequency` |
| `LoadPercentage` | `total/load_percentage`, `inverter_1/load_percentage` |
| `InverterMode` | `total/inverter_mode`, `inverter_1/device_mode` |
| `OutputSourcePriority` | `total/output_source_priority`, `inverter_1/output_source_priority` |
| `ChargerSourcePriority` | `inverter_1/charger_source_priority` |

## Strong Candidates For New Historical Fields

These are useful for long-term observatory power history, capacity planning, and incident review. They should be added only after confirming units, reset behavior, sign convention, and cadence.

| Area | Topics | Suggested treatment |
|------|--------|---------------------|
| Energy counters | `total/pv_energy`, `total/load_energy`, `total/grid_energy_in`, `total/grid_energy_out`, `total/battery_energy_in`, `total/battery_energy_out` | Advertised by MQTT discovery, but not returned by REST and not observed on MQTT state during the follow-up sample; defer DB fields until values are observable |
| PV strings | `inverter_1/pv_power_1`, `inverter_1/pv_power_2`, `inverter_1/pv_voltage_1`, `inverter_1/pv_voltage_2`, `inverter_1/pv_current_1`, `inverter_1/pv_current_2` | Persist if string-level diagnostics matter; otherwise keep local until needed |
| Inverter load detail | `inverter_1/load_power`, `inverter_1/load_apparent_power`, `inverter_1/system_and_load_power` | Persist only if distinct from `total/load_power` and useful historically |
| Battery detail | `battery_1/power`, `battery_1/state_of_charge`, `inverter_1/battery_voltage`, `inverter_1/battery_current`, `inverter_1/battery_power` | Avoid duplicate central fields unless source meaning differs from total/battery aggregate |

## Local-Only Or Operator Metadata

These are valuable on the gateway UI but should not automatically become high-cadence website history:

| Area | Examples | Reason |
|------|----------|--------|
| Hardware identity | `inverter_1/model_name`, `inverter_1/model_number`, `inverter_1/serial_number`, `inverter_1/firmware_version` | Store as local/device inventory or occasional config snapshot, not every power reading |
| Temperatures/status bits | `inverter_1/temperature`, `inverter_1/status_1`, `status_2`, `status_3`, `status_4` | Useful for local diagnostics; centralize later if alerts need history |
| Settings/selects | `output_source_priority`, `charger_source_priority`, `max_charge_current`, `max_grid_charge_current`, `shutdown_battery_voltage`, `back_to_battery_voltage`, etc. | Track as configuration snapshots or command capabilities, not per-snapshot telemetry |
| Command topics | `solar_assistant/.../set` | Read-only inventory for now; no writes without a separate safety/auth/audit design |

## Recommended Persistence Split

Persist now:

- Continue sending the normalized aggregate power snapshot to v9 `PowerReading`.
- Include the REST aliases discovered above so populated fields match SolarAssistant's actual topic names.

Persist next after review:

- Add cumulative energy counters only after the advertised MQTT state topics publish observable values and their reset behavior is understood.
- Consider PV string fields only if string-level troubleshooting is valuable centrally.
- Add a low-frequency device/config snapshot table for hardware identity and settings changes.

Keep local for now:

- Home Assistant discovery entity metadata.
- MQTT command topics and select options.
- Hardware identity cards, firmware/model/serial, and gateway capability inventory.
- Diagnostic/status topics until they become alert inputs.

## Gateway Changes From This Pass

- The discovery probe now parses MQTT Home Assistant discovery payloads and classifies entities as `db_candidate`, `review`, or `local_only` without printing state values.
- The gateway exposes `/inventory` with sanitized REST topic metadata from the latest poll.
- The gateway exposes `/mqtt-inventory` with sanitized Home Assistant discovery metadata, state-topic availability, device summaries, and command-topic inventory. It does not publish MQTT messages or expose raw state payload values.
- The local monitor page links to `/inventory` and summarizes topic classifications.
- The local monitor page links to `/mqtt-inventory` and summarizes MQTT connection, entity, state-topic, command-topic, and device counts.
- The mapper now recognizes SolarAssistant aliases such as `total/system_power`, `total/battery_voltage`, `total/ac_output_voltage`, and `total/inverter_mode`.
- The local monitor now uses the same MudBlazor shell/header/footer/layout pattern as the Davis gateway while keeping SolarAssistant-specific cards.
- The local monitor keeps a bounded in-memory rolling history and renders PV, load, grid, and battery power trend cards. This is local-only display state and does not change central DB persistence.

Live deployment after this pass reported MQTT connected with `48` entities, `42` state topics, `14` command topics, and `1` discovered device.
