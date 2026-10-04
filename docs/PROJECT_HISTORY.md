# Project History

## 2026-10-04 — Unified agent guidance and current UI consumers (#417)

OpenCode review/correction skills now route to the canonical `.agents` lifecycle;
one preparation role uses shared risk/disproof/context and correction checklists.
Claude/Copilot adapters share that profile without a competing review format or
fixed-model claim. Project instructions follow headless mounted-config/secret-file
collectors and owned MSTest/browser fixtures. CSS governance retains token/font/
palette/layout and sandbox sign-off requirements for the actual website and
ThemeSandbox consumers. Adoption/failure history remains source-bound and separate
from current authority; no workflow gate, application behavior, live operation or
repository protection setting changes in this batch.

Current JK MQTT documentation explicitly retains its existing secret-gated
settings-password button/command, positive ACK and DeviceInfo password readback.
That bounded credential-write exception does not imply generic BMS control,
explicit settings-query support or new live hardware qualification.

## 2026-10-04 — Source-backed operations and recovery ownership (#415)

Current runbooks now follow shared endpoint/auth and per-sender retry behavior,
mounted config/secret files, guarded HA transport, hardware-free fixtures and
hosted selective CI. Added a self-hosted website deployment entry; preserved the
former ACA identity/blob/protector record as explicitly superseded history.
Recovery ownership distinguishes existing procedures from missing service drills,
including the unresolved August SQL disk identity. No deployment, live hardware,
credential rotation, schema or runtime policy change is part of this batch.

Correction review added the shared [root-bootstrap/materialization prerequisites](development/key-vault-materialization.md),
qualified gist preview versus credential checking and HA agent-backed backup
creation, and documented the existing SQL derivation error-propagation hazard.
The helper's runtime behavior remains a separate reviewed-change boundary.

## 2026-10-04 — Verified gateway rollback checkpoints (#414)

Replaced SmartShunt's active-volume tar and destructive restore recipe with a
shared quiescent full-volume backup contract. A usable checkpoint now requires a
durable, explicitly owned archive, checksum, SQLite integrity and isolated restore
proof. Rollback preserves the current volume and later observations, restoring an
incompatible older schema only into a separate recovery volume with explicit
reconciliation. Davis/JK historical cutover evidence is retained; future #352 HA
authority migration stays separate. See [backup and rollback](gateways/sqlite-backup-and-rollback.md).

## 2026-10-04 — Canonical v9 weather query boundary (#410)

Added a separate scoped, typed weather query service for new website features over
v9 Davis raw/archive observations. Queries require a station, explicit UTC ranges,
bounded continuation and freshness/no-data handling. Existing legacy weather APIs
and stored-procedure historical access remain unchanged; external production writers
and consumers remain an explicit operational inventory rather than an absence claim.
See [canonical weather queries](development/canonical-weather-queries.md).

Purpose: keep a lightweight working history that explains what changed, what was decided, and what to revisit later. This is not a release changelog.

## 2026-10-04 — Canonical retry durability and BMS event-time history (#400)

- Reconcile conflicts against committed identities and keep unresolved records retryable;
  clear failed EF state before reuse and preserve atomic child writes.
- Serialize BMS mutation on SQL device rows and rebuild paged event-time suffixes,
  preserving raw observations and stable unaffected intervals across delayed delivery.
- Coordinate snapshot content proof with #402 while retaining public contracts.
  [Behavior and compatibility](development/canonical-ingest-retries.md) describe the boundary.

## 2026-10-04 — Complete UTC power-history windows (#405)

- Select deterministic source/bucket representatives before loading detail JSON, retaining
  the full requested range instead of silently capping newest raw rows.
- Emit complete bounded UTC grids with explicit null outage gaps; preserve PV skew/subtotal,
  source/device isolation and battery-facing signs. Capture and retain both query boundaries.
