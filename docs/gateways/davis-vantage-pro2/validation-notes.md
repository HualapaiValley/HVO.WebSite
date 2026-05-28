# Davis Vantage Pro2 Validation Notes

This document tracks evidence, open questions, and validation steps for the Davis gateway.

## Confirmed So Far

| Area | Evidence | Confidence |
|------|----------|------------|
| CRC-CCITT framing | Official PDF, WeeWX CRC table, HVO `CrcCalculator`. | High |
| LOOP packet size and parsing | Official PDF, WeeWX, CumulusMX, HVO `Loop2Packet`. | High |
| Archive page and record geometry | Official PDF, WeeWX, CumulusMX, HVO `ArchiveRecord`. | High |
| Rain bucket conversion | Official PDF, WeeWX, CumulusMX, HVO code. | High |
| WeatherLink IP pacing/prefix quirks | HVO code comments/behavior plus mature driver patterns. | Medium-high |
| Display-unit independence from raw protocol units | Official protocol and mature driver behavior. | Medium-high until tested on HVO hardware. |

## Live Validation Checklist

| Validation | Why it matters | Status |
|------------|----------------|--------|
| Change console temperature display units and compare raw LOOP/archive bytes. | Confirms HVO can normalize from protocol units independent of display settings. | Open |
| Change console wind display units and compare raw LOOP/archive bytes. | Confirms wind parser assumptions. | Open |
| Change console barometer display units and compare raw LOOP/archive bytes. | Confirms pressure parser assumptions. | Open |
| Change console rain display units without changing bucket type and compare raw rain counters. | Confirms display unit is separate from bucket conversion. | Open |
| Validate rain bucket type against actual hardware bucket. | Prevents rain totals/rates from being scaled wrong. | Open |
| Validate archive UTC conversion across DST/timezone settings. | Prevents wrong historical timestamps. | Open |
| Validate `DMPAFT` behavior when requested timestamp predates circular buffer. | Decides whether startup catchup should enable fallback. | Open |
| Add CRC example unit test for `0xCEC6 0x03A2 -> 0xE2B4`. | Locks cross-checked CRC behavior. | Open |

## Test Strategy

Use three test levels. Do not treat mock-only coverage as proof that the Davis protocol behavior is correct.

| Level | Purpose | Tooling | Should cover | Limitations |
|-------|---------|---------|--------------|-------------|
| Unit tests | Validate pure logic and parsing. | xUnit plus direct parser/model calls. | CRC vectors, LOOP packet parsing, archive record parsing, rain conversion, timestamp conversion, forecast mapping, settings decode helpers. | Cannot prove transport timing, command sequencing, or hardware side effects. |
| Mocked service tests | Validate HVO orchestration around interfaces/classes. | Moq/fakes for station/outbox/database dependencies. | Worker retry behavior, outbox enqueue behavior, current-reading updates, API/controller behavior. | Mocks can accidentally encode wrong protocol assumptions. |
| Protocol simulator tests | Validate command/response sequencing without live hardware. | In-process TCP fake WeatherLink/Davis console or scripted stream. | Wakeup, ACK/NAK, LF/CR-prefixed ACK, CRC failures, `LPS 1 1`, `LPS 2`, `DMPAFT`, `SETTIME`, EEPROM read/write command flow, timeout/reconnect handling. | Simulator must be kept aligned with official protocol and live observations. |
| Live hardware tests | Validate actual Davis console behavior. | Manual or gated integration tests against the deployed console. | Display-unit behavior, rain bucket settings, write operations, archive catchup edge cases, timezone/DST behavior, WeatherLink IP quirks. | Requires hardware, can be slow/risky, and write tests need safety controls. |

## Test Coverage Matrix

