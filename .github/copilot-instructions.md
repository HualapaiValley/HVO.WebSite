# Copilot Instructions

> **For GitHub Copilot Coding Agent:** Read `AGENTS.md` at the repository root. It is the authoritative instruction file for autonomous coding agents and contains the full review framework, severity levels, workflow rules, CSS governance enforcement, Blazor/gateway-specific checks, and PR format. This file (`copilot-instructions.md`) supplements it for Copilot Chat and inline suggestions.

## Project Overview

**HVO.WebSite** is a .NET 10 observatory dashboard and monitoring web application built with ASP.NET Core and Blazor Server (SSR). It provides real-time observatory status, weather monitoring, imaging session tracking, and equipment control interfaces.

- **Runtime**: .NET 10 / ASP.NET Core + Blazor Server (SSR)
- **ORM**: Entity Framework Core
- **Dependencies**: HVO.Core, HVO.Core.SourceGenerators (NuGet from HVO.SDK)

## Key Components

| Project | Description |
|---------|-------------|
| **HVO.WebSite.v9** | Main observatory dashboard — Blazor SSR pages + ASP.NET Core API endpoints |
| **HVO.DataModels** | Entity Framework Core models and DbContext for observatory data |
| **HVO.WebSite.Themes** | Shared CSS themes, fonts, and static assets (Razor Class Library) |

## Coding Standards

- **Blazor Components**: Follow the file structure convention — `.razor` (markup), `.razor.cs` (code-behind), `.razor.css` (scoped styles)
- **Theme**: Use the HVO Dark theme variables and classes from HVO.WebSite.Themes
- **Logging**: Use structured logging with `ILogger<T>`
- **EF Core**: Use async data access patterns (`ToListAsync`, `FirstOrDefaultAsync`, etc.)

## CSS Authoring Rules

> **Full governance policy**: `docs/CSS_GOVERNANCE.md` — read it before touching any CSS or Blazor markup.

The theme is the single source of truth. `hvo-shared-shell.css` defines all tokens; `hvo-components.css` defines all reusable component classes. Violations are **blocker** findings in code review.

### Hard rules — never violate

1. **No hardcoded colors.** Every color in a `.razor.css` or `app.css` must be `var(--shell-*)`, `var(--hvo-series-*)`, `var(--hvo-accent-*)`, or `color-mix()` built from those vars. No `#hex`, `rgb()`, or `rgba()` literals.
2. **No pass-through alias variables.** `--my-text: var(--shell-page-text)` is forbidden — use the theme var directly. Computed derived values (e.g. `color-mix(...)`) are allowed.
3. **No local copies of theme classes.** Never redefine `.hvo-card`, `.hvo-chip`, `.shell-brand-mark`, `.shell-page-stack`, or any other class already in the theme files. Extend via modifier classes only.
4. **No `:root` overrides of `--shell-*` or `--hvo-*` tokens.** These are global; overriding them changes the look across the entire project. The only exception is `--shell-page-hero-image`.
5. **No `@font-face` outside `hvo-shared-shell.css`.** Fonts are declared once in the Themes RCL.
6. **No hardcoded colors in inline `style=""` attributes.** Use `var(--hvo-*)` or a CSS class. Exception: runtime-computed positional values (e.g. `left: @pct%`) and CSS custom property injection (e.g. `--hvo-ring-level: @soc%`).
7. **Chart.js C# hex literals must have `// --hvo-*` comments.** Canvas 2D cannot resolve CSS variables, so hex literals are required in `HvoChartDataset`. Each must reference the canonical palette variable: `"#69d3ff" // --hvo-series-1`.

### Global CSS change process (changes to hvo-shared-shell.css or hvo-components.css)

These files affect every consuming web project simultaneously.

- **Adding a class/token**: demo it in ThemeSandbox `/css-reference` or `/palette` first; get sign-off before merge.
- **Modifying**: grep all projects first (`rg "classname" src/`); verify visual impact; run `dotnet build` across the entire solution.
- **Removing/renaming**: add a deprecation comment in one commit; migrate all usages in the next; delete only when zero usages remain.
- **Palette color change**: update `hvo-shared-shell.css` AND all C# hex literals in the same commit.

### Key spacing tokens

| Use | Token |
|-----|-------|
| Gap between cards | `var(--shell-card-gap)` (`12px`) |
| Page content inset | `var(--shell-page-padding)` |
| Gap between page sections | `var(--shell-page-section-gap)` |
| Body typeface | `var(--shell-font-family)` |
| Monospace typeface | `var(--shell-font-mono)` |

## Issue & PR Workflow

Follow this process for every issue. **Never auto-start the next issue unless explicitly instructed.**

### 1. Start Work

- Create a new branch from `main` for the issue (e.g., `feature/<issue#>-<short-description>` or `fix/<issue#>-<short-description>`).

### 2. Implement

- Work the issue on the branch.
- Write appropriate tests for **all new and modified code**.
- Ensure the project builds with **zero warnings and zero errors**.
- Ensure all tests pass with **zero failures and zero warnings**.

### 3. Submit

- Commit, push, and create a PR.
- **Stop and wait** — do not proceed until instructed.

### 4. Code Review

- After the PR is code-reviewed, address **all** review comments.
- Rebuild the project — **zero warnings and zero errors**.
- Commit and push the fixes.

> **Hard rule:** Never create a PR or merge that has warnings or errors unless specifically instructed otherwise. Never create a PR or merge with failing tests or test warnings.

### 5. Merge & Clean Up

- Merge the PR.
- Switch back to `main` and pull latest.
- Delete the merged branch (local and remote).

### 6. Wait

- **Do not** start the next issue automatically. Wait for explicit instructions.

## Shared Theme & Layout

The repo uses the unified shared theme/layout system in `HVO.WebSite.Themes`.

**Key rules:**
- **All shared components** go in `HVO.WebSite.Themes`, not per-app projects
- **HvoFormat** is the single formatting utility — no raw `ToString("F*")` in razor files
- **ShellLayoutState** is shared from Themes RCL — never copy-pasted
- Theme changes must be demonstrated in ThemeSandbox before production use

## Conventions

- **Branch naming**: `feature/<issue#>-<short-desc>`, `fix/<issue#>-<short-desc>`
- **Commit messages**: Conventional commits (`feat:`, `fix:`, `chore:`, `refactor:`, `test:`, `docs:`)
- **Merge strategy**: Squash merge into `main`

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