- Restore stored UTC timestamp kinds, use the injected clock and existing visible display
  zone policy. [Query and compatibility notes](development/power-history-utc.md) explain
  payload-work bounds, malformed-data fallback and timezone behavior.

## 2026-10-04 — Power snapshot observation identity (#402)

- Replaced historical source/content uniqueness with submitted source/time observation
  identity, retaining recurring states and fresh unchanged confirmations without rewriting
  old rows. Inventory/configuration remain producer-driven observed snapshots; no new
  periodic producer or confirmation timestamp is invented.
- Kept content proof for identical replay and rejected different content at an immutable
  identity. Latest/freshness queries retain source chronology and exclude impossible
  future/default timestamps. #400 owns matching uniqueness-race acknowledgement proof.
- Added the non-destructive index migration and actual SQL upgrade/downgrade coverage;
  [semantics and rollout guidance](development/power-observation-identity.md) documents
  per-type producer/reader evidence, storage growth and rollback limits.

## 2026-10-04 — Canonical SQL Server test boundary (#409)

- Added a disposable, owned SQL Server lane using the actual production EF registration,
  real clean/prior migrations, canonical queries, conflict and atomicity checks.
- Kept fast InMemory/SQLite tests and separated the provisioned SQL category from HA
  integration. Standard reviewed CI now requires the SQL lane and retains its report.
- Test-only databases/credentials/resources never use application connection settings.
  [Fixture guidance](development/sql-server-integration-tests.md) records tested upgrade
  checkpoints and reusable concurrency seams for #400/#402/#405/#410.

## How To Use This File

- Add or update one entry per working session or tightly related block of work.
- Keep entries curated and short. Summarize outcomes, decisions, and next-context notes.
- Do not turn this into a minute-by-minute transcript.
- Prefer updating the current session entry while work is fresh instead of reconstructing it later.
- Record why a decision was made when that context will matter later.

Good content:

- what changed
- important design or cleanup decisions
- what was intentionally deferred
- what the next session should know immediately

Avoid:

- every command that was run
- temporary dead ends unless they explain a final decision
- low-value narration of routine edits
- repeating release-note detail that already belongs in `CHANGELOG.md`

## Entry Template

```md
## YYYY-MM-DD

### Focus

- Short summary of the main work completed

### Key Decisions

- Decision and why it was made

### Deferred Or Open

- What was intentionally left for later

### Notes For Next Session

- Immediate context worth knowing before resuming work
```

## 2026-10-04

### Interactive website foundation

- Established a Routes-owned interactive shell while retaining public/authenticated SSR and Entra policy handling. Dashboard reads begin after interactive render; shared layouts no longer retain anonymous navigation-event handlers.
- Added scoped typed dashboard operations and a cancellable, serialized refresh lifecycle. Current readings and history have independent cadences; current-time presentation and content/window history revisions continue updating without new row counts.
- Provider failures retain independently successful sections and expose retry; an interactive dashboard boundary contains unexpected rendering failures. Browser fixtures use the actual host with test-owned clock/providers/authentication, without production testing switches.
- #405 owns history projection refinements. Deployment, hardware and exporter enablement remain separately authorized work.

## 2026-10-03

### Shared Development Process Pilot

- Added the shared issue/epic/review/PR procedures and explicit AGENTS/Claude/Copilot routing while preserving project engineering guidance in `docs/AGENT_PROJECT_GUIDANCE.md`.
- Standardized actual author/reviewer/model/effort provenance, validation performers, independently verified finding threads, and the draft -> review -> gated CI lifecycle.
- Kept main/squash and operational authorization boundaries; archived the fixed #187–#198 campaign instructions as history. Adoption is tracked by #394; metadata automation becomes active once adopted on main.
- Excluded the archived `HVO.Database.sqlproj` from the active solution at the owner's direction. Preserved its SQL reference files and direct file-reading tests; EF Core migrations remain authoritative. The active build retains the zero-warning requirement.
- The owner adopted PR #395 and its first hosted main build/test and Docker smoke passed. Live controller qualification exposed GitHub's additional contents-write requirement for draft conversion; the follow-up scopes that capability to the trusted metadata job and requires a live token check. Independent follow-up review also identified that Actions run/job metadata associates target-event runs with the PR head; the controller now separates that association from trusted base admission and tested-merge binding, with realistic success/failure regression coverage. Candidate CI stays read-only; branch-protection activation remains open until qualification finishes.

