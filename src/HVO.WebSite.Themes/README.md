# HVO.WebSite.Themes - Shared UI Design System

Razor Class Library providing the HVO Dark design system, shared web assets, and reusable CSS primitives for HVOv9 Blazor applications.

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
│   │       └── hvo-dark.css          # HVO Dark design system
│   └── fonts/
│       └── [custom-fonts]            # Self-hosted web fonts
└── HVO.WebSite.Themes.csproj
```

## HVO Dark Design System

### Color Palette (CSS Custom Properties)

#### Core Colors
```css
--hvo-body-bg: #05070d;              /* Deep space background */
--hvo-body-color: #f8fafc;           /* High-contrast text */
--hvo-accent: #3b82f6;               /* Blue accent (buttons, links) */
--hvo-accent-strong: #2563eb;        /* Darker blue (hover states) */
--hvo-accent-soft: rgba(59, 130, 246, 0.2);  /* Subtle highlights */
```

#### Semantic Colors
```css
--hvo-success-bg: rgba(34, 197, 94, 0.25);
--hvo-success-fg: #bbf7d0;
--hvo-danger-bg: rgba(248, 113, 113, 0.25);
--hvo-danger-fg: #fecaca;
--hvo-warning-bg: rgba(250, 204, 21, 0.25);
--hvo-warning-fg: #fef9c3;
--hvo-info-bg: rgba(56, 189, 248, 0.25);
--hvo-info-fg: #e0f2fe;
```

#### Surfaces & Borders
```css
--hvo-card-bg: linear-gradient(145deg, rgba(15, 23, 42, 0.92), rgba(15, 23, 42, 0.65));
--hvo-card-glass: rgba(30, 41, 59, 0.6);
--hvo-panel-shadow: 0 12px 35px rgba(15, 23, 42, 0.45);
--hvo-border-muted: rgba(148, 163, 184, 0.18);
--hvo-border-strong: rgba(148, 163, 184, 0.2);
```

## Integration

### 1. Add Project Reference

```xml
<ItemGroup>
  <ProjectReference Include="..\HVO.WebSite.Themes\HVO.WebSite.Themes.csproj" />
</ItemGroup>
```

### 2. Reference Theme Stylesheet

```html
<link rel="stylesheet" href="_content/HVO.WebSite.Themes/css/themes/hvo-dark.css" />
```

### 3. Enable Theme on Root Elements

```html
<html lang="en" data-theme="hvo-dark">
<body data-theme="hvo-dark">
```

## Dependencies

- **None** - Pure CSS, no JavaScript required
- Bootstrap 5.3 (expected to be loaded by consuming app)
- Bootstrap Icons (expected to be loaded by consuming app)

## Used By

- `HVO.WebSite.v9` - Main observatory website
- `HVO.Hardware.DavisVantagePro2` - Davis admin UI
- `HVO.Hardware.JkBms` - JK BMS monitoring UI
- `HVO.Hardware.VictronSmartShunt` - SmartShunt monitoring UI
- `HVO.Gateway.SolarAssistant` - SolarAssistant monitoring UI

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

In consuming apps, create a site-specific CSS file loaded after the theme:

```html
<link rel="stylesheet" href="_content/HVO.WebSite.Themes/css/themes/hvo-dark.css" />
<link rel="stylesheet" href="css/site-overrides.css" />
```

See `src/HVO.WebSite.v9/wwwroot/css/overrides/README.md` for the override convention.