| Area | Unit | Mocked | Simulator | Live | Priority | Notes |
|------|------|--------|-----------|------|----------|-------|
| CRC known vector | Needed | N/A | Optional | N/A | High | Add `0xCEC6 0x03A2 -> 0xE2B4`. |
| LOOP1 parser golden packet | Needed | N/A | Optional | Optional | High | Include battery, forecast, sunrise/sunset, monthly/yearly totals, extra sensors. |
| LOOP2 parser golden packet | Needed | N/A | Optional | Optional | High | Include derived values, pressure, wind, rain windows, solar/UV. |
| Dash/null sentinel handling | Needed | N/A | Optional | Optional | High | Cover `0xFF`, `0x7FFF`, `0xFFFF`, calm/dash wind. |
| Rain bucket conversion | Needed | N/A | Optional | Live confirm bucket | High | Cover 0.01in, 0.2mm, 0.1mm. |
| Archive record parser | Needed | N/A | Needed | Optional | High | Include interval rain and 16-point wind direction conversion. |
| DMPAFT page download | Optional | Optional | Needed | Needed | High | Include page count/start index, ACK prompting, CRC failure, empty response. |
| Console wakeup and ACK prefix | N/A | Optional | Needed | Needed | High | WeatherLink IP can prefix ACK with LF/CR. |
| LOOP interruption for commands | N/A | Optional | Needed | Needed | High | Validate `RequestLoopInterruption`, cancellation token, command lock behavior. |
| Worker reconnect/backoff | N/A | Needed | Needed | Optional | Medium | Simulate read failures and repeated protocol errors. |
| Outbox enqueue/dedupe | Optional | Needed | Optional | Optional | Medium | Confirm archive/live separation and timestamp keys. |
| Unit/display setting independence | N/A | N/A | Optional | Needed | High | Must be live-validated before API lock. |
| Set console time | Optional payload test | Optional | Needed | Needed with safety | High | Verify payload format, CRC, ACK, and archive timestamp implications. |
| EEPROM writes | Optional payload tests | Optional | Needed | Needed with safety | High | Include read-back verification plan. |
| Alarm writes/clears | Optional encode tests | Optional | Needed | Needed with safety | Medium | High-risk behavior; keep local-only. |
| Archive clear | N/A | Optional | Needed | Only with explicit manual approval | Low | Destructive; should usually be simulator-only. |

## Protocol And Implementation Gap Register

| Gap | Type | Risk | Proposed resolution | Status |
|-----|------|------|---------------------|--------|
| Command-by-command Davis protocol coverage is not documented. | Spec | We may think we support more of the protocol than we do. | Add official command coverage table to `manufacturer-protocol.md`. | Open |
| Write paths are structurally implemented but not all live-validated. | Implementation/test | Incorrect writes could change console settings or corrupt expected behavior. | Add simulator tests, live validation checklist, read-back verification, and safety gates. | Open |
| Unit normalization is implicit in property names. | API design | Future local/cloud consumers may confuse raw protocol units, normalized units, and console display settings. | Redesign DTO/API names before lock-in. | Open |
| Mock-only tests would not catch protocol sequencing defects. | Test design | False confidence in transport/command behavior. | Build a protocol simulator test harness. | Open |
| Startup archive catchup does not enable `fallbackOnEmpty`. | Implementation | Edge cases may miss older available archive data. | Decide behavior after simulator/live DMPAFT validation. | Open |
| Destructive/config writes lack final exposure policy. | Safety | Unsafe UI/cloud exposure. | Local auth, confirmation, audit logging, read-back, and command allowlist. | Open |

## Definition Of Done For Davis Logic

Do not mark Davis logic as fully implemented until these criteria are satisfied or explicitly deferred with rationale.

| Criterion | Required for full implementation? | Current status | Notes |
|-----------|-----------------------------------|----------------|-------|
| Manufacturer command coverage table is complete against the official PDF. | Yes | In progress | Current table is seeded but not a full official command transcription. |
| Every HVO-supported read operation has unit/golden-data coverage. | Yes | Open | LOOP1, LOOP2, archive records, EEPROM/settings, diagnostics. |
| Every HVO-supported protocol flow has simulator coverage. | Yes | Open | Wakeup, ACK/NAK, ACK prefix, CRC failures, LOOP streaming, DMPAFT, write commands. |
| Every HVO-supported write operation is simulator-tested. | Yes | Open | Include payload shape, ACK handling, read-back path, failure behavior. |
| Risky writes are live-tested or marked unsupported/deferred. | Yes | Open | Time, barometer, archive interval, EEPROM, alarms, archive clear. |
| Unit/display behavior is live-validated. | Yes | Open | Temperature, wind, barometer, rain display units vs raw protocol units. |
| Local DTO/API model separates raw/vendor, HVO-normalized, and display settings. | Yes before API lock | Open | Current `*F`, `*Mph`, `*Inches` names are implementation debt. |
| Live and archive contracts are reviewed separately. | Yes before cloud schema lock | Open | Archive interval records should not be collapsed into live observations without preserving semantics. |
| Rain semantics are explicitly modeled. | Yes before cloud schema lock | Open | Rate, daily, storm, rolling windows, monthly/yearly, and archive interval fields remain distinct. |
| Safety gates exist for destructive/configuration writes. | Yes before UI exposure | Open | Local auth, confirmation, audit log, read-back verification, and allowlist. |
| Reconnect/interruption behavior is simulator-tested. | Yes | Open | Covers command interruption during LOOP and recovery from unknown console mode. |
| Live validation checklist is complete or explicitly deferred. | Yes | Open | Deferrals must include risk and follow-up owner. |

## Definition Of Done For Davis Documentation

