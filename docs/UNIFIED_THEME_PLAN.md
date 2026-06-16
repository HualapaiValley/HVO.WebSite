# Unified Theme & Layout Plan

**Status**: Active migration reference
**Last updated**: 2026-06-16
**Issues**: #153 (epic), #154–#163 (sub-tasks)

## Overview

Replace the current fragmented UI (Bootstrap 5.3 main site + per-app custom CSS across 5 MudBlazor gateways) with a unified theme system shared through `HVO.WebSite.Themes` (Razor Class Library). The plan introduces three layout variants, a central formatting utility with runtime unit configuration, a consistent component palette, and optional charting via Chart.js with dynamic theming.

### Current State

| App | UI Library | Layout | Card CSS | Theme State |
|-----|-----------|--------|----------|-------------|
| HVO.WebSite.v9 | Bootstrap 5.3 | NavMenu + footer | Custom bootstrap | None |
| DavisVantagePro2 | MudBlazor | ShellLayoutState + forked app.css | Inline | Copy-pasted ShellLayoutState |
| JK BMS | MudBlazor | ShellLayoutState | jk-metric, jk-detail-block | Copy-pasted ShellLayoutState |
| SmartShunt | MudBlazor | ShellLayoutState | smartshunt-card, smartshunt-* | Copy-pasted ShellLayoutState |
| SolarAssistant | MudBlazor | Direct (no ShellLayoutState) | solar-card, solar-* | No ShellLayoutState |
| TplinkKasa | MudBlazor | Direct | Kasa-specific | No ShellLayoutState |

All 5 MudBlazor apps already reference `HVO.WebSite.Themes` for `hvo-shared-shell.css` and `hvo-dark.css`.

---

## Phase 0 — Shared RCL Foundation

**Goal**: Create the shared components, CSS, and utilities in `HVO.WebSite.Themes` that all apps will consume.

### 0.1 — HvoFormat Utility Class

**File**: `src/HVO.WebSite.Themes/Components/Format/HvoFormat.cs`

```csharp
namespace HVO.WebSite.Themes.Components.Format;

public enum UnitSystem { Metric, Imperial }

public static class HvoFormat
{
    public static string Timestamp(DateTime? utc, string? format = null)
        => utc?.ToLocalTime().ToString(format ?? "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? "--";

    public static string FooterTimestamp(DateTime? utc)
        => Timestamp(utc, "dd MMM yyyy - h:mm:ss tt");

    public static string Temperature(double? celsius, UnitSystem units = UnitSystem.Metric)
        => celsius is null ? "--"
            : units == UnitSystem.Imperial
                ? $"{celsius.Value * 9.0 / 5.0 + 32.0:F1} °F"
                : $"{celsius.Value:F1} °C";

    public static string Speed(double? ms, UnitSystem units = UnitSystem.Metric)
        => ms is null ? "--"
            : units == UnitSystem.Imperial
                ? $"{ms.Value * 2.23694:F1} mph"
                : $"{ms.Value * 3.6:F1} km/h";

    public static string PressureInHg(double? inhg, UnitSystem units = UnitSystem.Metric)
        => inhg is null ? "--"
            : units == UnitSystem.Imperial
                ? $"{inhg.Value:F2} inHg"
                : $"{inhg.Value * 33.8639:F1} hPa";

    public static string Voltage(double? volts, int decimals = 2)
        => volts is null ? "--" : $"{volts.Value:F{decimals}} V";

    public static string Current(double? amps)
        => amps is null ? "--" : $"{amps.Value:F1} A";

    public static string Power(double? watts)
        => watts is null ? "--" : $"{watts.Value:F0} W";

    public static string EnergyAh(double? ah)
        => ah is null ? "--" : $"{ah.Value:F1} Ah";

    public static string Rain(double? inches)
        => inches is null ? "--" : $"{inches.Value:F2} in";

    public static string Percent(double? value)
        => value is null ? "--" : $"{value.Value:F0} %";

    public static string Duration(TimeSpan? ts)
        => ts is null ? "--" : ts.Value.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
}
```

