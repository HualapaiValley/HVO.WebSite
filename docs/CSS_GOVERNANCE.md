# CSS Governance — HVO.WebSite

**Status**: Active policy (established after full CSS audit, June 2026)

This document defines the rules every developer and AI agent must follow when writing or modifying CSS or Blazor markup across any HVO.WebSite project. Violations found during code review must be fixed before merge.

---

## 1. The Theme is the Single Source of Truth

All visual tokens (colors, spacing, typography, shadows, borders) live in one place:

| File | Purpose |
|------|---------|
| `src/HVO.WebSite.Themes/wwwroot/css/themes/hvo-shared-shell.css` | Shell layout, theme tokens (`--shell-*`), canonical palette (`--hvo-series-*`, `--hvo-accent-*`) |
| `src/HVO.WebSite.Themes/wwwroot/css/themes/hvo-components.css` | All reusable component classes (`hvo-card`, `hvo-chip`, `hvo-button`, etc.) |

If you need a color, spacing value, or component style and it already exists in one of these files — use the variable or class. **Do not re-invent it.**

---

## 2. Hard Rules — Never Violate These

### 2.1 No hardcoded colors in per-project CSS

**Banned:**
```css
/* ❌ NEVER */
color: #f8fbff;
background: rgba(126, 157, 196, 0.14);
border: 1px solid rgba(248, 113, 113, 0.40);
```

**Required:**
```css
/* ✅ ALWAYS */
color: var(--shell-page-text);
background: var(--shell-overlay-panel);
border: 1px solid color-mix(in srgb, var(--shell-overlay-panel-border) 60%, var(--hvo-accent-danger) 40%);
```

This applies to every `.razor.css` file and every `wwwroot/app.css` in every project. The only exception is `hvo-showcase.css` contrast-override classes where the value itself IS the palette demo.

### 2.2 No pass-through alias variables

A variable that does nothing but forward to another variable adds indirection without value and creates drift risk.

**Banned:**
```css
/* ❌ NEVER — pure alias, zero added value */
.my-page {
    --my-text: var(--shell-page-text);
    --my-muted: var(--shell-pill-text);
    --my-card: var(--shell-card-background);
}
```

**Required:** Use the theme variable directly where needed.

**Allowed:** Local variables that perform real computation:
```css
/* ✅ ALLOWED — real derived value, not a pass-through */
.my-page {
    --panel-strong: color-mix(in srgb, var(--shell-card-background) 86%, var(--shell-overlay-panel) 14%);
    --status-overlay: linear-gradient(135deg,
        color-mix(in srgb, var(--hvo-accent-cyan) 4%, transparent),
        color-mix(in srgb, var(--hvo-accent-amber) 3%, transparent));
}
```

### 2.3 No local copies of theme classes

If `hvo-components.css` or `hvo-shared-shell.css` already defines a class, **do not redefine it** in a per-project file, even with slightly different values. Divergent copies break both theme switching and future global updates.

**Banned:** Redefining `.hvo-chip`, `.hvo-ring-gauge`, `.hvo-card`, `.shell-page-stack`, `.hvo-state-empty`, `.shell-brand-mark`, etc. in any per-project CSS.

**Required:** Use the class directly. If you need a variation, extend via a modifier class:
```css
/* ✅ EXTEND — only the deviation, not the whole class */
.power-status-card__eyebrow {
    color: var(--hvo-accent-cyan);   /* deviation from hvo-eyebrow default */
    letter-spacing: 0.18em;          /* deviation from hvo-eyebrow default */
}
```
```razor
<!-- ✅ apply both the base class and the modifier -->
<p class="hvo-eyebrow power-status-card__eyebrow">Power System</p>
```

### 2.4 No redefinition of `--shell-*` or `--hvo-*` root tokens in per-project files

These tokens are defined globally in `hvo-shared-shell.css` and apply to all projects simultaneously. Overriding them at `:root` or on a class that wraps the whole page silently changes the look for the entire project.

**Banned:**
```css
/* ❌ NEVER — changes global layout gap for the whole project */
:root { --shell-page-section-gap: 1rem; }

/* ❌ NEVER — changes shell tokens on a dead/mismatched selector */
.my-shell-wrapper.shell-theme-dark { --shell-card-background: ...; }
```

**Exception:** Intentional per-app background image override using the designated hook:
```css
/* ✅ ALLOWED — the designated override hook */
:root { --shell-page-hero-image: url('/images/my-observatory-bg.jpg'); }
```

### 2.5 No `@font-face` in per-project CSS

Fonts are declared once in `hvo-shared-shell.css` and served from the Themes RCL. Duplicating `@font-face` in `app.css` causes redundant HTTP requests and can cause rendering flashes.

**Banned:** Any `@font-face` block in any file other than `hvo-shared-shell.css`.

### 2.6 No `!important` in per-project CSS (with one exception)

