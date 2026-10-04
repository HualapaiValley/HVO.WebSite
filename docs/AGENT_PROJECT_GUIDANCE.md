# WebSite project guidance

These are WebSite-specific engineering requirements. The shared issue/PR/review lifecycle is defined in [the development process](development/PROCESS.md). Read the applicable project sections before editing or reviewing; read CSS_GOVERNANCE.md before CSS or Blazor markup work.

---

## Repository structure

```
src/
  HVO.WebSite.v9/               Main observatory site (Blazor SSR + ASP.NET Core API)
  HVO.WebSite.Themes/           Shared Razor Class Library — CSS tokens, components, layouts, fonts
  HVO.DataModels/               EF Core models and DbContext
  HVO.Hardware.DavisVantagePro2/  Davis weather station gateway
  HVO.Hardware.JkBms/           JK BMS battery monitor gateway
  HVO.Hardware.Eg4/             EG4 6500EX and MPPT100 gateway
  HVO.Hardware.VictronSmartShunt/ Victron SmartShunt gateway
  HVO.Edge.Exporter.HomeAssistant/ Implemented HA telemetry exporter (disabled in production)
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
tests/
  HVO.WebSite.UnitTests/        MSTest + FluentAssertions + bUnit application tests
  HVO.*.Tests/                 Contract, gateway, outbox, API and provider tests
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
| Testing | MSTest + FluentAssertions; bUnit component tests; MSTest/Playwright browser tests |
| Containers | Docker + `docker --context devpi5` / `docker --context hvo-docker` SSH remote contexts |
| CI | GitHub Actions |
| Hosting | Self-hosted Docker (hvo-docker for website + registry + SQL Server; devpi5 for gateways) |
| Registry | Self-hosted Docker Registry (registry:2 on hvo-docker, exposed as registry.hualapaivalleyobservatory.org) |
| Secrets | Azure Key Vault (`hvo-central-kv`) — primary source of truth |

---

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

The active direct Pi collectors are Davis, JK BMS, EG4, and SmartShunt. Home Assistant owns Kasa and Govee acquisition/presentation. The HA exporter is implemented but intentionally disabled in production with no mappings or source claims. The retired direct SolarAssistant and TP-Link/Kasa applications, containers, and images are not deployment targets.

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

See [test lanes and runner prerequisites](development/testing.md) for exact filters, settings, report paths and supported local commands. The category validator checks each MSTest method using its class/method attributes, including root-level `*IntegrationTests.cs` and `*LiveTests.cs`; comment text or a sibling test's category cannot satisfy the requirement. Test helpers are not required to carry test categories.

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
- Establish the [existing root-bootstrap and whole-helper prerequisites](development/key-vault-materialization.md) before approved `./scripts/sync-secrets-from-keyvault.sh --apply` materialization from `hvo-central-kv`; the private gist is only a devcontainer bootstrap cache
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

---

## Shared Theme & Layout

The repo uses the unified shared theme/layout system in `HVO.WebSite.Themes`.

**Key rules:**
- **All shared components** go in `HVO.WebSite.Themes`, not per-app projects
- **HvoFormat** is the single formatting utility — no raw `ToString("F*")` in razor files
- **ShellLayoutState** is shared from Themes RCL — never copy-pasted
- Theme changes must be demonstrated in ThemeSandbox before production use

---

## Dev Container Tool Policy

The dev container includes the baseline CLI and diagnostic tools used by this repo, including .NET, Docker CLI access, GitHub CLI, Azure CLI, `jq`, `rg`, Node.js/npm, and Python 3.

- **Scripting & automation**: Prefer `bash`/`zsh` shell scripts, `gh` CLI, `dotnet` CLI, or `az` CLI
- **JSON processing**: Use `jq`, .NET `System.Text.Json`, or Python for focused validation scripts
- **Issue/PR management**: Use `gh issue create`, `gh pr create`, etc.
- **Azure resources**: Use `az` CLI for Entra ID, App Service, Container Apps, subscriptions, etc.
- **Search**: Use `rg` (ripgrep) for text search
- **Frontend/browser tooling**: Use the repo-pinned Node/npm tooling only when the task requires it, such as Playwright browser installation or MCP support

### Heredoc / Multi-Line String Warning

**Do NOT use `cat << 'EOF'` or any heredoc syntax in terminal commands.** Heredocs are unreliable in this environment — content frequently gets corrupted, garbled, or truncated. Instead:

1. Write multi-line content to a file using the file-creation tool (e.g., `create_file`).
2. Reference that file in the terminal command (e.g., `gh issue create --body-file /tmp/issue-body.md`).

This applies to **all** cases where you need to pass multi-line text to a CLI command.

---

## Offline-First Resource Policy

Any edge application with a web UI runs on a **local network with no internet access**. Every CSS, JS, font, and image resource must be served from within the app or from the shared `HVO.WebSite.Themes` RCL static assets. The active Davis, JK BMS, EG4, and SmartShunt collectors are headless.

- **Never** add CDN URLs (`cdn.jsdelivr.net`, `fonts.googleapis.com`, `unpkg.com`, etc.) to any gateway `App.razor` file.
- Chart.js is bundled at `src/HVO.WebSite.Themes/wwwroot/js/chart.min.js` — reference it as `_content/HVO.WebSite.Themes/js/chart.min.js`.
- `HVO.WebSite.v9` (Azure-hosted main site) may use external resources.
- When adding new JS/CSS libraries, download them and add to `HVO.WebSite.Themes/wwwroot/`.

## HvoChart — Testing Standards

### Known Failure Modes to Prevent with Tests

**1. Blazor circuit crash from Chart.js interop**
- **Symptom:** "Blazor circuit interrupted" banner — entire page goes non-interactive.
- **Root cause:** C# anonymous types serialize `null` properties to JSON `null`. Chart.js 4.x requires absent properties (undefined), not `null`, for optional config like `title`, `suggestedMin`, `suggestedMax`, `animation`. JS throws → interop exception → circuit dies.
- **Fix in place:** `hvo-chart.js` `stripNulls()` removes all `null`/`undefined` keys from the config before passing to `new Chart()`. Data-array `null` values are preserved (they represent gaps). `HvoChart.razor` `OnAfterRenderAsync` wraps `RenderChartAsync` in try-catch so any JS error never propagates to the Blazor circuit.
- **Test:** Playwright — after page load, assert `#blazor-error-ui` is **not** visible.