**UnitSystem cascade**: A cascading parameter `UnitSystem` flows from the layout root. An `IUserPreferencesService` (stored in localStorage) persists the user's choice. The app default comes from `appsettings.json`:

```json
{
  "HvoFormatting": {
    "UnitSystem": "Metric"
  }
}
```

### 0.2 — ShellLayoutState (Shared)

**File**: `src/HVO.WebSite.Themes/Components/Layout/ShellLayoutState.cs`

Extract from the 3 copy-pasted versions. Same API surface:
- `IsDarkMode`, `CurrentSection`
- `SetTheme(bool)`, `ToggleTheme()`
- `SetFooter(params ShellFooterItem[])` (5 slots)
- `FooterSlot1` through `FooterSlot5` — each is a `ShellFooterItem` with `Text` + optional `Indicator` (Online/Warning/Offline/None)
- `event Action? Changed`

### 0.3 — MudTheme (Unified)

**File**: `src/HVO.WebSite.Themes/Components/Layout/HvoTheme.cs`

```csharp
public static class HvoTheme
{
    public static MudTheme Create() => new()
    {
        PaletteLight = new PaletteLight
        {
            Primary = "#2d5fb7",
            Secondary = "#2d8b79",
            Background = "#ecf3fb",
            Surface = "#fbfdff",
            AppbarBackground = "rgba(255,255,255,0)",
            AppbarText = "#13263f",
            TextPrimary = "#10233f",
            TextSecondary = "#4f6887"
        },
        PaletteDark = new PaletteDark
        {
            Primary = "#6da5ff",
            Secondary = "#57bca6",
            Background = "#08111f",
            Surface = "#1f2937",
            AppbarBackground = "rgba(0,0,0,0)",
            AppbarText = "#f8fbff",
            TextPrimary = "#f8fbff",
            TextSecondary = "#9fb4d5"
        }
    };
}
```

Apps can override individual palette values via `MudTheme.PaletteLight/Dark` setters after calling `Create()`.

### 0.4 — HvoGatewayLayout Component

**File**: `src/HVO.WebSite.Themes/Components/Layout/HvoGatewayLayout.razor`

**Parameters**:
- `RenderFragment NavItems` — nav pills rendered in the header
- `RenderFragment AppBarActions` — icon buttons (theme toggle, app-specific actions)
- `ShellFooterItem? FooterSlot1` through `FooterSlot5`
- `bool IsDarkMode`
- `EventCallback<bool> IsDarkModeChanged`
- `MudTheme? Theme` (optional override, uses `HvoTheme.Create()` by default)

**Structure**:
```
MudThemeProvider (Theme)
MudDialogProvider
MudLayout
  MudAppBar (shell-appbar, fixed top)
    Brand icon + "Hualapai Valley Observatory"
    MudText subtitle (uppercase, e.g. "JK BMS DASHBOARD")
    NavItems render fragment
    spacer
    AppBarActions render fragment
  MudMainContent
    @Body
  MudAppBar (shell-appbar-footer, fixed bottom)
    5 footer slots with status dot indicators
```

The component applies `.shell-theme-dark` or `.shell-theme-light` CSS class based on `IsDarkMode`.

### 0.5 — HvoPublicLayout Component

**File**: `src/HVO.WebSite.Themes/Components/Layout/HvoPublicLayout.razor`

For the main site non-admin routes. Clean, content-forward layout with minimal branding footer. No status bar.

**Parameters**:
- `RenderFragment NavItems` — top nav (observatory info, weather, gallery, contact)
- `RenderFragment AuthSection` — login/profile controls
- `string? BrandTitle`, `string? BrandSubtitle`

### 0.6 — HvoAdminLayout Component

**File**: `src/HVO.WebSite.Themes/Components/Layout/HvoAdminLayout.razor`

For the main site `/admin/*` routes. Sidebar navigation + content area.

**Parameters**:
- `RenderFragment SidebarItems`
- `RenderFragment AppBarActions`
- `bool IsDarkMode` / `EventCallback<bool> IsDarkModeChanged`

### 0.7 — Shared Card & Metric CSS

**File**: `src/HVO.WebSite.Themes/wwwroot/css/themes/hvo-components.css`

Define shared component classes replacing per-app custom CSS:

```css
.hvo-card {
    background: var(--shell-card-background);
    color: var(--shell-card-foreground);
    border: 1px solid var(--shell-card-border);
    border-radius: 12px;
    padding: 1.25rem;
    box-shadow: var(--shell-card-shadow);
}

.hvo-card-primary {
    border-left: 4px solid var(--shell-pill-active-background);
}

.hvo-card-note {
    background: var(--shell-overlay-panel);
    border-color: var(--shell-overlay-panel-border);
    opacity: 0.85;
}

.hvo-metric {
    display: flex;
    justify-content: space-between;
    align-items: baseline;
    padding: 0.4rem 0;
    border-bottom: 1px solid var(--shell-card-inner-border);
}

.hvo-metric:last-child {
    border-bottom: none;
}

.hvo-metric-label {
    color: var(--shell-pill-text);
    font-size: 0.8125rem;
}

.hvo-metric-value {
    font-family: var(--shell-font-mono);
    font-size: 0.9375rem;
    font-weight: 600;
    color: var(--shell-card-foreground);
}

.hvo-card-title {
    font-size: 1.125rem;
    font-weight: 600;
    margin-bottom: 0.75rem;
    color: var(--shell-card-foreground);
}

.hvo-eyebrow {
    font-size: 0.6875rem;
    text-transform: uppercase;
    letter-spacing: 0.08em;
    color: var(--shell-pill-text);
    margin-bottom: 0.25rem;
}

.hvo-mono {
    font-family: var(--shell-font-mono);
}
```

### 0.8 — Font Consolidation

Move the `@font-face` for "Open Sans Custom" into the Themes RCL (`wwwroot/fonts/` or as an embedded resource). Ensure the shell CSS loads it before the font-family declarations. Remove the copy in Davis `Status.razor.css`.

### 0.9 — Theme-Aware Chart.js Wrapper

**File**: `src/HVO.WebSite.Themes/Components/Charts/HvoChart.razor`

A Blazor component that wraps Chart.js (via ChartJs.Blazor NuGet package on each consuming app, or as a shared dependency).

**Theming mechanism**: On initialization and on theme toggle, invoke JS interop to read computed CSS custom properties from `document.documentElement`:

```javascript
window.hvoChart = {
    getThemeColors() {
        const style = getComputedStyle(document.documentElement);
        return {
            gridColor: style.getPropertyValue('--shell-chart-grid-color').trim(),
            axisColor: style.getPropertyValue('--shell-chart-axis-color').trim(),
            labelColor: style.getPropertyValue('--shell-chart-label-color').trim(),
            markerFill: style.getPropertyValue('--shell-chart-marker-fill').trim(),
        };
    },
    applyTheme(chartId) { ... }
};
```

Chart.js defaults are updated with these colors before each render/redraw. Chart.js adapters for date/time axis are included for time-series data.

**HvoChart parameters**:
- `string ChartId` — unique DOM id
- `ChartType Type` — Line, Bar, Doughnut, PolarArea, Bubble
- `IReadOnlyList<HvoChartDataset> Datasets`
- `IReadOnlyList<string>? Labels`
- `bool ShowLegend`
- `bool EnableAnimations` (default: false for performance)
- `int Height` (default: 300)
- `string? Title`
- `string? XAxisLabel`, `string? YAxisLabel`

### 0.10 — Clean Up hvo-dark.css

Remove Bootstrap-specific variables (`--bs-body-font-family`, `--bs-font-monospace`, `--bs-*`). These are only relevant to the main site which is being migrated away from Bootstrap. Deprecate the old `:root[data-theme="hvo-dark"]` selectors — after migration, all themes go through the shell CSS. The file remains for backward compat during migration.

### 0.11 — Per-App _Imports Cleanup

Ensure each app's `_Imports.razor` includes the shared Themes namespace:
```
@using HVO.WebSite.Themes.Components.Layout
@using HVO.WebSite.Themes.Components.Format
@using HVO.WebSite.Themes.Components.Charts
```

Remove per-app duplicates of `@using MudBlazor` where it's already provided by the Themes RCL's `_Imports.razor`.

---