## 2026-08-18

### Home Assistant Core Setup Completion

- Closed the broad Home Assistant setup scope after validating Mosquitto, dedicated credentials, 15 Kasa parent integrations, managed YAML deployment, the reviewed ESPHome proxy definition and transport, and native H5074/H5075 `govee_ble` entities.
- Kept the HA telemetry exporter disabled with no production mappings or source claims.
- Moved permanent HVO Bluetooth proxy placement, temporary bridge retirement, HOME fallback-credential cleanup, and H5179 commissioning to focused follow-up issue #385.

### Direct Gateway Retirement Completion

- Archived the retired SolarAssistant outbox and data-protection volumes to `/home/roys/backups/hvo-issue-330` on `devPi5`.
- Verified both archives by SHA-256 and tar listing before deleting only the two explicitly named SolarAssistant Docker volumes.
- Confirmed Davis, JK BMS, EG4, and SmartShunt remained healthy and retained all active outbox and data-protection volumes.

## 2026-08-14

### Direct Gateway Retirement

- Retired the direct SolarAssistant and TP-Link/Kasa applications, tests, deployment definitions, containers, and images.
- Confirmed the active direct headless vNext collectors are Davis, JK BMS, EG4, and SmartShunt.
- Confirmed Home Assistant owns Kasa and Govee acquisition and presentation.
- Kept the implemented HA exporter intentionally disabled with no production mappings or source claims.

## 2026-05-24

### Repo Cleanup And Review Follow-Through

- Merged PR `#81` (`Roll out Pi gateways and JK session model`)
- Resolved all visible PR review threads and posted reply comments before merge
- Landed JK BMS review fixes including:
  - coordinator-managed BLE connection lifetime cleanup
  - reconnect backoff behavior aligned with `NextPollAt`
  - live-test initialization aligned with the new connection model
  - stale lifecycle/docs cleanup across JK BMS transport and client code

### Repository Cleanup Decisions

- Removed the deferred RabbitMQ/Service Bus ingest POC from the active repo
- Removed obsolete helper/preview files:
  - `update_gist.sh`
  - `docker-compose.design.yml`
  - `scripts/sync-davis-mock-preview.sh`
- Kept `HVO.Staging` because it still contains active bridging code used by the Davis app and unit tests
- Kept `HVO.WebSite.Themes` because it is a live shared asset project, even though the `.csproj` is intentionally small

### Tooling Structure

- Moved `HVO.Tools.JkBleConsole` from `src/` to `tools/` because it is a standalone operational utility, not an app/runtime project
- Updated solution and doc references to the new path

### Documentation Cleanup Decisions

- Identified the docs set as a mix of:
  - current reference docs
  - active discovery notes
  - old planning/brainstorm material
- Added this `PROJECT_HISTORY.md` file to preserve session context and decisions separately from `CHANGELOG.md`
- Reorganized docs so active reference stays visible and older planning docs move under `docs/archive/`

### Notes For Next Session

- Consider whether `HVO.DataModels.csproj` still needs `Microsoft.EntityFrameworkCore.Tools`
- Revisit whether `HVO.Staging` can be retired by promoting the staged functionality into package dependencies
- Audit remaining deploy compose files for whether each one still matches current operational reality

### JK BMS devPi5 Baseline

- Deployed the JK BMS session-instrumentation changes to `devPi5` and collected a 20-minute baseline log window using the built-in adapter (`hci0`)
- Measured log results in that window:
  - `BLE session established`: `7`
  - `BLE connected to`: `7`
  - `Transport disconnected`: `0`
  - `BLE session request failed`: `0`
  - `BLE exchange failed`: `0`
