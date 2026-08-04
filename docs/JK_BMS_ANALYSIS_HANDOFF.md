# JK BMS Analysis Handoff

Date: 2026-08-04
Repository: `/workspaces/HVO.WebSite`
Branch: `main`

## Objective

Continue the JK BMS fleet dashboard work and determine whether the reported BMS SoC can be corrected or independently estimated using:

- JK BMS signed current/power history
- SolarAssistant battery SoC, voltage, current, and power history
- Victron SmartShunt voltage/current/power history

The immediate analytical target is the suspected low-battery event around July 18, 2026, reportedly near 4% SoC and approximately 46 V. The goal is to determine whether that event can provide a defensible zero/near-zero SoC anchor and then estimate present SoC from subsequent signed battery energy.

## Current JK BMS Dashboard Files

Primary overview:

- `src/HVO.Hardware.JkBms/Components/Pages/Status.razor`
- `src/HVO.Hardware.JkBms/Components/Pages/Status.razor.cs`
- `src/HVO.Hardware.JkBms/wwwroot/app.css`

History and calculations:

- `src/HVO.Hardware.JkBms/History/BmsHistoryService.cs`
- `src/HVO.Hardware.JkBms/Components/Pages/Charts.razor`
- `src/HVO.Hardware.JkBms/Components/Pages/Charts.razor.cs`

Power sign normalization:

- `src/HVO.Hardware.JkBms/Components/Pages/BmsDisplayFormatting.cs`

Timezone configuration:

- `src/HVO.Hardware.JkBms/Configuration/JkBmsOptions.cs`
- `src/HVO.Hardware.JkBms/Configuration/JkBmsDisplayTimeZoneResolver.cs`
- `src/HVO.Hardware.JkBms/appsettings.json`
- `deploy/pi-gateways/jkbms/docker-compose.yml`
- `deploy/pi-gateways/jkbms/.env.example`

Tests:

- `tests/HVO.Hardware.JkBms.Tests/History/BmsHistoryCalculationsTests.cs`
- `tests/HVO.Hardware.JkBms.Tests/Components/BmsDisplayFormattingTests.cs`
- `tests/HVO.Hardware.JkBms.Tests/Components/JkBmsStatusPageBunitTests.cs`
- `tests/HVO.Hardware.JkBms.Tests/Components/DeviceDetailPageBunitTests.cs`
- `tests/HVO.Hardware.JkBms.Tests/Protocol/CellInfoPacketTests.cs`

## Dashboard Features Already Implemented

- Combined fleet metrics for SoC, average voltage, current/power into and out of the pack.
- SoC, battery power, voltage, current, and temperature line charts.
- Overview chart ranges: 24 hours, 48 hours, and 7 days.
- Three-day table for battery charged, battery discharged, net battery energy, high voltage, and low voltage.
- Local observatory timezone support using `America/Phoenix` by default.
- Wide body/header/footer layout for JK BMS.
- Direction-aware hours-to-full and hours-to-empty logic.
- Corrected power sign based on live deployed readings.

The SoC rate text was moved into the State of Charge card. The 7-day range was added to the overview source and was included in the last native build attempt.

## Verified Power Sign Correction

Live JK readings were extracted from the Pi outbox around 2026-08-04 01:18 UTC. All seven banks had negative raw current, with normalized total power approximately `-2.28 kW`, matching the user’s observation that roughly 2-2.5 kW was being removed from the battery.

The canonical formatter was changed from negating the raw current/power to preserving the observed sign:

- Positive normalized power: into/charging the pack
- Negative normalized power: out/discharging from the pack

Relevant file:

- `src/HVO.Hardware.JkBms/Components/Pages/BmsDisplayFormatting.cs`

Protocol/model comments and tests were updated accordingly. The live server-rendered page was verified to show approximately `-2131 W`, `Discharging now`, and `Not charging`.

## ETA Logic

`BmsHistoryCalculations.CalculateToday` now gates ETAs by recent fleet power direction:

- Charging power plus positive SoC trend can produce hours to full.
- Discharging power plus negative SoC trend can produce hours to empty.
- The opposite ETA is null and the UI says `Not charging` or `Not discharging`.
- Direction exists but the SoC trend is not stable: `Trend pending`.

Relevant file:

- `src/HVO.Hardware.JkBms/History/BmsHistoryService.cs`

## Timezone Logic

The JK gateway uses `JkBms__DisplayTimeZoneId`, defaulting to `America/Phoenix`.

Timezone is applied to:

- Chart labels
- Daily summary boundaries
- Today energy calculations
- Device/admin/inventory timestamps
- Gateway footer timestamps

Do not use machine-local `ToLocalTime()` for new BMS dashboard calculations. Use `JkBmsDisplayTimeZoneResolver` and explicit `TimeZoneInfo` conversion.

## Verified Retention Gap

Central SQL Server:

- Host: Docker context `hvo-docker`, SQL container `mssql`
- Database: `HualapaiValleyObservatory`
- Schema: `v9`

Central tables:

- `v9.BmsReading`
- `v9.BmsReadingMinute`
- `v9.BmsReadingHourly`
- `v9.PowerReading`

Observed on 2026-08-04:

- `v9.BmsReading`: 88,300 rows, approximately 2026-06-26 through 2026-07-03.
- `v9.BmsReadingMinute`: 0 rows.
- `v9.BmsReadingHourly`: 0 rows.
- `v9.PowerReading`: central power history also ended around 2026-07-03.
- The central schema has no daily BMS rollup table.

The Pi JK outbox continued through 2026-08-04, so the central gap is not caused by the JK gateway lacking data.

The issue is documented in:

- `docs/gateways/jk-bms.md` under `Known Issues And Quirks`.

## Retention Root Cause Found

Central ingest currently only inserts raw BMS rows:

- `src/HVO.WebSite.v9/Controllers/BmsController.cs`
- `src/HVO.WebSite.v9/Services/BmsIngestService.cs`

`BmsIngestService` writes to `_db.BmsReadings`. There is no discovered hosted rollup/retention service for BMS data, and the minute/hour tables are schema-only and empty.

The central data models/context are:

- `src/HVO.DataModels/Models/V9/BmsReading.cs`
- `src/HVO.DataModels/Models/V9/BmsReadingMinute.cs`
- `src/HVO.DataModels/Models/V9/BmsReadingHourly.cs`
- `src/HVO.DataModels/Data/HvoV9DbContext.cs`
- `src/HVO.DataModels/Migrations/V9/20260430234711_AddBmsSchema.cs`

Later implementation target:

- Preserve raw BMS readings for at least 30-60 days.
- Generate hourly rollups and retain them for 6-12 months.
- Generate daily rollups and retain them longer term.
- Add a scheduled worker or separate maintenance job.
- Add ingest-gap monitoring and rollup-freshness monitoring.
- Investigate why central ingestion stopped July 3 while the edge outbox continued.

Do not delete or reset production data while investigating this.

## Live Gateway Locations

Docker contexts:

- `devpi5`: `192.168.1.8`
- `hvo-docker`: `192.168.1.238`

JK BMS:

- Container: `jkbms-hvo-jkbms-1`
- URL: `http://192.168.1.8:5200/`
- Database: `/app/data/outbox.db`
- Compose: `deploy/pi-gateways/jkbms/docker-compose.yml`
- Persistent volume: `jkbms-outbox`

SolarAssistant:

- Container: `hvo-solarassistant-1`
- URL: `http://192.168.1.8:5300/`
- Database: `/app/data/outbox.db`
- Compose: `deploy/pi-gateways/solarassistant/docker-compose.yml`
- Persistent volume: `solarassistant-outbox`

SmartShunt:

