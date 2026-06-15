# Task: Repo-wide review 03 — Error handling, logging, telemetry, and diagnostics

**GitHub issue:** #191
**Type:** Review only — do not make code changes, do not open a PR
**Output:** Post report as a comment on issue #191 if possible; otherwise write to `.codex/reports/191-logging.md`

## Instructions

Perform a focused repository-wide review of error handling, logging, telemetry, and diagnostics.

## Focus areas

- Swallowed exceptions and catch-all handlers that hide root causes
- Missing logging around critical workflows (ingest, data forwarding, auth)
- Incorrect log levels (errors logged as info, debug noise in production)
- Logs that expose sensitive data (API keys, passwords, PII)
- Missing correlation IDs or trace IDs across service boundaries
- Missing telemetry around external HTTP calls and MQTT connections
- Missing timing/performance diagnostics around slow operations
- Inconsistent error responses from API endpoints
- User-facing errors that expose internal exception details
- Areas where a production failure would be hard to diagnose
- Background worker failure modes — do they log and continue, or crash silently?

## Key context for this repo

- Gateway apps forward data to Azure via outbox pattern — failures must be logged with enough context to diagnose
- MQTT and REST polling workers run continuously — connectivity errors are expected and must not crash the worker
- Blazor circuit crashes (JS interop exceptions) are a known failure mode — check that `OnAfterRenderAsync` JS calls are in try-catch
- Health check endpoints must reflect actual device connectivity, not just process liveness

## Output format

1. Executive summary
2. Top diagnostic blind spots
3. Logging/telemetry consistency observations
4. Sensitive logging risks
5. Suggested standard logging pattern for this repo
6. Phased remediation plan

## Key files to check

- All background workers in gateway projects (`Workers/`, `Services/`)
- `src/HVO.WebSite.v9/` — API endpoint error handling and logging
- `src/HVO.Hardware.*/` and `src/HVO.Gateway.*/` — MQTT, REST, outbox workers
- `src/HVO.WebSite.Themes/Components/Charts/` — JS interop try-catch
- Health check registrations in each project's `Program.cs`