- The observed `hci0` baseline does not show reconnect churn during the captured 20-minute period; each of the 7 configured BMS devices established a session once and stayed connected through the window
- The USB adapter (`hci1`, UGREEN BT6.0) remained present but would not power on after reboot (`btmgmt`/`bluetoothd` returned status `0x03`), so no USB-vs-built-in comparison baseline was collected

### Deferred Or Open

- Pull the new JK BMS UI/session counters from `devPi5:5200` to confirm whether the in-memory counters match the log-based baseline
- Decide whether the audible BMS beeps reported earlier were a transient host/device issue or occur only under longer runtimes not covered by the 20-minute sample

### Follow-Up Findings

- Pulled the JK BMS status UI on `devPi5:5200` after the baseline and confirmed all 7 configured devices were still connected with `up 1`, `drop 0`, and `fail 0`, matching the log-based baseline
- Confirmed the current JK poll interval is `30s`; the beep reports fit the regular polling cadence better than reconnect churn because the measured baseline showed no disconnect or exchange-failure events
- Checked `bluetoothctl devices Connected` on `devPi5`; only the 7 JK BMS devices were connected, so nearby devices such as the SmartShunt and Govee sensors were not consuming local Bluetooth connection slots at the time of measurement
- Checked current `SolarAssistant` activity on `devPi5`; logs showed HTTP and MQTT activity only, with no evidence that it was using the local Bluetooth controller
- Investigated the USB adapter further and identified `hci1` (`33fa:0012`, UGREEN BT6.0) as a likely kernel support gap on the current Pi kernel (`6.12.87+rpt-rpi-2712`), based on the persistent power-on failure (`status 0x03`) and kernel message `Unexpected continuation: 1 bytes`
- Current conclusion: the built-in adapter (`hci0`) is stable for the present 7-device JK workload, and a second Bluetooth bus is not currently justified without evidence that additional locally connected BLE devices create contention

### Notes For Next Session

- Treat the next real decision point as adding more locally connected BLE clients on `devPi5`, not merely having more Bluetooth devices in radio range
- If a second bus becomes necessary later, validate the user-ordered Panda adapter instead of spending more time on the unsupported `33fa:0012` device

### Active BLE Contention Follow-Up

- Verified that `devPi5` is currently using Wi-Fi on `wlan0` at `5200 MHz` (5 GHz); `eth0` was down during the BLE contention tests, so the observed instability was not a 2.4 GHz coexistence issue
- Ran a held `bleak` connection to the SmartShunt (`E2:21:F0:89:A7:C0`) for 60 seconds while JK BMS remained live; BlueZ showed `SmartShunt + 7 JK` connected at the same time and JK stayed stable during that idle connection hold
- Moved beyond idle connection state and performed repeated SmartShunt GATT reads (12 reads over ~60 seconds from characteristic `97580002-ddf1-48be-b73e-182664615d8e`); this introduced a JK disconnect/reconnect on `bank-1b`, showing that active SmartShunt traffic on the shared adapter can disturb the JK workload
- Held a persistent Govee connection to `Govee_H5074_8D05` (`A4:C1:38:80:8D:05`); the Govee dropped after ~30 seconds and JK `bank-1c` later experienced timeout and `Not connected` failures followed by reconnects
- Practical conclusion from the live tests: `hci0` on `devPi5` is good for the current `7` JK workload and can tolerate an additional idle SmartShunt connection, but it is not currently robust for adding active non-JK BLE traffic such as SmartShunt reads or persistent Govee sessions on the same adapter

### POC Direction Decision