## Phase 0.5 — Theme Sandbox Validation App

**Goal**: A small standalone MudBlazor Blazor Server app that exercises all Phase 0 components before migrating production apps.

### Project: HVO.ThemeSandbox

Creates a new project `src/HVO.ThemeSandbox/` (Blazor Server, .NET 10). It:
- References `HVO.WebSite.Themes`
- Demonstrates `HvoGatewayLayout` with dummy nav pills and footer slots
- Demonstrates `HvoPublicLayout`
- Demonstrates `HvoAdminLayout` with sidebar menu
- Demonstrates theme toggle (dark/light)
- Shows `HvoFormat` output for both Metric and Imperial unit systems (toggle button)
- Shows `HvoChart` with sample time-series data
- Exercises all `hvo-card`, `hvo-metric`, `hvo-eyebrow` CSS classes
- Tests `ShellLayoutState` integration
- Includes Playwright tests for:
  - Layout renders with correct brand text
  - Theme toggle switches dark/light
  - Unit toggle switches Metric/Imperial formatting
  - Chart renders without errors
  - Responsive behavior (AdminLayout sidebar collapses)
  - Navigation pills highlight correctly

### Validation Checklist (passed before Phase 1 starts)

- [ ] All three layouts render correctly in both themes
- [ ] Theme toggle is smooth (no flash, no layout shift)
- [ ] UnitSystem toggle correctly changes all HvoFormat output
- [ ] hvo-card / hvo-metric / hvo-eyebrow render consistently
- [ ] HvoChart shows themed colors that change on theme toggle
- [ ] ShellLayoutState footer slots update correctly
- [ ] Navigation pills highlight active section
- [ ] No Bootstrap CSS leaks into MudBlazor components
- [ ] Playwright tests all pass

---

## Phase 1 — Gateway App Migration

**Goal**: Migrate all 5 gateway apps to use the shared components from Phase 0, one app at a time.

### 1.1 — JK BMS (First Migration, simplest)

**Files to change**:
- `Components/Layout/MainLayout.razor` — wrap with `<HvoGatewayLayout>`
- `Components/Layout/MainLayout.razor.cs` — remove `ShellTheme`, reference `HvoTheme.Create()`, pass footer slots
- `Components/Layout/ShellLayoutState.cs` — delete, use shared
- `Components/Layout/MainLayout.razor.css` — remove any shell-specific styles
- `Components/Pages/Status.razor` (or equivalent) — replace inline format strings with `HvoFormat.*`
- `Components/Pages/DeviceDetail.razor` — replace `ToString("F1")` etc. with `HvoFormat.*`
- Per-app CSS files — replace `jk-metric`, `jk-detail-block` etc. with `hvo-metric`, `hvo-card`

**Playwright tests**:
- Gateway layout renders with brand name and correct subtitle
- All nav pills present and navigate correctly
- Footer shows 5 status slots with expected labels
- Theme toggle works
- Metric cards show correctly formatted values

### 1.2 — SmartShunt

