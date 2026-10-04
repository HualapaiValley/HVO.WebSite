# Website and ThemeSandbox browser tests

This MSTest/Playwright assembly references the real [website](../../src/HVO.WebSite.v9/README.md) and [ThemeSandbox](../../src/HVO.ThemeSandbox/README.md). Its non-live cases have `Browser`; deployed Home Assistant dashboard checks have `Live`. [Central test ownership](../README.md#hvowebsiteplaywrighttests), [test lanes](../../docs/development/testing.md) and [selective CI](../../docs/development/selective-ci.md) own the repository-wide requirements.

## Setup

From the repository root, use the exact SDK in [global.json](../../global.json), restore/build, then install the project's Chromium version and system dependencies:

```bash
dotnet restore HVO.WebSite.sln --locked-mode --nologo
dotnet build HVO.WebSite.sln --no-restore --nologo
pwsh tests/HVO.WebSite.PlaywrightTests/bin/Debug/net10.0/playwright.ps1 install --with-deps chromium
```

The generated `playwright.ps1` exists after the build. PowerShell and the required Linux browser libraries must be installed; downloading Chromium alone does not install those libraries. Use the corresponding output directory if deliberately building Release. No deployed application or physical gateway is a setup prerequisite.

Run all non-live cases with the integration session bound and a dedicated report directory:

```bash
dotnet test tests/HVO.WebSite.PlaywrightTests/HVO.WebSite.PlaywrightTests.csproj --no-build --no-restore --settings integration.runsettings --filter "TestCategory!=Integration&TestCategory!=Live" --logger trx --results-directory TestResults/browser/HVO.WebSite.PlaywrightTests
python3 tools/ci-results.py TestResults/browser/HVO.WebSite.PlaywrightTests
```

The full local solution filter also includes these Browser tests; focused execution does not replace full validation. Selected CI builds whole selected assemblies, installs Chromium in the browser lane and uses fresh lane/project reports. No obsolete ignored scaffolds or ignored-result allowance is part of current qualification.

## Fixture ownership

- [BrowserApplication](Infrastructure/BrowserApplication.cs) starts real Kestrel hosts on OS-assigned loopback ports, loads local static web assets, checks loopback binding and owns bounded teardown. ThemeSandbox uses its actual application with Development configuration.
- [WebsiteBrowserApplication](Infrastructure/WebsiteBrowserApplication.cs) uses the actual website under Testing, replacing databases/providers with isolated deterministic data, a fake clock and per-request test authentication. It owns temporary Data Protection keys. The real OIDC challenge handler remains; static discovery configuration and test interception avoid contacting the identity provider. This is UI/auth behavior evidence, not a SQL-provider or real Entra sign-in drill.
- [HomeAssistantWeatherWindCardPlaywrightTests](HomeAssistantWeatherWindCardPlaywrightTests.cs) renders the tracked card module with test-owned HA state in an isolated page. It is Browser, does not connect to HA, and exercises direction/freshness/missing-data/responsive behavior.
- [BrowserSession](Infrastructure/BrowserSession.cs) gives each owned case its own headless Chromium browser/context, bounded navigation/actions and diagnostics. Tests do not inherit a production URL, token or manually running host.

## Behavior and failure evidence

The [main-site](MainSitePlaywrightTests.cs) and [sandbox](ThemeSandboxPlaywrightTests.cs) suites exercise public/authorized navigation, controls, reference routes and responsive surfaces. [Dashboard](WebsiteDashboardBrowserTests.cs), [chart/theme](ScopedChartThemeBrowserTests.cs), [instrument styles](ThemeSandboxInstrumentStyleBrowserTests.cs) and [power history](PowerHistoryBrowserTests.cs) cases assert actual Chart.js instances/data/configuration, nullable gaps, theme changes, computed styles, UTC spacing/display-zone labels, provider failure recovery and empty states. A visible canvas, stylesheet request or button alone is insufficient.

[BrowserFailureQualificationTests](BrowserFailureQualificationTests.cs) injects a missing chart script and a blocked theme handler, proves the same positive assertions fail, then verifies recovery. Expected injected diagnostics are distinguished from unexpected page/console errors. Shared visual changes must also satisfy [CSS governance](../../docs/CSS_GOVERNANCE.md): reachable sandbox demos/sign-off, affected website and sandbox surfaces in both themes, and relevant responsive states.

## Retained artifacts

Owned Browser cases retain a unique `<test-name>-<guid>` directory under `TestResults/browser` by default. `HVO_BROWSER_ARTIFACTS` can select a separate evidence root. Each case attaches `console.log`, `page.png` or `failure.png`, and `trace.zip` to its TRX; an empty console log is valid when no message occurred. Do not share or overwrite artifact directories across cases. Unexpected errors fail assertions; CI retains `TestResults/**` even on failure.

The [report verifier](../../tools/ci-results.py) rejects missing/stale/empty, failed, ignored or inconclusive reports. Preserve screenshots, console logs, traces and the exact source/command alongside TRX. Source-bound approval and fresh matching CI remain required; these fixtures do not establish deployment success.

## Live boundary

The separate HA Kasa, weather, operations and Energy dashboard suites opt in through `HVO_HOME_ASSISTANT_URL` and `HOME_ASSISTANT_TOKEN` and carry `Live`. They inspect a configured deployed HA and its entities; ordinary Browser/full local non-live commands exclude them. Running those checks needs applicable target authorization and prerequisites from [HA guidance](../../deploy/home-assistant/README.md). Their current implementation does not use the owned website/ThemeSandbox host or imply the same diagnostic-artifact contract.