- The next POC should be a SmartShunt integration path
- Because the Panda adapter has not arrived yet, immediate SmartShunt development should happen on `hci0` with the JK BMS process stopped so the adapter remains stable during protocol and polling work
- Started that SmartShunt path by adding `tools/HVO.Tools.SmartShuntBleConsole`, a minimal `Linux.Bluetooth` console probe for connect/service enumeration, repeated characteristic reads, and raw notification capture
- Used the new SmartShunt console on `devPi5` after stopping `jkbms-hvo-jkbms-1` and clearing two lingering JK BLE links from BlueZ; with JK inactive, `hci0` was stable for direct SmartShunt probing
- Confirmed SmartShunt service/characteristic map on `E2:21:F0:89:A7:C0`, including:
  - `97580001-ddf1-48be-b73e-182664615d8e`
    - `97580002-ddf1-48be-b73e-182664615d8e` `[read]`
    - `97580003-ddf1-48be-b73e-182664615d8e` `[write,notify]`
    - `97580004-ddf1-48be-b73e-182664615d8e` `[write]`
    - `97580006-ddf1-48be-b73e-182664615d8e` `[read,write,notify]`
  - `306b0001-b081-4037-83dc-e59fcc3cdfd0`
    - `306b0002-b081-4037-83dc-e59fcc3cdfd0` `[read,write-without-response,notify]`
    - `306b0003-b081-4037-83dc-e59fcc3cdfd0` `[write-without-response,notify]`
    - `306b0004-b081-4037-83dc-e59fcc3cdfd0` `[write-without-response,notify]`
- Reconfirmed that repeated reads from `97580002-ddf1-48be-b73e-182664615d8e` return a stable 20-byte payload (`ff160100ff4602008c0096a15000f2ffffff0000`), matching the earlier `bleak` test but now on a clean adapter with JK stopped
- Subscribed to notifications on `97580003-ddf1-48be-b73e-182664615d8e` for 30 seconds; the subscription succeeded but produced no packets without a triggering write, so passive notify alone is not enough to advance protocol discovery on that characteristic
- Looked at GitHub references while continuing local probing:
  - `batmon-ha` appears to implement only Victron's public SmartShunt GATT service (`6597...`), not the private `9758...` / `306b...` path seen on this device
  - reverse-engineered VictronConnect projects point to init/keepalive writes on `306b0002` / `306b0003` and notifications on `306b0003` / `306b0004`
  - ESPHome/Victron reference material indicates the supported connected-BLE path requires enabling the third-party GATT protocol in VictronConnect and pairing with a passkey before use
- Extended `HVO.Tools.SmartShuntBleConsole` with:
  - connect retry handling
  - generic `write` support
  - scripted `victron-init` mode for the repo-backed `306b...` init sequence
- Ran the scripted `victron-init` probe on `devPi5`:
  - `306b0003` and `306b0004` accepted notification subscriptions
  - `306b0002` notify start failed with ATT error `0x0e`
  - the SmartShunt disconnected before the first init write completed, returning `org.bluez.Error.Failed: Not connected`
- Probed the public `6597...` path directly:
  - reading `6597ed8d-4bda-4c1e-af4b-551c4cf74769` failed with ATT error `0x0e`
  - writing public keepalive `ffff` to `6597ffff-4bda-4c1e-af4b-551c4cf74769` also failed with ATT error `0x0e`
- Current conclusion: both the public `6597...` telemetry path and the private VictronConnect-style write/init path appear gated in the current unpaired state; the next meaningful unlock is likely SmartShunt protocol-enable/pairing rather than more blind characteristic poking
- Confirmed the SmartShunt pairs successfully from `devPi5` through BlueZ once an interactive agent is active; passkey `000000` worked and the device became `Paired: yes`, `Bonded: yes`
- After pairing, the public `6597...` SmartShunt GATT path unlocked:
  - writing little-endian `20000` (`204e`) to `6597ffff-4bda-4c1e-af4b-551c4cf74769` succeeded as keepalive
  - direct reads succeeded for the main public telemetry characteristics:
    - `6597ed8d` -> `0015` -> `53.76 V`
    - `6597ed8c` -> `9d5a0000` -> `+23.197 A`
    - `6597ed8e` -> `db04` -> `+1243 W`
    - `6597eeff` -> `70f9ffff` -> `-168.0 Ah`
    - `65970fff` -> `dc23` -> `91.80 %`