Same as 1.1, plus:
- Unify secondary color from orange (#f28c28) to teal-green (#2d8b79)
- Replace `smartshunt-card`, `smartshunt-card-primary`, `smartshunt-card-note` with `hvo-card`, `hvo-card-primary`, `hvo-card-note`
- Replace `FormatTemperature` in `SmartShuntPageBase.cs` with `HvoFormat.Temperature`
- Add `ShellLayoutState` (it currently doesn't use one)

### 1.3 — SolarAssistant

Same as 1.1, plus:
- Add `ShellLayoutState` (it currently doesn't use one — manages theme state locally with `_isDarkMode`)
- Replace `solar-card`, `solar-card-link`, `solar-eyebrow`, `solar-hero-title` with shared classes
- Replace all `ToString(System.Globalization.CultureInfo.InvariantCulture)` with `HvoFormat.*`
- Replace `MudChip` status badges with consistent pattern using shared CSS

### 1.4 — TplinkKasa

Same as 1.1, plus:
- Add `ShellLayoutState` (doesn't use one)
- Replace all Kasa-specific formatting helpers (`FormatPercent`, `FormatKelvin`, `FormatWatts`, `FormatDegrees`, `FormatMilliseconds`, `FormatNullable`) with `HvoFormat.*`
- Consolidate Kasa device card CSS into shared classes

### 1.5 — DavisVantagePro2 (Most Complex)

Same as 1.1, plus:
- Remove **entire forked `app.css`** (~800 lines of duplicated `hvo-shared-shell.css`). The shell CSS comes from the Themes RCL now.
- Replace all SVG chart inline formatting with `HvoFormat.*` (the SVG chart components stay, just the format strings change)
- Remove the per-app `@font-face` for "Open Sans Custom"
- Remove `app.css` `@import` of shell CSS
- Verify no regression in the inline SVG charts

### Per-App Migration Checklist (for each app)

- [ ] `MainLayout.razor` updated to use `<HvoGatewayLayout>`
- [ ] Per-app `MudTheme` definition removed, uses `HvoTheme.Create()`
- [ ] Per-app `ShellLayoutState.cs` removed
- [ ] All `ToString("F*")` / `ToString(CultureInfo.InvariantCulture)` replaced with `HvoFormat.*`
- [ ] Per-app card/metric CSS classes replaced with shared `hvo-*`
- [ ] Any per-app `@font-face` removed
- [ ] Any forked shell CSS removed
- [ ] `_Imports.razor` includes shared Themes namespaces
- [ ] Builds with zero warnings, zero errors
- [ ] Playwright tests pass
- [ ] Manual visual check: dark/light theme looks correct

---

## Phase 2 — Main Site (HVO.WebSite.v9) Migration

**Goal**: Replace Bootstrap 5.3 with MudBlazor + shared components.

### 2.1 — Add MudBlazor Dependency

Add `<PackageReference Include="MudBlazor" />` to `HVO.WebSite.v9.csproj`. Currently `Directory.Packages.props` already has MudBlazor versioned (used by all 5 gateways). Add `_Imports.razor` entries.

### 2.2 — Remove Bootstrap CDN

From `Components/App.razor`:
- Remove `<link href="https://cdn.jsdelivr.net/npm/bootstrap@5.3.3/dist/css/bootstrap.min.css" ...>`
- Remove `<link href="https://cdn.jsdelivr.net/npm/bootstrap-icons@1.13.1/font/bootstrap-icons.css" ...>`
- Remove `<script src="https://cdn.jsdelivr.net/npm/bootstrap@5.3.3/dist/js/bootstrap.bundle.min.js" ...>`
- Remove any Bootstrap JS initialization

Replace with:
- `<link href="_content/HVO.WebSite.Themes/css/themes/hvo-shared-shell.css" rel="stylesheet" />`
- MudBlazor CSS (from MudBlazor package)
- Main site's own CSS

### 2.3 — Replace MainLayout.razor, NavMenu.razor, MainLayoutFooter.razor

**Route-based layout resolution** in `Components/App.razor` or `Routes.razor`:

```csharp
// In App.razor or via a custom Router
@* Public routes use HvoPublicLayout *@
@* /admin/* routes use HvoAdminLayout *@
```

Implementation: Two approaches:
1. **Per-page layout directive** — each admin page sets `@layout HvoAdminLayout`
2. **Router + LayoutView** — custom route matching in `Routes.razor`

Preferred: Approach 2 (cleaner, no per-page annotations):

```razor
<!-- Routes.razor -->
<Router AppAssembly="typeof(Program).Assembly">
    <Found Context="routeData">
        <RouteView RouteData="routeData" DefaultLayout="typeof(HvoPublicLayout)" />
        <FocusOnNavigate RouteData="routeData" Selector="h1" />
    </Found>
</Router>

<!-- Admin/_Imports.razor -->
@layout HvoAdminLayout
```

Or use a custom `LayoutProvider`:
```csharp
// Components/Layout/LayoutProvider.razor
@inherits LayoutComponentBase
@{
    Layout = Request.Path.StartsWith("/admin", StringComparison.OrdinalIgnoreCase)
        ? typeof(HvoAdminLayout)
        : typeof(HvoPublicLayout);
}
@Body
```

### 2.4 — Wire Up Auth UI

- Replace Bootstrap sign-in/sign-out buttons (Microsoft.Identity.Web UI) with MudBlazor components
- User avatar/menu in `HvoPublicLayout` / `HvoAdminLayout` header
- Preserve Entra ID auth flow (no auth logic changes, only UI skin)

### 2.5 — Main Site Playwright Tests

- Public layout renders with correct brand and nav items
- Admin layout shows sidebar with menu items
- Auth sign-in button redirects to Microsoft login
- Responsive: admin sidebar collapses on mobile
- Theme toggle (if exposed on public site)

---

## Phase 3 — Chart Integration (Optional, Per-Gateway)

**Goal**: Add charting via `HvoChart` wrapper to gateways where visual time-series adds value.

### 3.1 — Add ChartJs.Blazor package

To each gateway that will use charts. Or add to `Directory.Packages.props` and reference from `HVO.WebSite.Themes` if the RCL should own the chart dependency. (TBD: does the RCL reference ChartJs.Blazor directly, or does each gateway add it?)

**Recommendation**: `HVO.WebSite.Themes` references `ChartJs.Blazor` as a NuGet package. The `HvoChart` component lives in the Themes RCL. Consuming gateways get it transitively.

### 3.2 — Davis: Replace Inline SVG Charts (Optional)

Current Davis `Status.razor` has 4 inline SVG charts:
- Wind compass rose
- Sun/moon position
- Temperature/humidity history
- Barometric pressure history

These can stay as-is (they work and are performant) or be replaced with `HvoChart` for better theming. Decision deferred to implementation — start with new charts, migrate existing only if there's a clear benefit.

### 3.3 — JK BMS: Voltage History Chart

Add a line chart to `DeviceDetail.razor` showing the last N cell voltage readings for each bank. Uses `HvoChart` with time on X-axis and voltage on Y-axis.

### 3.4 — SmartShunt: Telemetry Trends

Add a line chart to `Telemetry.razor` showing voltage, current, and power over time. Multi-series `HvoChart`.

### 3.5 — SolarAssistant: Power History

The existing `PowerHistoryChart.razor` (SolarAssistant) already has a `CardHead` wrapper. Replace the placeholder content with an `HvoChart` showing power metrics over time.

---

## Phase 4 — Cleanup

**Goal**: Remove dead code and finalize consistency.

### 4.1 — Remove Dead CSS

- Remove Bootstrap-era CSS from `hvo-dark.css` (`--bs-*` vars, Bootstrap-specific selectors)
- Remove any per-app CSS files that are now empty after migration
- Verify no CSS files in gateway apps import shell CSS directly (should come from Themes RCL)

### 4.2 — Remove Per-App _Imports Duplicates

Ensure `_Imports.razor` in each app doesn't duplicate what the Themes RCL's `_Imports.razor` provides.

### 4.3 — Final Playwright Test Suite

Each gateway and the main site should have a comprehensive Playwright test suite covering:
- Layout rendering (all 3 variants)
- Theme toggle
- Navigation (pills, sidebar links)
- Unit system switching
- Card/metric rendering
- Chart rendering
- Responsive breakpoints

### 4.4 — Remove HVO.ThemeSandbox

After all production apps are migrated and verified, delete the sandbox project (it served its purpose).

---

## Complete Migration Order

```
Phase 0  (#154): RCL Foundation
    └─ 0.1 HvoFormat
    └─ 0.2 ShellLayoutState (shared)
    └─ 0.3 HvoTheme (unified MudTheme)
    └─ 0.4 HvoGatewayLayout
    └─ 0.5 HvoPublicLayout
    └─ 0.6 HvoAdminLayout
    └─ 0.7 Shared Card & Metric CSS (hvo-components.css)
    └─ 0.8 Font Consolidation
    └─ 0.9 HvoChart wrapper
    └─ 0.10 Clean up hvo-dark.css
    └─ 0.11 _Imports cleanup
         │
    Phase 0.5 (#155): ThemeSandbox Validation
         │
    Phase 1: Gateway Migration
    └─ 1.1 (#156) JK BMS (simplest, proves the pattern)
    └─ 1.2 (#157) SmartShunt (unify secondary color)
    └─ 1.3 (#158) SolarAssistant (add ShellLayoutState)
    └─ 1.4 (#159) TplinkKasa (add ShellLayoutState)
    └─ 1.5 (#160) DavisVantagePro2 (remove forked app.css)
         │
    Phase 2 (#161): Main Site Migration
    └─ 2.1 Add MudBlazor
    └─ 2.2 Remove Bootstrap CDN
    └─ 2.3 Replace layouts
    └─ 2.4 Wire auth UI
    └─ 2.5 Main site tests
         │
    Phase 3 (#162): Charts (per-gateway, optional)
    └─ 3.1 ChartJs.Blazor dep
    └─ 3.2 Davis SVG retain/replace
    └─ 3.3 JK BMS voltage chart
    └─ 3.4 SmartShunt telemetry chart
    └─ 3.5 SolarAssistant power chart
         │
    Phase 4 (#163): Cleanup
    └─ 4.1 Remove dead CSS
    └─ 4.2 Remove _Imports duplicates
    └─ 4.3 Final Playwright test suite
    └─ 4.4 Delete ThemeSandbox
```

---

## Testing Strategy

### Playwright Test Conventions

All Playwright tests follow these conventions:

```
src/HVO.AppName/Playwright/
    Pages/
        HomePage.cs
        AdminPage.cs
    Tests/
        LayoutTests.cs
        ThemeTests.cs
        NavigationTests.cs
        ChartTests.cs (if applicable)
```

Test patterns:
- Each test verifies one concern
- Use page object model for reuse
- Tests run against the dev server (not production)
- Both dark and light themes are tested where visuals matter

Example test structure:
```csharp
[Test]
public async Task GatewayLayout_RendersBrandText()
{
    await Page.GotoAsync("/");
    await Expect(Page.Locator(".shell-brand-text")).ToContainTextAsync("Hualapai Valley Observatory");
    await Expect(Page.Locator(".shell-brand-subtitle")).ToContainTextAsync("JK BMS DASHBOARD");
}

[Test]
public async Task ThemeToggle_SwitchesDarkMode()
{
    await Page.GotoAsync("/");
    var toggle = Page.Locator("button[aria-label*='theme']");
    await toggle.ClickAsync();
    await Expect(Page.Locator(".shell-theme-dark")).ToBeVisibleAsync();
}
```

### Manual Verification Checklist (per app)
- [ ] Dark theme: all text readable, contrast OK, cards visible
- [ ] Light theme: same checks
- [ ] Theme toggle smooth (no FOUC/layout shift)
- [ ] All nav pills navigate to correct pages
- [ ] Footer slots show live/accurate data
- [ ] Metric values use correct format and unit
- [ ] Unit toggle (if applicable) switches between Metric/Imperial
- [ ] No Bootstrap CSS visible or interfering
- [ ] Responsive: layout works at 1280px, 1024px, 768px, 375px widths
- [ ] Charts render and re-theme on toggle (if applicable)

---

## Key Design Decisions

1. **Shared layout in RCL, not per-app**: `HvoGatewayLayout`, `HvoPublicLayout`, `HvoAdminLayout` constructed once in `HVO.WebSite.Themes`. Each app's `MainLayout.razor` is a thin wrapper passing app-specific parameters.

2. **UnitSystem is cascading, not static**: Flows from layout root via `CascadingParameter`, allowing user override persisted in localStorage, falling back to appsettings.json default.

3. **Charts are optional, wrapper is shared**: `HvoChart` lives in Themes RCL. ChartJs.Blazor is a dependency of the Themes RCL. Gateways that don't use charts don't pay the JS payload (tree-shaken by the framework).

4. **Phase 0.5 sandbox prevents migration regrets**: Validating all components in isolation before touching production apps catches design issues early.

5. **Per-app migration order**: JK BMS first (simplest structure) → SmartShunt + SolarAssistant + TplinkKasa (medium) → Davis last (most complex, forked CSS, inline SVG charts).

6. **Secondary color unified**: All apps get teal-green (#2d8b79/#57bca6). SmartShunt's orange was unique but can be reintroduced via `MudTheme` override later if desired.
