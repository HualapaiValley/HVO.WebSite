# AGENTS.md

## Purpose

This repository uses AI coding agents — including GitHub Copilot Coding Agent and OpenCode — for code review, investigation, refactoring, documentation assistance, and implementation support.

The goal is not to produce superficial comments. The goal is to identify meaningful engineering risks, verify that changes meet requirements, improve maintainability, and protect production stability across all six applications in this solution.

When working in this repository, act as a senior software engineer and architect with strong experience in:

- C# and .NET 10
- ASP.NET Core and Blazor Server (SSR)
- MudBlazor component library
- Entity Framework Core
- REST APIs and service-oriented systems
- CSS custom properties, design systems, and component libraries
- Chart.js and JS interop from Blazor
- Docker, SSH remote contexts, and Raspberry Pi edge deployments
- CI/CD, configuration management, structured logging, and production support

Prefer clear, actionable, evidence-based findings over style preferences.

---

## Repository structure

```
src/
  HVO.WebSite.v9/               Main observatory site (Blazor SSR + ASP.NET Core API)
  HVO.WebSite.Themes/           Shared Razor Class Library — CSS tokens, components, layouts, fonts
  HVO.DataModels/               EF Core models and DbContext
  HVO.Hardware.DavisVantagePro2/  Davis weather station gateway
  HVO.Hardware.JkBms/           JK BMS battery monitor gateway
  HVO.Hardware.VictronSmartShunt/ Victron SmartShunt gateway
  HVO.Gateway.SolarAssistant/   SolarAssistant inverter gateway
  HVO.Gateway.TplinkKasa/       TP-Link Kasa smart plug gateway
  HVO.ThemeSandbox/             CSS/component reference app (not deployed to production)
deploy/
  hvo-docker/                   Docker Compose + .env for the website host (hvo-docker)
  pi-gateways/                  Per-gateway Docker Compose + .env for devpi5
scripts/
  deploy-hvo-website.sh         Deploys the website to hvo-docker via Docker SSH context
  deploy-pi-gateway.sh          Deploys one or all gateways to devpi5 via Docker SSH context
  publish-image.sh              Builds and pushes images to self-hosted container registry
  sync-env-gist.sh              Syncs root .env to private GitHub gist (devcontainer bootstrap)
docs/
  CSS_GOVERNANCE.md             Full CSS authoring policy (read before touching any CSS)
  UNIFIED_THEME_PLAN.md         Migration epic plan
tests/
  HVO.WebSite.UnitTests/        Unit tests (188 tests)
  HVO.WebSite.PlaywrightTests/  Playwright end-to-end tests
```

---

## Tech stack

| Layer | Technology |
|-------|-----------|
| Runtime | .NET 10, ASP.NET Core, Blazor Server SSR |
| UI components | MudBlazor 8.x |
| Shared theme | HVO.WebSite.Themes RCL — `hvo-shared-shell.css`, `hvo-components.css` |
| Charts | Chart.js 4.4.0, bundled locally at `_content/HVO.WebSite.Themes/js/chart.min.js` |
| ORM | Entity Framework Core (async-only: `ToListAsync`, `FirstOrDefaultAsync`, etc.) |
| Testing | xUnit + FluentAssertions (unit); Playwright (E2E) |
| Containers | Docker + `docker --context devpi5` / `docker --context hvo-docker` SSH remote contexts |
| CI | GitHub Actions |
| Hosting | Self-hosted Docker (hvo-docker for website + registry + SQL Server; devpi5 for gateways) |
| Registry | Self-hosted Docker Registry (registry:2 on hvo-docker, exposed as registry.hualapaivalleyobservatory.org) |
| Secrets | Azure Key Vault (obs-infra-kv) — only Azure resource besides the Tailscale proxy VM |

---

## Operating rules for agents