- Without keepalive, the paired public read session still dropped after a couple of reads; keepalive remains part of the viable public-GATT session model
- A short notify test on paired public characteristic `6597ed8d` started successfully, but the 25-second capture produced no notification packets during that window despite periodic keepalive writes; immediate next protocol work should treat successful read + keepalive as the proven baseline and not assume notifications are required yet
- Extended `HVO.Tools.SmartShuntBleConsole` with decoded public SmartShunt commands:
  - `public-snapshot`: single paired connection, send keepalive, read and decode all known public `6597...` fields
  - `public-monitor`: single paired connection, send keepalive, subscribe to all known public `6597...` notify/read fields, decode notifications, and print periodic state snapshots
- Validated `public-snapshot` on `devPi5`; decoded public fields currently observed:
  - `soc`: `91.90 %`
  - `voltage`: `53.76 V`
  - `power`: about `-1251 W`
  - `current`: about `-23.28 A`
  - `consumed_ah`: about `-166.1 Ah`
  - `starter_voltage`: `n/a`
  - `val2`: `n/a`
  - `val3`: `n/a`
  - `temperature?`: `n/a`
  - `remaining_time`: `n/a`
- Validated `public-monitor` on `devPi5`; notifications are active and useful on the public service after pairing and keepalive:
  - frequent live updates arrived for `current` and `power`
  - `voltage` also changed between `53.76` and `53.77 V`
  - `consumed_ah` updated from `-166.0 Ah` to `-165.9 Ah`
  - `soc` remained stable at `91.90 %` during the short monitor window
  - the currently optional/secondary fields (`starter_voltage`, `val2`, `val3`, `temperature?`, `remaining_time`) remained `n/a` for this device/session
- Current practical conclusion: the supported public paired GATT path is now understood well enough to serve as the first real SmartShunt integration baseline; reverse-engineering the private `9758...` / `306b...` path is no longer required for initial telemetry collection
- Reviewed the iOS VictronConnect screenshots in `docs/screenshots/`; they confirm the app exposes much more than the public `6597...` telemetry service:
  - live status fields we already match: SoC, voltage, current, power, consumed Ah
  - additional app-visible surfaces not yet covered by public GATT: remaining time, history counters, min/max statistics, battery settings, alarms, monitor mode, VE.Smart networking, product info, and Bluetooth-interface firmware update
  - screenshot evidence confirms `Bluetooth GATT service (experimental)` is enabled on the device, SmartShunt firmware is `v4.14`, and the Bluetooth interface is separately versioned (`v2.46`, update available to `v2.52`)
- Re-probed the paired private surfaces on `devPi5`:
  - `97580006-ddf1-48be-b73e-182664615d8e` now returns changing 8-byte values on each read (`44c207a6d095e1a5`, `339e6c3b77f73657`, `b26ed9b92a8256dd`), reinforcing that it behaves more like session/token/module state than user telemetry
  - `306b0002-b081-4037-83dc-e59fcc3cdfd0` reads a stable 7-byte value: `00040001de4a00`
- Retried the scripted `victron-init` sequence after pairing and, for the first time, received a sustained private stream before disconnect:
  - `306b0002` emitted repeated `f901` ACK frames and the stable `00040001de4a00` frame
  - `306b0003` emitted structured frames containing private IDs including `ed8c`, `ed8d`, `ed8e`, `ed8f`, `ec5a`, and `0308`
  - observed examples:
    - `080319ed8c44...` aligns with current-like data
    - `080319ed8d42...` aligns with voltage-like data
    - `080319ed8e42...` aligns with power-like data
    - `080319ed8f42...` appears to be an additional live/private metric not exposed in the documented public GATT set
    - `080319ec5a44...` and `080319030844...` appear to be additional private counters/state values worth correlating with app-only screens such as history/settings
  - the session still eventually dropped on a later `f941` keepalive write (`org.bluez.Error.Failed: Not connected`), so private-path stability/decoding is still incomplete