**2. Charts blank because scripts are missing (offline gateways)**
- **Symptom:** Canvas elements present in DOM but no lines drawn.
- **Root cause:** chart.js loaded from CDN; gateway machine has no internet.
- **Fix in place:** `chart.min.js` bundled locally in Themes RCL.
- **Test:** Playwright — assert `chart.min.js` in Network responses contains no CDN URL; OR assert canvas `.clientWidth > 0`.

**3. Null data points not rendering as gaps**
- **Symptom:** Line connects across missing data instead of breaking.
- **Root cause:** `HvoChartDataset.Data` uses `IReadOnlyList<double>` (no nulls) or `spanGaps=true`.
- **Fix in place:** `HvoChartDataset.Data` is `IReadOnlyList<double?>`. Default `SpanGaps=false`.
- **Test:** Unit test asserts `HvoChartDataset` accepts and stores `null` entries. Playwright — assert that a chart with known null slots shows a visual break (canvas pixel check or element count check).

### Unit Test Checklist (`HvoChartDatasetTests`)

Ensure these are covered:
- `HvoChartDataset` constructor accepts `double?[]` with `null` entries.
- `null` entries are preserved (not converted to `0` or stripped).
- `Tension` per-dataset parameter is stored correctly.
- `Data_AllowsNullEntries_RepresentingGaps` test passes.

### Playwright Test Checklist

Apply these checks to current website or ThemeSandbox chart surfaces:
- No `#blazor-error-ui` visible after page settles (circuit alive).
- Expected chart canvases are present and have `clientWidth > 0`.
- `datetime-local` inputs have themed styling (not browser default white).
- `hvo-card-shell` cards have non-transparent backgrounds.
- No legacy class names (`proto-*`, `action-btn`, `card-shell`, `archive-table`) on live pages.
- `hvo-components.css` served from `_content/HVO.WebSite.Themes/` (not CDN).