| Criterion | Status | Notes |
|-----------|--------|-------|
| Documentation is split into manufacturer protocol, HVO implementation, HVO API contracts, and validation notes. | Done | Current docs follow the gateway documentation set pattern. |
| Manufacturer protocol doc avoids HVO design decisions except provenance/support status. | In progress | Command coverage still needs full official transcription. |
| HVO implementation doc includes project layout, classes, public methods, samples, worker flow, command flow, and decision log. | Done | Keep updated as implementation changes. |
| HVO API contracts doc lists local endpoints, outbox payloads, central mappings, and stream decisions. | Seeded | Needs final API/outbox versioning once design is locked. |
| Validation notes include unit, mocked, simulator, and live test expectations. | Done | Convert open items to tests/work items during implementation. |

## Known Issues And Quirks

| Issue | Evidence | Impact | Workaround | Validation needed |
|-------|----------|--------|------------|-------------------|
| HVO unit-specific property names may hide protocol/display-unit nuance | Parser exposes `*F`, `*Mph`, `*Inches` while console stores display unit bits. | Public API consumers may confuse protocol units, console display preferences, and HVO-normalized units. | Keep all three concepts explicit in docs/API. | Live test display-unit changes on the real console before locking public local API. |
| Rain fields have different semantics | LOOP exposes rate, daily, 15-min, hour, 24-hour, storm, monthly, yearly; archive exposes interval rain. | Cannot map all to one `RainfallInches` column. | Keep separate fields/streams. | Decide central schema names and reset/cadence semantics. |
| LOOP1/LOOP2 have complementary fields | Worker merges LOOP1 cache with LOOP2. | Missing LOOP1 refresh can affect battery/forecast/monthly totals. | Cache LOOP1 per batch. | Validate stale behavior. |
| Console local time differs from host timezone | Archive conversion code strips `DateTimeKind.Local`. | Wrong archive UTC if offset is wrong. | Use console UTC offset. | Validate timezone/DST settings. |
| WeatherLink IP/TCP has timing quirks | Current client adds a 50 ms send delay and handles LF/CR-prefixed ACK. | Without pacing/prefix handling commands can fail intermittently. | Preserve pacing and prefix tolerance. | Validate after any transport refactor. |
| `DMPAFT` empty response edge case | Code comments note firmware can return zero pages if request predates circular buffer. | Startup catchup can miss oldest available archive records if fallback is not enabled. | Use fallback path for manual/full catchup where appropriate. | Decide whether startup catchup should enable `fallbackOnEmpty`. |

## HVO-Derived Weather Calculation Candidates

These are not Davis protocol fields unless explicitly stated in [manufacturer-protocol.md](manufacturer-protocol.md).

| Calculation | Inputs | Candidate formula/source | Use | Status |
|-------------|--------|--------------------------|-----|--------|
| Dew point fallback | temperature, relative humidity | Magnus/Tetens-style approximation | Use only when Davis `DewPointF` is absent, or for archive records that lack dew point. | Candidate; mark as HVO-derived. |
| Heat index fallback | temperature, relative humidity | NOAA/NWS Rothfusz regression with standard applicability constraints | Use only when Davis `HeatIndexF` is absent. | Candidate; mark as HVO-derived. |
| Wind chill fallback | temperature, wind speed | NOAA/NWS wind chill equation with standard applicability constraints | Use only when Davis `WindChillF` is absent. | Candidate; mark as HVO-derived. |
| Dew spread | temperature minus dew point | Simple difference | Observatory risk indicator for condensation/dew. | HVO-specific. |
| Dew risk | dew spread plus trend/context | HVO rules TBD | Observatory-specific alerting/UI. | Needs validation. |

Rules for derived values:

- Prefer Davis console-derived live values from LOOP2 when present.
- Do not overwrite or relabel Davis values with HVO calculations.
- Persist/display provenance such as `source=DavisConsole` vs `source=HvoCalculated` if derived values enter an API or central schema.
- Archive-derived dew point, heat index, wind chill, dew spread, or dew risk are not Davis archive fields and must be labeled as calculated.

## Open Questions

| Question | Why it matters | Status |
|----------|----------------|--------|
| Which Davis fields belong in central historical weather vs local-only diagnostics? | Prevents oversized or ambiguous central schema. | Open |
| Should archive records get a separate central payload/table from live LOOP records? | Archive interval semantics differ from live readings. | Open |
| Which console write operations should HVO support long-term? | Safety and audit requirements. | Open |
| Do real-console display-unit changes leave raw LOOP/archive bytes unchanged as documented? | Confirms the PDF/open-source-driver interpretation on HVO hardware. | Needs live validation |
| Should the local API expose both raw protocol values and normalized values? | Helps future-proof against unit/display setting changes. | Open |
| Should HVO calculate archive dew point, heat index, wind chill, dew spread, or dew risk? | These are useful but not Davis archive protocol fields. | Open |
| Should startup archive catchup enable DMPAFT full-archive fallback on zero-page responses? | Could improve recovery from circular-buffer edge cases. | Open |