- Current conclusion after screenshots plus paired private probing:
  - public `6597...` GATT is the right baseline for supported live telemetry collection
  - private `306b...` is now confirmed as the active hidden data bus carrying additional app-visible values beyond the public service
  - `9758...` remains a likely Bluetooth-module/session/update sideband rather than the main history/settings bus
- Once the Panda adapter is available, rerun the validation as an adapter-split experiment to test whether `JK on one bus + SmartShunt on another` removes the measured contention
- Deferred for now: persistent Govee integration, because it already showed worse contention behavior than SmartShunt and does not look like the right next stability target on the shared onboard adapter
- Deferred for now: TP-Link and Digital Loggers, because they are network-based and do not help resolve the immediate BLE topology question that now has real measured evidence
- Deferred for now: a unified edge outbox/proxy design, because the higher-value short-term decision is still hardware/runtime topology for mixed BLE workloads; that proxy question is better revisited once the BLE placement strategy is settled

### SmartShunt Consolidated Summary

- Built the SmartShunt tool/runtime surface in this repo:
  - `tools/HVO.Tools.SmartShuntBleConsole/`
  - `src/HVO.Hardware.VictronSmartShunt/`
  - `tests/HVO.Hardware.VictronSmartShunt.Tests/`
  - `deploy/pi-gateways/smartshunt/`
- Validated the public paired `6597...` path as the production baseline for live telemetry.
- Validated the private `306b...` path as optional enrichment for product metadata, history/statistics, and internal/private state.
- Repeatedly deployed the SmartShunt gateway to `devPi5` on port `5400` and verified end-to-end forwarding to the website power API.

### SmartShunt Validated Findings

- Core live telemetry is correct and correlates with the Victron app for:
  - SoC
  - voltage
  - current
  - power
  - consumed Ah
- Stable private product metadata now includes:
  - firmware `v4.25`
  - serial `HQ211369CMY`
  - raw product/family metadata `0089a3fe`
  - raw device-id-like metadata `3aaa47ca2438e600`
  - stable metadata value `497`
- Stable private history/stat fields now include:
  - deepest / last / average discharge
  - total charge cycles
  - full discharges
  - cumulative Ah drawn
  - min/max battery voltage
  - time since last full
  - synchronizations when present
  - low/high voltage alarm counters
  - min/max starter voltage
  - discharged / charged energy
- Useful internal/private runtime fields now surfaced locally:
  - `StreamingCounter`
  - `ChargeStatusCoarsePercent`
  - `CurrentCoarseA`

### SmartShunt Key Decisions

- Keep public `6597...` as the production baseline.
- Keep private `306b...` as optional enrichment only.
- Keep SmartShunt BLE work read-only until a real VictronConnect capture exists.
- Treat SmartShunt SoC drift versus the JK fleet as a device-state/synchronization issue, not a collector decode issue.
- Keep `CurrentCoarseA` as local UI-only fallback/display context, not forwarded primary telemetry.

### SmartShunt Deferred / Blocked

- No verified safe BLE sync/write path exists yet.
- Battery settings and stable alarm-threshold configuration reads are still not available in the current private session shape.
- Shared-adapter `hci0` contention remains real for mixed `JK + active SmartShunt` workloads.
- Tested USB Bluetooth adapters on `devPi5` have not produced a usable second BLE bus; continue assuming `hci0` only.

### Notes For Next Session

- Treat the current SmartShunt surface as stable unless a new field has strong live evidence.
- Use `roys@devPi5` and preserve remote `deploy/pi-gateways/smartshunt/.env` during sync/deploy work.
- Key local status endpoint: `http://localhost:5400/status`
- Current SmartShunt tests pass `19/19`.
- If sync/settings work resumes later, get a real VictronConnect Bluetooth capture first.

