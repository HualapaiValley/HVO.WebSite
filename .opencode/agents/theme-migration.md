---
description: Executes the Unified Theme & Layout migration across all HVO.WebSite apps. Validates via build + Playwright tests at every phase. Updates GitHub issue checklists as work progresses.
mode: autopilot
---

# Unified Theme & Layout Migration Agent

You are executing the migration plan from `docs/UNIFIED_THEME_PLAN.md`. Follow the phases strictly. Never skip a phase. Never start the next phase before the current one is verified.

## Configuration Reference

| Key | Value |
|-----|-------|
| Plan doc | `docs/UNIFIED_THEME_PLAN.md` |
| Epic issue | #153 |
| RCL project | `src/HVO.WebSite.Themes/` |
| Shell CSS | `src/HVO.WebSite.Themes/wwwroot/css/themes/hvo-shared-shell.css` |
| Old dark CSS | `src/HVO.WebSite.Themes/wwwroot/css/themes/hvo-dark.css` |
| Unified palette | `HvoTheme.Create()` in Themes RCL |
| Format utility | `HvoFormat` in `HVO.WebSite.Themes.Components.Format` |
| Shared layouts | `HvoGatewayLayout`, `HvoPublicLayout`, `HvoAdminLayout` in `HVO.WebSite.Themes.Components.Layout` |
| Shared chart | `HvoChart` in `HVO.WebSite.Themes.Components.Charts` |

## Phase Execution Protocol

For every phase/sub-task:

### 1. Start
- Read `docs/UNIFIED_THEME_PLAN.md` for the phase details
- Read the corresponding GitHub issue (e.g., #154 for Phase 0)
- Create a branch: `feature/<issue#>-<short-desc>`
- Update the GitHub issue checklist: mark the task as `in-progress` via issue comment or edit

### 2. Implement
- Follow the task list in the issue body. Each checkbox is one unit of work.
- After each checkbox: build with `dotnet build` — **zero warnings, zero errors**.
- After all checkboxes are done: run Playwright tests if they exist for this phase.
- Commit after each logical checkpoint with conventional commit messages (`feat:`, `refactor:`, `test:`, `chore:`).

### 3. Verify (Hard Gate)
Before marking the phase complete, ALL of these must pass:
- [ ] `dotnet build` — zero warnings, zero errors
- [ ] All Playwright tests pass — zero failures, zero warnings
- [ ] Manual visual check: dark theme looks correct (no broken colors, readable text)
- [ ] Manual visual check: light theme looks correct
- [ ] Theme toggle is smooth (no flash, no layout shift)
- [ ] No Bootstrap CSS remains (if applicable to the phase)

### 4. Submit
- Push branch to origin
- Create a PR with `Closes #<issue-number>` in the description
- **Stop and wait** for review instructions. Do not merge automatically.

### 5. After Merge
- Switch back to `main` and pull latest
- Delete the merged branch
- Update the GitHub issue: check all completed items, close the issue
- **Do not** start the next phase until explicitly instructed

## Phase Order (Strict)

```
Phase 0 (#154) → Phase 0.5 (#155) → Phase 1.1 (#156) → Phase 1.2 (#157)
→ Phase 1.3 (#158) → Phase 1.4 (#159) → Phase 1.5 (#160)
→ Phase 2 (#161) → Phase 3 (#162) → Phase 4 (#163)
```

## Important Design Rules

1. **All components go in HVO.WebSite.Themes**, not in per-app projects. The RCL is the single source of truth for shared UI.
2. **HvoFormat is the only way to format data in razor files.** No `ToString("F*")`, no `CultureInfo.InvariantCulture` in views. Exceptions only for non-standard formats (angles, Kelvin, enum names).
3. **HvoFormat accepts UnitSystem as a parameter.** The cascade flows from layout root → `IUserPreferencesService` (localStorage) → fallback to `appsettings.json:HvoFormatting:UnitSystem`.
4. **Unified MudTheme** (`HvoTheme.Create()`) is the default. Override individual palette values only when there's a clear functional reason, never for cosmetic preference.
5. **ShellLayoutState** is shared from Themes RCL. Never copy-paste it into a per-app project.
6. **Davis app.css** (~800 lines of forked shell CSS) must be deleted, not maintained.
7. **Chart theming** uses JS interop reading `--shell-chart-*` CSS vars. Chart.js defaults update on theme toggle.
8. **CSS governance**: Read `docs/CSS_GOVERNANCE.md` before writing any CSS. The full hard rules, permitted patterns, and global change process are defined there. Key points:
   - No hardcoded `#hex` or `rgba()` in `.razor.css` or `app.css` — use `var(--shell-*)`, `var(--hvo-series-*)`, `var(--hvo-accent-*)`, or `color-mix()`.
   - No pass-through alias variables.
   - No local copies of `hvo-components.css` or `hvo-shared-shell.css` classes.
   - No `--shell-*` overrides at `:root`.
   - Global CSS changes (`hvo-shared-shell.css`, `hvo-components.css`) require ThemeSandbox demo + cross-project grep + full solution build before merge.

## Playwright Test Rules

- Tests go in each app's `Playwright/` directory (or nearest test project)
- Use page object model: `Playwright/Pages/` for page interactions, `Playwright/Tests/` for test methods
- Every phase that adds or modifies UI must add or update Playwright tests
- Tests must pass in CI (headless mode)
- Test both dark and light themes where visuals matter

## Common Pitfalls

1. **Don't forget `_Imports.razor`** — every app needs `@using HVO.WebSite.Themes.Components.Layout` and `@using HVO.WebSite.Themes.Components.Format`
2. **Don't delete hvo-dark.css early** — it's still referenced by the main site until Phase 2 removes Bootstrap. Phase 0.10 only cleans Bootstrap vars, doesn't delete the file.
3. **Don't merge Davis Phase 1.5 before checking SVG charts** — the inline SVG charts in `Status.razor` use `CultureInfo.InvariantCulture` for SVG coordinate strings. Replace format strings in labels but keep `CultureInfo.InvariantCulture` for SVG path/coordinate rendering.
4. **Don't skip the ThemeSandbox** — Phase 0.5 is the validation gate. If sandbox tests fail, fix in Phase 0 before touching production gateways.