`!important` in per-project CSS fights the cascade unpredictably. The only legitimate use is to override a MudBlazor internal style that cannot be reached by specificity alone.

If you think you need `!important`, first check whether the correct approach is:
- a more specific selector, or
- adding a utility class to `hvo-components.css`.

### 2.7 No inline `style` attributes with hardcoded color values

**Banned:**
```razor
<!-- ❌ NEVER -->
<div style="color: #ff7f7f; background: rgba(255, 127, 127, 0.2);">
```

**Required:** CSS variables resolve in inline styles:
```razor
<!-- ✅ ALLOWED — uses a CSS variable -->
<div style="color: var(--hvo-accent-danger);">

<!-- ✅ ALLOWED — runtime-computed value passed to a CSS custom property -->
<div class="hvo-ring-gauge" style="--hvo-ring-level: @SocPercent%; --hvo-ring-color: var(--hvo-accent-success);">

<!-- ✅ ALLOWED — layout value computed in C# (e.g., position along a scale) -->
<span class="hvo-barometer-marker" style="left: @markerPct.ToString("F1")%;">
```

### 2.8 Chart.js C# hex literals must reference the canonical palette variable

Chart.js renders to an HTML `<canvas>` whose 2D context cannot resolve CSS variables. Colors must be provided as hex literals in C# `HvoChartDataset` constructors. Every hex literal **must** have a comment linking it to the canonical palette variable:

```csharp
// ✅ REQUIRED — hex literal with canonical variable reference
new HvoChartDataset("Outside °F", data, "#69d3ff", ...)   // --hvo-series-1
new HvoChartDataset("Inside °F",  data, "#ffb86c", ...)   // --hvo-series-2
new HvoChartDataset("Sun alt °",  data, "#ffcf66", ...)   // --hvo-accent-amber
```

When the palette changes, both `hvo-shared-shell.css` and all C# hex literals must be updated in the same commit.

---

## 3. What Goes Where

| Situation | Where it goes |
|-----------|--------------|
| New reusable component style used in ≥2 projects | `hvo-components.css` |
| New token used by shell layout or multiple apps | `hvo-shared-shell.css` |
| Style unique to one page/project | Per-project `.razor.css` or `app.css` |
| Computed local token (derived from theme vars) | Per-project `.razor.css` block-scoped on the page class |
| C# chart dataset color | Hex literal in `HvoChartDataset` with `// --hvo-*` comment |
| ThemeSandbox demo/showcase CSS | `hvo-showcase.css` in ThemeSandbox `wwwroot/` only |

---

## 4. The Global CSS Change Process

Changes to `hvo-shared-shell.css` or `hvo-components.css` affect **all six projects** simultaneously. The following process is mandatory.

### 4.1 Adding a new CSS class or token

1. **Propose in ThemeSandbox first.** Add a live demo to `/css-reference` (for component classes) or `/palette` (for new tokens) in `HVO.ThemeSandbox`. A class that has no sandbox demo is not ready for production.
2. **Document the intended use.** Add a comment in the CSS file:
   ```css
   /* ── New component: hvo-alert ─────────────────────────────────────
      Use for dismissible callout banners. Modifier: hvo-alert-danger.
      Consumed by: DavisVantagePro2/AlarmActive, future apps. ──────── */
   ```
3. **Get agreement before touching production.** The sandbox demo is the "sign-off artifact." No new class goes into `hvo-components.css` without a demo and explicit sign-off.
4. **Validate the build.** Run `dotnet build` from the solution root — zero warnings, zero errors across **all** projects.

### 4.2 Modifying an existing CSS class or token

1. **Grep all projects first.** Before changing a class or variable, search for every usage:
   ```bash
   rg "hvo-chip-success|--hvo-accent-success" src/
   ```
2. **Assess the visual impact.** If the change affects rendering (not just a comment or structural refactor), do a visual check against the ThemeSandbox `/css-reference` page and at least one live gateway.
3. **Treat it as a cross-project change.** The commit must mention which projects are affected.

### 4.3 Removing or renaming a CSS class or token

1. **Deprecation comment first** (separate commit):
   ```css
   /* ⚠️ DEPRECATED — use .hvo-new-name instead. Remove after 2026-09-01. */
   .hvo-old-class { ... }
   ```
2. **Grep and migrate all usages** across all projects in a single commit.
3. **Delete the deprecated rule** only after all usages are gone.
4. **Never delete without a migration path.** If a class is in use, provide the replacement in the same PR.

### 4.4 Changing a palette color (`--hvo-series-*` or `--hvo-accent-*`)

1. Update `hvo-shared-shell.css`.
2. **Find and update all C# hex literals** that reference this color (grep for the old hex value across all `.razor.cs` files).
3. Update the ThemeSandbox Palette page — it serves as the visual confirmation.
4. Run `dotnet build` — zero warnings, zero errors.

---

## 5. Audit Checklist