## 2026-06-13

### Hybrid Multi-Model Code Review Infrastructure

Historical session record: the June availability, prices, model choices and
next-session instructions below describe that experiment. They are not current
review policy or a live catalog. #417 consolidates the prep roles into the
current [OpenCode adapter](../.opencode/agents/review-prep.md); assign final review
through the [canonical procedure](../.agents/skills/hvo-code-review/SKILL.md)
using actual live capabilities/settings and required independence.

- Built a hybrid review workflow (low-cost prep + GPT validation) with skill files and subagents.
- Added OpenCode Zen free prep agents: DeepSeek V4 Flash, Nemotron 3 Ultra, MiMo V2.5.
- Added OpenCode Go prep agents: DeepSeek V4 Pro, Qwen3.7 Plus, MiniMax M3 (need restart to activate).
- Ran all 3 Zen free agents and 3 explore-type agents across repo groups.
- Created `MODEL_RANKING.md` for running model comparison.

### Key Decisions

- **Daily driver (free):** DeepSeek V4 Flash Free (Zen) — best signal-to-noise among free models.
- **Final validation:** GPT 5.5 for critical reviews.
- **Go models:** DeepSeek V4 Pro, Qwen3.7 Plus, MiniMax M3 tested on 2026-06-13 across all architecture groups.
- Token telemetry plugin removed (caused OpenCode hangs). No live event plugins active.

### Files Created

- `.opencode/agents/review-prep.md` (parameterized — swap model field for any provider)
- `.opencode/agents/qwen-review-prep.md`
- `.opencode/agents/qwen-review-resolution-prep.md` (removed in CSS alias cleanup)
- `MODEL_RANKING.md` (running ranking updated with each reviewed model)
- `code-review-qwen-pre/Zen-{model}-{area}-Review.md` (3 Zen prep outputs)
- `code-review-gpt55/HVO.WebSite.v9-Final-Review.md` (GPT validated findings)
- `code-review-comparison/MODEL_COMPARISON.md` (cross-model comparison)

### Deferred / Open

- Token usage tracking — no exact provider telemetry; all estimates.

### Notes For Next Session

- Go subagent type is available: `review-prep` — parameterized, swap model field for any provider.
- Run prep agent, then GPT validate, then update `MODEL_RANKING.md`.
- Explore-agent prep outputs saved under tool-output files (Gateways: `tool_ec257b53e001...`, Hardware: `tool_ec257e75b001...`).
- Current ranking: DeepSeek V4 Pro > DeepSeek V4 Flash Free > Qwen3.7 Plus > MiniMax M3 > Qwen3 Coder Next > Nemotron 3 Ultra > MiMo V2.5 Free.

## 2026-10-04 — Ingest trust boundaries (#401)

Source authority now covers every source-bearing weather/power single, batch, archive and snapshot write through a shared check; unreserved legacy ingest remains supported and SmartShunt keeps exact ownership. Authentication cache entries carry hard UTC expiry and bounded deadlines using the injected clock. Forwarded header configuration validates explicit peers/networks at startup, preserves loopback defaults even with the framework host switch, and uses one trusted hop without a manual scheme override. Independent F1 review exposed duplicate framework/application forwarding: the application now defers to the active host startup filter so all switch combinations consume one hop, qualified by real-environment trusted multihop and OIDC regressions. The route discovery matrix and actual-host OIDC tests document the security boundary; deployment topology and Entra rollout verification remain operator work. See [ingest trust boundaries](development/ingest-trust-boundaries.md).

SQL-provider acceptance reuses the same route inventory and payloads against the disposable #409 fixture. It checks actual denied/authorized persistence, mixed batches, case/accent/trim aliases, competing reservations and hard UTC expiry with primed and empty authentication caches. SmartShunt's already-trimmed payload contract is preserved. Test collation changes remain confined to owned disposable databases.