- **Do not make code changes unless explicitly asked.** For review-only tasks, produce a structured report only.
- **If asked to implement a GitHub issue,** create a branch first (`feature/<issue#>-<short-desc>` or `fix/<issue#>-<short-desc>`), implement on the branch, then open a PR and stop. Do not merge or start the next issue without explicit instruction.
- **Never commit directly to `main`** when working on an issue. Always use a branch.
- **Build must pass at zero warnings and zero errors** before any PR. Run `dotnet build` from the solution root.
- **All tests must pass** before any PR. Run `dotnet test --filter "TestCategory!=Live"` for unit tests. Live Playwright tests require gateway URLs set in environment variables.
- If asked to propose code changes, keep changes small, focused, and reviewable.
- Do not reformat large files unless formatting is the requested task.
- Do not introduce new frameworks, packages, patterns, or architectural direction without explaining why the existing approach is insufficient.
- Do not remove existing behavior unless the task explicitly asks for it.
- Do not change public API contracts, database schemas, configuration keys, authentication behavior, or deployment manifests without calling out the compatibility risk.
- When uncertain, state the assumption and explain what evidence would confirm it.
- Read `docs/CSS_GOVERNANCE.md` before writing or reviewing any CSS or Blazor markup.

---

## Review output contract

When performing a review-only task (any issue labelled `review`):

1. **Always post results as a GitHub issue comment.** Use `gh issue comment <number> --body-file <file>` or `gh issue comment <number> --body "..."`. The comment is the permanent record that downstream agents and humans will read.
2. **Never just print the report to stdout and stop.** If the only output is terminal text, it is lost. It must be on the issue.
3. **If `gh` is unavailable or the post fails**, write the report to a markdown file at `docs/reviews/<issue#>-<slug>.md`, commit it to a branch named `review/<issue#>-<slug>`, and open a PR so the report is preserved in the repo.
4. **Structure the comment** exactly as the issue body requests. Every finding must have: severity (P0/P1/P2/P3), file/path, evidence, recommended fix, and estimated effort.
5. **Do not open a PR for review-only tasks** unless the fallback file approach is needed (point 3).

```bash
# Preferred: post directly to the issue
gh issue comment 190 --body-file /tmp/review-report.md

# Fallback if gh unavailable: write to repo and open PR
git checkout -b review/190-security
mkdir -p docs/reviews
# write report to docs/reviews/190-security.md
git add docs/reviews/190-security.md
git commit -m "docs: add security review report for issue #190"
gh pr create --title "Review report: issue #190 security" --body "Closes #190 — adds review report since gh issue comment was unavailable."
```

---

## Implementation handoff workflow

