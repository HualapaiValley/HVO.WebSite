# Task: Repo-wide review 09 — Front-end Blazor, JavaScript, and CSS

**GitHub issue:** #197
**Type:** Review only — do not make code changes, do not open a PR
**Output:** Post report as a comment on issue #197 if possible; otherwise write to `.codex/reports/197-frontend.md`

## Instructions

Perform a focused repository-wide review of front-end code. This repo uses Blazor Server SSR as the primary UI technology — there is no Angular. Review all Blazor, JavaScript interop, CSS, and any JS/TS files present.

## Focus areas

- Blazor component structure — separation of markup, logic, and styles (`.razor` / `.razor.cs` / `.razor.css` triad)
- JS interop safety — `OnAfterRenderAsync` must wrap JS calls in try-catch to prevent Blazor circuit crashes
- `HvoChart` usage — no bespoke `<canvas>` wrappers; all charts via `HvoChart` component
- `HvoFormat` usage — no raw `ToString("F1")`, `ToString("F2")`, or `CultureInfo.InvariantCulture` in `.razor` files
- Shared layouts — `HvoGatewayLayout`, `HvoPublicLayout`, `HvoAdminLayout` from `HVO.WebSite.Themes` RCL; no local copies
- CSS theme compliance (see `docs/CSS_GOVERNANCE.md` for full rules):
  - No hardcoded `#hex`, `rgb()`, `rgba()` in `.razor.css` or `app.css`
  - No `@font-face` outside `hvo-shared-shell.css`
  - No CDN URLs in gateway `App.razor` files
  - No `:root` overrides of `--shell-*` or `--hvo-*` tokens
  - No local copies of theme classes
- `IAsyncDisposable` on components with timers or JS object references
- `StateHasChanged()` not called on every timer tick
- Business logic in Blazor components that belongs in services
- Form validation completeness
- Accessibility on interactive elements

## Key context for this repo

- All gateway apps run offline — no CDN URLs allowed in App.razor (P0 violation)
- `hvo-shared-shell.css` and `hvo-components.css` are the single source of truth for all CSS tokens and component classes
- CSS violation severity: P0 = CDN URL or @font-face in wrong place; P1 = hardcoded color or local theme class copy; P2 = missing gap/font-family variable
- `hvo-chart.js` `stripNulls()` must be called before `new Chart()` — Chart.js null config properties crash the Blazor circuit

## Output format

1. Executive summary
2. Top front-end risks
3. Component/service structure observations
4. JS interop safety observations
5. CSS/theme compliance observations
6. UX/accessibility risks
7. Suggested remediation plan

## Key files to check

- All `.razor`, `.razor.cs`, `.razor.css` files across all projects
- `src/HVO.WebSite.Themes/wwwroot/css/themes/hvo-shared-shell.css` and `hvo-components.css` (source of truth)
- `src/HVO.WebSite.Themes/wwwroot/js/hvo-chart.js` — chart interop safety
- `src/HVO.ThemeSandbox/` — reference implementation of correct patterns
- `docs/CSS_GOVERNANCE.md` — full authoring policy
- All gateway `App.razor` files — stylesheet load order and CDN check