- Container: `hvo-smartshunt-1`
- URL: `http://192.168.1.8:5400/`
- Database: `/app/data/outbox.db`
- Compose: `deploy/pi-gateways/smartshunt/docker-compose.yml`
- Persistent volume: `smartshunt-outbox`

Use read-only copies for analysis. The edge outbox is actively written, so copy the database and its `-wal`/`-shm` companions together where present, or use an application/API query instead.

Example safe copy pattern:

```sh
docker --context devpi5 cp hvo-solarassistant-1:/app/data/outbox.db /tmp/solarassistant-outbox.db
docker --context devpi5 cp hvo-smartshunt-1:/app/data/outbox.db /tmp/smartshunt-outbox.db
```

Do not print payloads, credentials, API keys, passwords, or full database contents.

## Edge Outbox Payloads

JK BMS outbox writer:

- `src/HVO.Hardware.JkBms/Outbox/BmsOutboxWriter.cs`
- Payload type: `bms.reading`
- Payload contains `Reading`, optional `Config`, optional `DeviceInfo`.

SolarAssistant and SmartShunt both use the shared edge outbox pattern. Inspect their payload type constants and payload models before parsing; do not assume the JK `BmsIngressRecord` shape.

Useful search:

```sh
rg -n "PayloadType|PowerReading|Battery|Voltage|Current|Soc|StateOfCharge" \
  src/HVO.Gateway.SolarAssistant src/HVO.Hardware.VictronSmartShunt src/HVO.Edge.Outbox
```

## SoC Analysis Already Completed

Pi JK outbox coverage at the time of analysis:

- Approximately 140,789 valid reading records.
- 7 banks.
- Approximately 2026-07-27 through 2026-08-04.
- Lowest observed SoC: about 5-10% depending on bank.
- Lowest observed pack voltage: about 51.0 V.

Central JK history:

- Approximately 2026-06-26 through 2026-07-03.
- Per-bank minimum SoC ranged from 36% to 84%.
- Lowest central voltage was about 52.43 V.

No retained source contained the user-reported 4% / 46 V event.

Signed amp-hour integration from each Pi bank’s retained low point to the latest reading closely matched the reported SoC rise:

- bank-1a: reported +32%, integrated about +32%
- bank-1b: reported +30%, integrated about +30%
- bank-1c: reported +28%, integrated about +27.6%
- bank-1d: reported +31%, integrated about +30.8%
- bank-2a: reported +39%, integrated about +38.7%
- bank-2b: reported +38%, integrated about +37.7%
- bank-2c: reported +36%, integrated about +36.1%

If the retained low points were incorrectly treated as true zero, the current capacity-weighted fleet estimate would be roughly 33% versus reported roughly 40%. This is only a scenario. It is not a defensible correction because the actual low anchor is missing and the retained low points are 5-10%, not zero.

## Next Analysis Steps

1. Query SolarAssistant local outbox payload types and timestamp range.
2. Query SmartShunt local outbox payload types and timestamp range.
3. Identify all records around 2026-07-18 through 2026-07-20.
4. Extract only timestamp, source/device, battery SoC, pack voltage, current, and power.
5. Find whether SolarAssistant and SmartShunt share a low-voltage event around the same time.
6. Compare their signed power/current integration after the event with their reported SoC.
7. If both independent sources agree on a low anchor, calculate an uncertainty-bounded derived SoC estimate.
8. Do not replace the BMS-reported SoC in production until the anchor and capacity basis are validated.

## Validation History

Last known focused JK test result:

- 168 passed, 0 failed.

Last known full solution build:

- 0 errors.
- Existing package vulnerability and Razor warnings remain in the repository.

Last reported native JK deployment:

- Image tag: `native-20260804014542`
- Build target: ARM64 on native `devpi5` Buildx builder.

## Worktree Safety

The worktree is intentionally dirty with the prior dashboard, timezone, sign correction, history, tests, deployment, and documentation changes. Do not run destructive commands such as `git reset --hard` or `git checkout --` unless explicitly requested. Review existing changes before editing.