After all review issues are complete (comments posted on #187–#198), the consolidated roadmap (#198) drives the next phase.

### Do not create one issue per finding

A review may surface 50–100 findings. Creating a separate GitHub issue and PR for each one is unworkable — it produces an unmanageable review queue and buries the important fixes in noise.

**Instead: group related findings into batches.** Each batch becomes one implementation issue and one PR.

### Batching rules

Group findings by **severity first, then by domain/area**:

| Batch | What it contains | Typical PR size |
|-------|-----------------|-----------------|
| **P0 batch** (one per area) | All P0 findings in a single project or domain | Small — P0s should be few and targeted |
| **P1 batch per domain** | All P1 findings in the same area (e.g., all CSS P1s, all async P1s, all security P1s) | Small-Medium |
| **P2 batch per project** | All P2 findings in one project | Medium |
| **P3 omnibus** | All P3 cleanups together | Medium — one PR, low-risk |

**Examples:**
- 6 P0 findings across 3 projects → 3 small P0 PRs (one per project)
- 18 P1 async findings → 2 PRs: one for gateway workers, one for the main site
- 30 P2 CSS findings → 1 PR (CSS changes are low-risk and easy to review together)
- 20 P3 naming cleanups → 1 omnibus PR

The goal is **5–15 total PRs**, not 50–100.

### Creating batch implementation issues

After the consolidated roadmap (#198) is posted, create one GitHub issue per batch:

```bash
gh issue create \
  --title "fix(P1/async): eliminate sync-over-async in gateway workers" \
  --body "## Findings to address

All P1 async findings from review #192 and #191 that affect gateway background workers.

Source reviews: #191 (logging), #192 (async/perf)

## Findings included
- <paste each finding block: file, evidence, recommended fix>

## Acceptance criteria
- [ ] No .Result / .Wait() in any worker class
- [ ] All EF Core calls use async methods
- [ ] dotnet build: 0 warnings, 0 errors
- [ ] Unit tests pass" \
  --label "P1,finding"
```

### How implementation agents read a batch issue

When assigned a batch implementation issue, an agent should:

1. `gh issue view <number>` — read the full finding list, evidence, and recommended fixes
2. `gh issue view <source-review-issue> --comments` — read the full review report for surrounding context
3. Implement all findings in the batch on one branch
4. Open a single PR — `Closes #<batch-issue>` in the body
5. Keep the PR focused: all changes in the batch should be in the same domain

### Severity triage

| Label | Action |
|-------|--------|
| `P0` + `finding` | Fix immediately — small focused batch, implement before any other work |
| `P1` + `finding` | Fix before next release — batch by domain, schedule promptly |
| `P2` + `finding` | Plan to fix — batch by project, add to backlog |
| `P3` + `finding` | Optional cleanup — one omnibus batch, low priority |

### Labels on batch issues

Every batch implementation issue must have:
- One of: `P0`, `P1`, `P2`, `P3` (the highest severity in the batch)
- The `finding` label
- Optionally: a domain label (e.g. `security`, `css`, `async`, `tests`)

---

## Issue and PR workflow

When asked to implement a GitHub issue:

1. **Read the issue.** Use `gh issue view <number>` to get the full description, acceptance criteria, and labels.
2. **Create a branch.** `git checkout -b feature/<issue#>-<short-desc>` from `main`.
3. **Implement.** Follow the coding standards below.
4. **Build and test.** `dotnet build` (0 warnings, 0 errors) and `dotnet test --filter "TestCategory!=Live"` (0 failures).
5. **Commit.** Use conventional commits: `feat:`, `fix:`, `refactor:`, `test:`, `docs:`, `chore:`.
6. **Open a PR.** Include `Closes #<issue>` in the PR body. Use the PR summary format below.
7. **Stop.** Do not merge, deploy, or start the next issue without explicit instruction.

**PR summary format:**

```md
## Summary
- 
- 

## Tests
- [ ] `dotnet build` — 0 warnings, 0 errors
- [ ] Unit tests pass
- [ ] Playwright tests pass (if UI changed)

## Risk / rollout notes
- 
```

---

## Severity levels

### P0 — Critical / Blocker

Must fix before merge. Likely production-breaking, security flaw, data corruption, build blocker, or outage risk.

Examples: authentication bypass; secret exposure; SQL injection; data corruption; broken build; Blazor circuit crash from unhandled JS interop exception; CDN resource in offline gateway App.razor; wrong Docker health check.

### P1 — High

Serious issue that should be fixed before merge or immediately after.

Examples: missing input validation; swallowed exceptions; unsafe PII logging; sync-over-async in request paths; EF Core N+1 queries; missing tests for new business logic; hardcoded color values in CSS that break theme switching; local copy of a theme class with divergent styles; gateway deploying with wrong device IP.

### P2 — Medium

Meaningful maintainability, reliability, or consistency issue. Should be planned but may not block release.

Examples: duplicate CSS class definitions; pass-through alias CSS variables; `gap: 12px` instead of `var(--shell-card-gap)`; font-family string literal instead of `var(--shell-font-family)`; missing `// --hvo-*` comment on chart hex literal; proto-* or legacy shim class usage; inconsistent error handling.

### P3 — Low

Cleanup, naming, minor documentation, or style concern.

Examples: unused local CSS variable; missing code comment on complex logic; minor naming clarity.

> **Severity mapping to OpenCode skill:** P0 = BLOCKER, P1 = CRITICAL, P2 = MAJOR, P3 = MINOR/NIT. Use whichever vocabulary fits the review surface.

---

## CSS and theme review — P0/P1 hard rules

> Full policy: `docs/CSS_GOVERNANCE.md`. Violations here are **P0 or P1** findings — not style nits.

Check every `.razor.css`, `app.css`, and inline `style=""` attribute:

**P0 — Must fix before merge:**
- `@font-face` block outside `src/HVO.WebSite.Themes/wwwroot/css/themes/hvo-shared-shell.css`
- CDN URL (`cdn.jsdelivr.net`, `fonts.googleapis.com`, `unpkg.com`, etc.) in any gateway `App.razor` — all gateway apps run offline
- Hardcoded color value (`#hex`, `rgb()`, `rgba()`) in a `.razor.css` file — must use `var(--shell-*)`, `var(--hvo-series-*)`, `var(--hvo-accent-*)`, or `color-mix()` from those vars
- `:root` redefinition of `--shell-*` or `--hvo-*` tokens in a per-project file (changes the token globally)

**P1 — Should fix before merge:**
- Local copy of a class already defined in `hvo-components.css` or `hvo-shared-shell.css` (duplicates diverge silently)
- Pass-through alias variable: `--my-text: var(--shell-page-text)` — use the theme var directly
- `style=""` attribute with hardcoded color in a `.razor` file (use `var(--hvo-*)` or a CSS class)
- Redefinition of `.shell-brand-mark`, `.shell-page-stack`, or any other base shell class in per-project CSS

**P2 — Should plan:**
- `gap: 12px` instead of `var(--shell-card-gap)`
- `font-family: "Open Sans..."` instead of `var(--shell-font-family)`
- Chart.js C# hex literal without `// --hvo-*` comment identifying the canonical palette variable
- New class in `hvo-components.css` or `hvo-shared-shell.css` without a ThemeSandbox demo

---

## Blazor and MudBlazor guidelines

Check for:

- `.razor` / `.razor.cs` / `.razor.css` file triad — all three should exist for pages and components with significant logic or styles
- `@rendermode InteractiveServer` on components that use JS interop or need real-time updates; SSR-only pages should not declare a render mode
- Shared layouts (`HvoGatewayLayout`, `HvoPublicLayout`, `HvoAdminLayout`) used from `HVO.WebSite.Themes` — never local copies
- `HvoFormat` used for all formatted output — no raw `ToString("F1")`, `ToString("F2")`, or `CultureInfo.InvariantCulture` in `.razor` files (exception: SVG coordinate rendering)
- `HvoChart` used for all charting — no bespoke `<canvas>` wrappers or direct Chart.js calls outside the shared component
- `OnAfterRenderAsync` JS interop wrapped in try-catch so a JS exception does not crash the Blazor circuit
- `@inject` services have appropriate lifetimes — scoped services not captured into singleton-lived objects
- `StateHasChanged()` called appropriately — not on every tick of a background timer
- `IAsyncDisposable` implemented and `DisposeAsync` awaited for components that start timers or hold JS object references
- Blazor error boundary (`<ErrorBoundary>`) used for critical UI sections that should not take down the whole page

---

## EF Core and data access guidelines

Check for:

- All EF Core queries use async methods: `ToListAsync`, `FirstOrDefaultAsync`, `SaveChangesAsync`, etc.
- No `.Result` or `.Wait()` on EF tasks
- Queries projected to DTOs or view models where possible — no returning full entity graphs to UI layers
- No `SaveChangesAsync` inside a loop without explicit transaction handling
- Pagination applied to queries that could return large result sets
- No client-side evaluation (verify no `AsEnumerable()` before a filter)
- Migrations: every schema change has a corresponding EF Core migration; no manual SQL without a migration
- `HVO.DataModels.DbContext` used via DI with scoped lifetime

---

## Gateway-specific guidelines

Each Pi gateway (Davis, JkBms, SmartShunt, SolarAssistant, TplinkKasa) has specific deployment and connectivity requirements.

Check for:

- `App.razor` loads all resources locally — no CDN URLs. Chart.js must be `_content/HVO.WebSite.Themes/js/chart.min.js`
- Required stylesheet load order in `App.razor`: `hvo-dark.css` → `MudBlazor.min.css` → `hvo-shared-shell.css` → `hvo-components.css` → project scoped CSS
- Health check endpoint (`/health`) wired and returning meaningful status — must reflect actual device connectivity, not just process liveness
- Outbox pattern: data forwarded to Azure via `Outbox__ApiEndpoint` and `Outbox__ApiKey` from environment; never hardcoded
- Device IP/hostname in `.env` only — never hardcoded in `appsettings.json` or source code
- MQTT and REST polling wrapped with timeouts and error handling — a single device failure must not crash the worker
- Background workers use `CancellationToken` throughout and honor it during shutdown
- `docker-compose.yml` has `restart: unless-stopped` for all gateway services

**Deployment variables override precedence** (critical for deploy scripts):
Docker Compose resolves environment variables in this order: shell environment > `--env-file`. A stale shell variable WILL override the `.env` file. Always verify with `docker compose config | grep <var>` before deploying.

---

## Security guidelines

Check for:

- API keys, passwords, and connection strings in `.env` files only — not in source code, `appsettings.json`, or docker-compose
- `.env` files are in `.gitignore` — verify with `git check-ignore -v deploy/pi-gateways/*/.env`
- Authentication and authorization on all API endpoints in `HVO.WebSite.v9` — check `[Authorize]` attributes and policy requirements
- No sensitive data (API keys, user credentials, PII) in log output
- No exception detail leaked in API error responses to clients
- `X-Api-Key` header validated on gateway ingest endpoints; reject missing or invalid keys with 401
- HTTPS enforced for Azure-hosted main site; gateways on local network may use HTTP

---

## General review guidelines

When reviewing code, check for:

- Correctness against the issue, PR description, or business intent
- Completeness — no TODOs or stubs left unfinished
- Regressions in existing behavior
- Error handling and exception propagation
- Logging quality — enough context to diagnose production failures without logging secrets
- Security and privacy risks
- Test coverage for changed behavior
- Configuration safety
- Async/threading correctness
- Performance — no blocking calls in request paths, no unbounded queries
- Build and CI reliability

---

## Required review output format

For review tasks, produce this structure:

1. **Executive summary**
2. **Overall risk level:** Low / Medium / High / Critical
3. **Top findings** (P0 and P1 only, ordered by severity)
4. **All findings grouped by severity:** P0, P1, P2, P3

For each finding:

- Severity: P0 / P1 / P2 / P3
- Area / file / line
- Evidence from the codebase
- Why it matters
- Recommended fix
- Estimated effort: Small / Medium / Large

5. **Test coverage observations**
6. **Deployment and configuration observations**
7. **Suggested remediation sequence**
8. **Open questions or assumptions**

If there are no findings in a severity band, explicitly say so.

---

## PR review guidance

For PR reviews:

- Focus first on changed files and the behavior introduced by the PR
- Compare implementation against the linked GitHub issue and its acceptance criteria
- Check whether tests were added or updated for changed behavior
- Check whether CSS changes comply with `docs/CSS_GOVERNANCE.md`
- Check whether new Blazor components follow the `.razor` / `.razor.cs` / `.razor.css` triad
- Check whether the build passes at zero warnings and zero errors
- Avoid low-value comments
- Identify risky changes that need manual verification on devpi5

For P0 and P1 findings, post GitHub PR review comments if possible via `gh pr review`.

---

## Repo-wide review guidance

For repo-wide reviews:

- Review by domain: architecture, CSS/theme compliance, security, data access, error handling, tests, gateway deployment, CI/CD
- Prefer findings tied to specific files, folders, or patterns
- Identify repeated patterns (e.g., hardcoded hex colors, duplicate CSS classes, missing cancellation tokens) with representative examples — do not list every instance
- Separate urgent defects from modernization opportunities
- Produce a remediation roadmap with clear priorities

---

## C# and .NET guidelines

Check for:

- Clear separation: endpoints/components → services → domain logic → data access → infrastructure
- Business logic not embedded in Blazor components, controllers, or config helpers
- Appropriate DI lifetime management — no scoped services in singletons, no captive dependencies
- Correct disposal of `IDisposable` and `IAsyncDisposable`
- No sync-over-async: `.Result`, `.Wait()`, or `GetAwaiter().GetResult()` in request paths
- Correct `async`/`await` usage; `async void` only on Blazor event handlers
- `CancellationToken` passed through to EF Core, HTTP calls, and background workers
- Guard clauses and validation at trust boundaries (API controllers, ingest endpoints)
- No overly broad `catch (Exception)` blocks that swallow failures silently
- Strong typing over magic strings — use `record` types for value objects, `enum` for state machines
- Consistent nullable reference types — no unchecked `!` suppressions without clear justification
- Zero compiler warnings — all warnings in this repo are treated as P2 or higher

---

## Testing guidelines

Treat test coverage as a core quality requirement:

- Tests for new or modified business logic — no coverage, no merge
- Tests for error paths and edge cases on gateway ingest and data processing
- Unit tests for `HvoChartDataset`, `StatusChartBuckets`, `HvoFormat`, view models, and data-transformation logic
- Playwright tests for UI behavior: Blazor circuit alive (`#blazor-error-ui` not visible), charts rendered, no legacy CSS class names, themed controls
- Live Playwright tests that require a running gateway must be tagged `TestCategory=Live` so the standard `--filter "TestCategory!=Live"` CI run skips them
- Tests must be deterministic — no `Thread.Sleep`, no dependency on wall-clock time without abstraction
- `HvoChartDataset` unit test checklist: null entries accepted and preserved; `Tension` parameter stored; `Data_AllowsNullEntries_RepresentingGaps` test passes

---

## Build and CI guidelines

Check for:

- `dotnet build` (all projects) passes at **zero warnings, zero errors** — this is a hard gate
- `dotnet test --filter "TestCategory!=Live"` passes at **zero failures**
- No `#pragma warning disable` that hides a legitimate issue
- No new NuGet packages added without a reason documented in the PR
- No CDN URLs introduced in gateway `App.razor` files

---

## Deployment guidelines

Check for:

- `deploy/pi-gateways/<gateway>/docker-compose.yml` updated if the container's environment variables, ports, or volume mounts changed
- `deploy/pi-gateways/<gateway>/.env.example` updated if new required variables were added
- Root `.env` and gist synced (`./scripts/sync-env-gist.sh`) when any gateway IP or credential changed
- Deployment tested: `./scripts/deploy-pi-gateway.sh --context devpi5 <gateway>` followed by `./scripts/check-deployments.sh`

---

## Documentation expectations

Flag missing documentation when it affects:

- Build or setup (new environment variables, new required services)
- Deployment (new gateway or changed deploy sequence)
- CSS token additions (ThemeSandbox demo required by `docs/CSS_GOVERNANCE.md`)
- New shared components (usage examples in ThemeSandbox `/css-reference` or `/instruments`)
- External integrations or MQTT/REST contract changes

Do not require documentation for obvious code.

---

## Preferred remediation sequence

When recommending fixes, suggest this order:

1. Stabilize P0 items — circuit crashes, security issues, broken builds, offline gateway CDN references
2. Stabilize P1 items — CSS theme violations that break dark/light switching, missing auth, swallowed exceptions
3. Add or restore tests around risky behavior
4. Improve logging and telemetry for production diagnosability
5. Reduce CSS/code duplication and resolve P2 compliance gaps
6. Modernize dependencies and deprecated patterns incrementally

---

## Commit and PR guidance for agent-created changes

If asked to create changes:

- Keep PRs focused on a single issue
- Include a clear summary with `Closes #<issue>`
- Document which tests were run and their results
- Note risk areas and any manual verification steps needed (e.g., "verify Davis UI at http://devPi5:5100 after deploy")
- Do not mix unrelated refactors with bug fixes in the same PR
- Do not include formatting-only changes unless formatting was the task

---

## Final instruction

When in doubt, be conservative, specific, and evidence-based. Check `docs/CSS_GOVERNANCE.md` before any CSS review. Check `copilot-instructions.md` for project-level coding standards. The best review is one that helps the team decide what to fix first and gives them enough evidence to act without re-reading the entire codebase.
