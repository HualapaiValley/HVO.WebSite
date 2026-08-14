# HVO.WebSite.Themes - Shared UI Design System

Razor Class Library providing the shared HVO shell layouts, MudBlazor theme, web assets, and reusable CSS primitives for HVO Blazor applications.

## Package Information

- **Target Framework**: .NET 10.0
- **Type**: Razor Class Library (RCL)
- **Static Web Assets**: CSS themes, fonts, icons

## Purpose

Centralized theme and design system for:
- Consistent branding across all HVO web properties
- Dark-first astronomy UI optimized for nighttime observatory use
- CSS custom properties for easy theme customization
- Self-hosted web fonts (eliminates external CDN dependencies)

## Structure

```
HVO.WebSite.Themes/
├── wwwroot/
│   ├── css/
│   │   └── themes/
│   │       ├── hvo-shared-shell.css  # shell layout, palette variables, light/dark themes
│   │       ├── hvo-components.css    # shared card, metric, chip, gauge, and state primitives
│   │       └── hvo-dark.css          # deprecated compatibility stylesheet
│   └── fonts/
│       └── [custom-fonts]            # Self-hosted web fonts
├── Components/
│   ├── Charts/                       # HvoChart wrapper
│   ├── Format/                       # HvoFormat and UnitSystem
│   └── Layout/                       # HvoGatewayLayout, HvoPublicLayout, HvoAdminLayout
└── HVO.WebSite.Themes.csproj
```

## Shared Theme Contract

The RCL is the source of truth for visual standards. Consuming apps should use the shared layouts and `hvo-*` CSS primitives for common UI surfaces, then keep app CSS limited to layout glue or truly device-specific controls.

### Layouts

- `HvoGatewayLayout` for gateway dashboards with top navigation and five footer status slots.
- `HvoPublicLayout` for public website routes.
- `HvoAdminLayout` for admin routes with sidebar navigation.
- `ShellLayoutState` for shared theme state, page metadata, and footer slots.
- `HvoTheme.Create()` for the unified MudBlazor palette.

### CSS Primitives

- `hvo-card`, `hvo-card-primary`, `hvo-card-note`, `hvo-card-title`, `hvo-eyebrow`
- `hvo-metric`, `hvo-metric-label`, `hvo-metric-value`, `hvo-mono`
- `hvo-chip-row`, `hvo-chip`, `hvo-chip-online`, `hvo-chip-warning`, `hvo-chip-error`
- `hvo-ring-gauge`, `hvo-ring-gauge-value`, `hvo-gauge-grid`
- `hvo-cell-grid`, `hvo-cell-tile`, `hvo-cell-tile-high`, `hvo-cell-tile-low`, `hvo-cell-num`, `hvo-cell-v`
- `hvo-device-title-row`, `hvo-device-meta`, `hvo-device-meta-caption`
- `hvo-surface-panel`, `hvo-state-empty`, `hvo-state-error`

These primitives were promoted from the ThemeSandbox showcase so the production apps and the reference catalog share one implementation.

## Integration

### 1. Add Project Reference

```xml
<ItemGroup>
  <ProjectReference Include="..\HVO.WebSite.Themes\HVO.WebSite.Themes.csproj" />
</ItemGroup>
```

### 2. Reference Theme Stylesheets

```html
<link rel="stylesheet" href="_content/MudBlazor/MudBlazor.min.css" />
<link rel="stylesheet" href="_content/HVO.WebSite.Themes/css/themes/hvo-shared-shell.css" />
<link rel="stylesheet" href="_content/HVO.WebSite.Themes/css/themes/hvo-components.css" />
```

`hvo-dark.css` is retained only for older Bootstrap-era routes during migration.

### 3. Apply Theme Classes

```html
<div class="shell-theme-dark">...</div>
<div class="shell-theme-light">...</div>
```

## Dependencies

- MudBlazor
- Chart.js for `HvoChart` consumers

## Used By

- `HVO.WebSite.v9` - Main observatory website
- `HVO.ThemeSandbox` - Shared component and design-system reference app

The active Davis, JK BMS, EG4, and SmartShunt collectors are headless and do not consume this UI library. Kasa and Govee presentation is owned by Home Assistant.

## Design Philosophy

### Dark-First for Astronomy
- Preserve night vision with dark backgrounds
- High contrast for legibility in total darkness
- Muted backgrounds to reduce eye strain

### CSS Custom Properties Over Sass
- Runtime theming without rebuilding
- Browser DevTools live-editing
- No build step for CSS changes

## Site-Specific Overrides

Consuming app CSS may define layout density, grid placement, and device-specific controls. It should not redefine shared surface colors, fonts, card borders, metric rows, or status chips. Use the shared `hvo-*` classes instead.

```html
<link rel="stylesheet" href="app.css" />
<link rel="stylesheet" href="_content/HVO.WebSite.Themes/css/themes/hvo-shared-shell.css" />
<link rel="stylesheet" href="_content/HVO.WebSite.Themes/css/themes/hvo-components.css" />
```

Loading app CSS before the shared shell and component CSS lets the RCL remain the final authority for common theme surfaces.
