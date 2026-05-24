# Project History

Purpose: keep a lightweight working history that explains what changed, what was decided, and what to revisit later. This is not a release changelog.

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