Run this checklist when reviewing any CSS-touching PR. If any item fails, request changes.

### Per-file checks
- [ ] No hardcoded `#hex`, `rgb()`, or `rgba()` color values in `.razor.css` or `app.css`
- [ ] No pass-through alias variables (`--local: var(--shell-something)` with no computation)
- [ ] No redefinition of a class already in `hvo-components.css` or `hvo-shared-shell.css`
- [ ] No `--shell-*` or `--hvo-*` tokens redefined at `:root` in a per-project file
- [ ] No `@font-face` blocks outside `hvo-shared-shell.css`
- [ ] No inline `style=""` attributes with hardcoded color values in `.razor` files
- [ ] `gap: 12px` → `gap: var(--shell-card-gap)` where applicable
- [ ] `font-family: "Open Sans..."` → `font-family: var(--shell-font-family)` where applicable

### C# chart color checks
- [ ] All `HvoChartDataset` hex color literals have `// --hvo-*` comments
- [ ] Hex values match the canonical palette in `hvo-shared-shell.css`

### Global change checks (only when `hvo-components.css` or `hvo-shared-shell.css` is modified)
- [ ] ThemeSandbox demo exists for any new class/token
- [ ] All existing usages grepped and confirmed unbroken
- [ ] `dotnet build` passes across ALL projects (not just the changed one)
- [ ] Visual check against ThemeSandbox and at least one gateway

---

## 6. Available Theme Tokens — Quick Reference

### Semantic palette (always use these; never hardcode the hex values)

| Variable | Dark hex | Use |
|----------|----------|-----|
| `--hvo-series-1` | `#69d3ff` | Temp outside, wind avg |
| `--hvo-series-2` | `#ffb86c` | Temp inside, secondary |
| `--hvo-series-3` | `#ffd166` | Solar radiation, sun arc fill |
| `--hvo-series-4` | `#57d38d` | Positive / healthy values |
| `--hvo-series-5` | `#ff9f5a` | Wind gust, caution / alert |
| `--hvo-series-6` | `#9fb8d4` | Moon arc, muted secondary |
| `--hvo-series-7` | `#7ae0ff` | Wind avg alt, second blue |
| `--hvo-series-8` | `#f38ba8` | Alarm / critical |
| `--hvo-accent-blue` | `#6da5ff` | Active links, hero readouts |
| `--hvo-accent-cyan` | `#83e4ff` | Secondary highlights |
| `--hvo-accent-amber` | `#ffcf66` | Warm readouts, sun marker |
| `--hvo-accent-success` | `#57d38d` | Online / positive states |
| `--hvo-accent-danger` | `#ff7f7f` | Offline / alarm states |

### Key surface tokens

| Variable | Use |
|----------|-----|
| `--shell-card-background` | Card / panel surface fill |
| `--shell-card-foreground` | Primary text on cards |
| `--shell-card-border` | Card outer border |
| `--shell-card-inner-border` | Inner highlight ring (inset glow) |
| `--shell-card-shadow` | Card drop shadow |
| `--shell-overlay-panel` | Inset panel / chip background |
| `--shell-overlay-panel-border` | Inset panel border |
| `--shell-page-text` | Body text color |
| `--shell-pill-text` | Muted / secondary text |
| `--shell-brand-title` | App name / hero heading |
| `--shell-overlay-link` | Link color |
| `--shell-font-family` | Body typeface |
| `--shell-font-mono` | Monospace typeface |
| `--shell-card-gap` | `12px` — gap between cards |
| `--shell-page-padding` | `1rem` — page content inset |
| `--shell-page-section-gap` | `0.9rem` — gap between sections |
| `--shell-chart-grid-color` | Chart grid line color |
| `--shell-chart-axis-color` | Chart axis line color |
| `--shell-chart-label-color` | Chart axis label color |
| `--shell-chart-marker-fill` | Chart point marker fill |
| `--shell-chart-empty-background` | Empty chart placeholder bg |
| `--shell-chart-empty-border` | Empty chart placeholder border |

Full definitions: `src/HVO.WebSite.Themes/wwwroot/css/themes/hvo-shared-shell.css`

---

## 7. Deprecated Items

Do not introduce new uses of these. Migrate existing uses when touching a file.

Completed cleanup: the Davis `PrototypeFrame.razor` component was removed in issue #212 after all `proto-*` page usages were migrated.

| Item | Replacement | Notes |
|------|-------------|-------|
| `hvo-dark.css` | `hvo-shared-shell.css` | Entire file deprecated; still present for backward compat |
| `.jk-*` classes in `hvo-components.css` | `hvo-*` classes | Legacy shim; remove when JkBms app is fully migrated |
| `.smartshunt-*` classes in `hvo-components.css` | `hvo-*` classes | Legacy shim |
| `.proto-*` class names | `hvo-*` equivalents | Fully migrated; class names must not reappear. Legacy `PrototypeFrame.razor` was removed in issue #212. |
