# Task: Repo-wide review 04 — Async, threading, performance, and reliability

**GitHub issue:** #192
**Type:** Review only — do not make code changes, do not open a PR
**Output:** Post report as a comment on issue #192 if possible; otherwise write to `.codex/reports/192-async-perf.md`

## Instructions

Perform a focused repository-wide review of async usage, threading, performance, scalability, and production reliability.

## Focus areas

- Sync-over-async: `.Result`, `.Wait()`, `GetAwaiter().GetResult()` in request paths
- Blocking calls in Blazor server request paths or background workers
- Long-running operations without timeouts (HTTP calls, MQTT, database)
- Thread pool starvation risks from synchronous blocking
- Lock contention or unsafe shared mutable state
- Missing `CancellationToken` flow — workers, EF Core calls, HTTP calls
- External HTTP calls without explicit timeouts
- Unsafe or unbounded retries
- N+1 EF Core query patterns
- Unbounded queries or missing pagination on large result sets
- Background worker lifecycle — do they honor `CancellationToken` on shutdown?
- `StateHasChanged()` called too aggressively from timers
- `IAsyncDisposable` missing on components with timers or JS object references

## Key context for this repo

- All gateway apps run background workers that poll devices via HTTP/MQTT — these need timeouts and CancellationToken throughout
- Blazor Server SSR — blocking the thread pool is more dangerous than in APIs
- EF Core — all queries must be async (`ToListAsync`, `FirstOrDefaultAsync`, etc.)
- AGENTS.md lists `.Result`/`.Wait()` on EF tasks as P1 findings

## Output format

1. Executive summary
2. Top reliability risks
3. Likely high-load failure points
4. Timeout/retry recommendations
5. Test or instrumentation recommendations
6. Phased remediation plan

## Key files to check

- All `Workers/` and `Services/` in gateway projects
- `src/HVO.WebSite.v9/` — API controllers and data services
- `src/HVO.DataModels/` — EF Core query patterns
- All `.razor.cs` files with timers, `OnAfterRenderAsync`, or background tasks
