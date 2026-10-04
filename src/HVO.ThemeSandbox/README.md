# HVO.ThemeSandbox

ThemeSandbox is the local reference and validation app for the shared [Themes RCL](../HVO.WebSite.Themes/README.md). It is a Blazor Interactive Server/MudBlazor consumer, not a production deployment or hardware gateway. [Program.cs](Program.cs) registers Razor interactive components and MudBlazor; [App.razor](Components/App.razor) serves the RCL's CSS, fonts and local chart scripts. The only project dependency is Themes; no SQL, collector, HA or secret configuration is needed for its demos.

## Run locally

Use the exact SDK in [global.json](../../global.json). From the repository root:

```bash
dotnet restore HVO.WebSite.sln --locked-mode --nologo
dotnet build HVO.WebSite.sln --no-restore --nologo
ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/HVO.ThemeSandbox/HVO.ThemeSandbox.csproj --no-build --no-restore --no-launch-profile --urls http://127.0.0.1:5088
```

Port 5088 is explicitly selected for this example, not a checked-in launch-profile/default port. Stop the local process after use. [appsettings.json](appsettings.json) and [Development settings](appsettings.Development.json) configure logging; the [router](Components/Routes.razor) uses SandboxGatewayLayout unless a page selects another layout.

## Reachable references

| Route | Reference responsibility |
|---|---|
| `/` | [Gateway demo](Components/Pages/GatewayDemo.razor) and shared shell/navigation |
| `/css-reference` | [Reusable CSS catalog](Components/Pages/HvoCssSection.razor) |
| `/palette` | [Canonical palette/token examples](Components/Pages/Palette.razor) |
| `/controls` | [Control catalog](Components/Pages/ControlsCatalog.razor) |
| `/instruments` | [Instrument demos](Components/Pages/InstrumentDemos.razor) |
| `/theme-showcase`, `/dashboard` | [Showcase](Components/Pages/ThemeShowcase.razor) and [dashboard template](Components/Pages/DashboardTemplate.razor) |
| `/states`, `/responsive` | [Empty/error/state patterns](Components/Pages/StatePatterns.razor) and [responsive references](Components/Pages/Breakpoints.razor) |
| `/public-layout`, `/admin-layout` | [Public](Components/Pages/PublicLayoutDemo.razor) and [admin](Components/Pages/AdminLayoutDemo.razor) shell demos |

These pages illustrate UI contracts; they do not expose live collector controls or prove deployment readiness. Current collectors are headless.

## Shared changes and validation

[CSS governance](../../docs/CSS_GOVERNANCE.md) is authoritative for tokens, palette/font ownership, shared components/layouts, chart colors and sandbox demos/sign-off. Add or register a reachable demo in the appropriate reference before promoting a new shared style/component. Inspect both active consumers, website and sandbox, in light/dark themes and relevant responsive states. Use shared ShellLayoutState, HvoFormat and HvoChart; per-app CSS is limited to justified layout/showcase differences.

The sandbox retains its existing deprecated `hvo-dark.css` compatibility include. The current website omits it; this include is not a requirement for new consumers. [Themes integration](../HVO.WebSite.Themes/README.md#integration) explains current asset ownership/load order.

The [owned Playwright fixture](../../tests/HVO.WebSite.PlaywrightTests/README.md) starts this actual application on an OS-assigned loopback port and exercises routes, controls, charts, computed styles, responsive layout and both themes. It requires Chromium/system dependencies, not a manually running sandbox. Run its focused non-live command after preparation, and retain screenshots/console/traces. Full exact-SDK zero-warning build and local non-live validation still follow [testing](../../docs/development/testing.md).
