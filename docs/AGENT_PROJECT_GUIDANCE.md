# WebSite project guidance

Read [AGENTS.md](../AGENTS.md), the [repository profile](development/repository-profile.md) and the applicable canonical [.agents procedures](development/PROCESS.md#task-routing) before implementation or review. These are WebSite engineering requirements; they do not introduce another issue/review/CI lifecycle. Read [CSS governance](CSS_GOVERNANCE.md) before CSS or Blazor markup work.

## Current projects and ownership

| Area | Current owner and contract |
|---|---|
| Website | `src/HVO.WebSite.v9`: ASP.NET Core API and Blazor SSR/Interactive Server with MudBlazor; internally hosted on hvo-docker. [Website setup](../src/HVO.WebSite.v9/README.md) and [self-hosted deployment](../deploy/hvo-docker/README.md) state SQL/identity prerequisites. |
| Shared UI | `src/HVO.WebSite.Themes` Razor Class Library owns tokens, local fonts/Chart.js, reusable components/layouts, ShellLayoutState and HvoFormat. Website and `src/HVO.ThemeSandbox` are the active UI consumers; sandbox is a reference/validation app, not a production deployment. |
| Data | `src/HVO.DataModels` owns EF Core contexts/models/migrations. Keep async queries and explicit provider/schema contracts; archived `src/HVO.Database` is outside the active solution, retained for history/direct file-reading tests. |
| Direct collectors | Headless Davis, JK BMS, EG4 and Victron SmartShunt use shared Edge.Contracts/Hosting/Outbox and optional Home Assistant MQTT telemetry projections. JK also has the bounded, secret-gated [settings-password command](#jk-settings-password-command); do not call its whole MQTT interface read-only. Davis also consumes Staging celestial calculations. No App.razor, chart UI or gateway theme-load requirement exists for these collectors. |
| HA ownership | Home Assistant owns Kasa/Govee acquisition/presentation. Edge.Exporter.HomeAssistant is implemented but disabled in production with no mappings/source claims. Retired direct SolarAssistant/Kasa apps, containers and images are not deployment targets. |
| Deployment | `deploy/hvo-docker` owns website Compose; `deploy/pi-gateways` owns per-collector mounted configuration/secrets and durable volumes. [Gateway operations](GATEWAY_OPERATIONS.md) and [Pi deployment guidance](../deploy/pi-gateways/README.md) are canonical operational references. |

The SDK is pinned by [global.json](../global.json), with roll-forward disabled. Tests use MSTest, FluentAssertions, bUnit and MSTest/Playwright; discover counts from current reports rather than a fixed instruction count. [Testing](development/testing.md) identifies actual projects, filters, prerequisites, owned browser/SQL/HA fixtures and evidence. The current UI assets bundle Chart.js locally; inspect the pinned asset/package source when changing versions rather than infer one from old prose.

## Shared theme and Blazor

[CSS governance](CSS_GOVERNANCE.md) owns every token/font/color/layout rule and the cross-consumer sandbox/sign-off process. Review per-app CSS and Razor inline styles, shared RCL changes and their consumers. Preserve these priority checks:

- P0: fonts declared outside `hvo-shared-shell.css`; per-app hardcoded colors; per-app `--shell-*`/`--hvo-*` global token redefinitions outside the designated background hook; CDN dependence in any future offline edge UI.
- P1: copied shared classes/layouts, pass-through token aliases, hardcoded inline colors and redefined base shell classes.
- P2: spacing/font literals replacing available tokens, C# chart palette literals without canonical-variable comments, or new shared styles without a sandbox demo. A missing mandatory demo/sign-off still prevents acceptance.

Shared components belong in Themes RCL; use HvoGatewayLayout/HvoPublicLayout/HvoAdminLayout and ShellLayoutState from that library rather than copies. Use HvoFormat for formatted UI output (SVG coordinates are the existing exception), and HvoChart rather than bespoke canvas wrappers or direct Chart.js calls. Keep the Razor/code-behind/isolated-CSS triad for substantial logic/styles. Declare InteractiveServer where JS interop or live UI interaction needs it; SSR-only surfaces should remain SSR.

[Website App.razor](../src/HVO.WebSite.v9/Components/App.razor) loads MudBlazor, shared shell, shared components, app overrides, then scoped CSS. It does not load the deprecated `hvo-dark.css` or require a `data-theme="hvo-dark"` attribute. [ThemeSandbox App.razor](../src/HVO.ThemeSandbox/Components/App.razor) retains its existing compatibility stylesheet before MudBlazor; do not prescribe that legacy stylesheet for new consumers. Reuse actual shared theme state and verify both light/dark themes.

Guard OnAfterRenderAsync/refresh interop so exceptions cannot kill a circuit; preserve logging/diagnostics for failure. Do not capture scoped services in singletons. Avoid unnecessary StateHasChanged on every timer tick; dispose and await timers/JS object references through IAsyncDisposable. Use error boundaries where a critical UI section should not take down the page.

Every edge UI, if introduced in separately scoped work, must serve CSS/JS/fonts/images locally without internet. Use Themes RCL assets, including `_content/HVO.WebSite.Themes/js/chart.min.js`; add approved dependencies locally to the RCL rather than CDN links. The website's internet reachability is not permission to duplicate shared assets or weaken offline consumers.

## Headless runtime, configuration and health

Current collectors call [AddHvoEdgeRuntime](../src/HVO.Edge.Hosting/EdgeWebApplicationBuilderExtensions.cs) and [MapHvoEdgeRuntimeEndpoints](../src/HVO.Edge.Hosting/Diagnostics/EdgeDiagnosticsEndpointRouteBuilderExtensions.cs). Inspect collector-specific registration, options, migration and workers as well as shared hosting before changing a contract.

- [Mounted configuration](../src/HVO.Edge.Hosting/Configuration/EdgeConfigurationBuilderExtensions.cs): non-secret `gateway.json` defaults to `/app/config/gateway.json`, required in Production. `HVO_EDGE_CONFIG_FILE` selects an absolute path under `Edge:Paths:ConfigDirectory`. Environment overrides are reapplied after mounted JSON and win. JSON reload is disabled.
- [SecretFileResolver](../src/HVO.Edge.Hosting/Configuration/SecretFileResolver.cs) resolves configured filenames under `Edge:Paths:SecretsDirectory` (default `/run/secrets`), rejects traversal/symlinks/missing/empty/placeholder values, and reads credentials at startup. Keep device identity/address in approved non-secret mounted config or supported environment overrides, not embedded in code. Keep diagnostics, central-ingest and optional MQTT credentials distinct. Configuration/secret changes need an approved restart/replacement, not hot reload.
- Durable state belongs in the collector's named `/app/data` volume. Preserve identity/deduplication/outbox contracts and use the [quiescent SQLite backup/rollback procedure](gateways/sqlite-backup-and-rollback.md) for an authorized recovery. MQTT projections do not replace central outbox delivery or authorize a second acquisition owner.
- Public `/health/live` is process liveness. `/health` and `/health/ready` return the real snapshot and HTTP 503 only for Critical; a noncritical degraded result can be HTTP 200. Do not equate liveness/200 with device connectivity, all backlog delivered or operational readiness.
- Protected `/diagnostics/health`, `/diagnostics/outbox`, `/diagnostics/status` and PUT `/diagnostics/outbox/settings` require the configured diagnostics credential. [Authorization filter](../src/HVO.Edge.Hosting/Diagnostics/EdgeDiagnosticsAuthorizationFilter.cs) returns 403 for missing or incorrect credentials. Devices are included in status; no `/diagnostics/devices` endpoint exists. Runtime outbox overrides reset on restart.
- Current Compose probes are target-specific: Davis/EG4 use `/health`; JK/SmartShunt use `/health/live`. The disabled exporter template uses `/health`. Ports/paths/restart/logging/core limits and rollout preflight follow [operations](GATEWAY_OPERATIONS.md), not a blanket connectivity probe requirement.

Propagate CancellationToken and bound MQTT/HTTP/TCP/BLE polling, retry and shutdown work; a device failure should not crash all acquisition. Inspect each sender's actual retry/acknowledgement behavior, including 401/403 differences, in the [sender/recovery contract](gateways/common-gateway-standards.md#sender-http-outcome-and-recovery-matrix). Do not invent maintenance subcommands or Azure-forwarding env requirements.

### JK settings-password command

The current Davis, EG4 and SmartShunt MQTT projections are read-only telemetry. JK's telemetry projection additionally exposes a [Home Assistant button](../src/HVO.Hardware.JkBms/HomeAssistant/JkBmsHomeAssistantProjection.cs) for `change_settings_password` when an enabled device has `SettingsPasswordSecret`. [Startup initialization](../src/HVO.Hardware.JkBms/Hosting/JkBmsServiceCollectionExtensions.cs) resolves that secret file and requires exactly six ASCII digits; the [worker](../src/HVO.Hardware.JkBms/Workers/BmsPollerWorker.cs) registers the command only for those configured devices.

The [shared MQTT router](../src/HVO.Edge.HomeAssistant.Mqtt/HomeAssistantMqttCommandRouter.cs) accepts only an exact registered topic with non-retained `PRESS`; the password comes from the configured secret, not the MQTT payload. The [JK device session](../src/HVO.Hardware.JkBms/Workers/JkBmsDevice.cs) rejects offline, busy and already verified requests. It queues the bounded password-change operation; the [client](../src/HVO.Hardware.JkBms/Protocol/JkBmsClient.cs) performs the BLE write and requires a positive acknowledgement, then the session reads DeviceInfo and reports `succeeded_verified` only when `SetupPasscode` matches the configured password. Initialization also recognizes an already matching password without issuing another write.

Review this existing credential-write capability and its gating/failure behavior when changing JK configuration, MQTT or device sessions. It preserves the sole acquisition/writer boundary; it does not provide arbitrary BMS control or establish explicit settings-query support. Source and simulated tests establish this implemented path, not live hardware qualification or authorization to press the button, change secrets or perform device writes during ordinary issue validation.

## Security, secrets and transport

Azure Key Vault remains the credential authority. Use [global materialization prerequisites](development/key-vault-materialization.md) for root bootstrap, sourced SSH input, parsable SQL and whole-helper vault scope. The private gist is a devcontainer recovery cache, not the credential authority. Secret files/approved secret stores are valid locations; obsolete ".env only" rules do not describe current mounts.

The existing helper's SQL derivation/partial-write/exit-code hazard remains tracked separately in [#437](https://github.com/HualapaiValley/HVO.WebSite/issues/437). Neither this guidance nor a successful drift check proves materialization, authentication, rotation or deployment. Apply, secret rotation and operational restarts require applicable authorization. Preserve owner files and never print values through Compose output, shell tracing, logs or verification.

Review ignored local env/config/secret paths and narrowly scoped identities, reject sensitive logging/raw exception detail, and test trust-boundary validation. The website uses Entra OIDC/cookies for UI roles and X-Api-Key/scope policies for protected APIs. [ApiKeyAuthMiddleware](../src/HVO.WebSite.v9/Middleware/ApiKeyAuthMiddleware.cs) returns 401 for missing keys on protected /api endpoints and invalid/inactive/expired presented keys; policy authorization is distinct. Read route metadata/policies and [ingest trust boundaries](development/ingest-trust-boundaries.md), not a blanket assertion that every endpoint is authenticated.

[Website Program.cs](../src/HVO.WebSite.v9/Program.cs) maps public health probes: liveness checks no dependencies; readiness selects database-tagged checks; aggregate health exposes minimal production output unless detailed health is explicitly enabled. Preserve those information boundaries. Its role/scope policies are not roof/hardware tag requirements.

Transport must match the reviewed deployment: website forwarding/HTTPS settings and trusted proxy configuration, Pi internal upstream/HA HTTP opt-in, TLS trust, URL composition and safe non-secret assertions follow the canonical [website](../deploy/hvo-docker/README.md) / [gateway](GATEWAY_OPERATIONS.md) runbooks. A historical Azure-hosted deployment does not establish the current host or authorize certificate bypass.

## C#, data and durability checks

- Keep endpoints/components → services → domain → data/infrastructure ownership; keep business rules out of rendering/controllers/config helpers. Use strong types and justified nullable suppressions.
- Use appropriate DI lifetimes and correct IDisposable/IAsyncDisposable ownership. Avoid sync-over-async (`.Result`, `.Wait()`, `GetAwaiter().GetResult()`) in request/I/O paths and unobserved tasks; async void belongs only to required event-handler signatures.
- Pass cancellation through EF/HTTP/device I/O and workers; bound retries/results/history/pagination. Broad catches must retain meaningful failure behavior and telemetry, rather than silently swallow errors.
- Use EF async APIs (ToListAsync/FirstOrDefaultAsync/SaveChangesAsync), scoped HvoV9DbContext from DI, DTO projection and pagination. Do not use AsEnumerable before filtering, SaveChanges loops without explicit transaction reasoning, or relational InMemory tests as provider proof.
- Every schema change needs the matching EF migration and compatibility/rollback evidence. Current EF models/context/migrations are schema authority; archived SQL is historical. Verify race/deduplication/partial-write/transaction behavior against actual SQL fixtures when affected.
- Build the active solution at zero warnings/errors. Do not hide legitimate warnings with suppressions, add unrelated packages/frameworks or change public/config contracts without a focused explanation and compatibility evidence.

## Validation and meaningful browser evidence

[Repository profile](development/repository-profile.md) keeps the required exact-SDK full local checks, even when post-review CI selects affected work:

```text
dotnet restore HVO.WebSite.sln --locked-mode --nologo
dotnet build HVO.WebSite.sln --no-restore --nologo
dotnet test HVO.WebSite.sln --no-build --no-restore --filter "TestCategory!=Integration&TestCategory!=Live"
```

Use [testing](development/testing.md) for report options, Chromium/system dependencies and owned simulator/HA/SQL/browser commands. The full local non-live filter includes non-integration browser cases. Do not run an unprovisioned `TestCategory!=Live` solution command as if it supplied SQL/HA fixtures; Live physical/deployed targets remain explicit opt-in. MSTest class/method categories and the validator establish partition ownership; counts come from fresh actual TRX.

Add tests for changed behavior, failures and boundaries; documentation-only work does not need invented tests. Preserve deterministic/fake-time behavior rather than Thread.Sleep or longer real waits. HvoChartDataset tests retain nullable gap entries, Tension and Data_AllowsNullEntries_RepresentingGaps coverage.

Website/ThemeSandbox browser tests use owned local application hosts and deterministic services; no deployed gateway is needed. For affected UI, exercise actual rendered behavior in both themes and relevant responsive layouts:

- Circuit stays alive and no unexpected console/page errors occur; retain a distinct screenshot, console log and trace for each Browser case.
- Charts have real Chart.js instances/datasets/configuration and expected data/gaps/theme changes, not merely a canvas with width. Preserve `stripNulls()` for optional config while preserving data-array null gaps and guarded HvoChart render/refresh; verify recovery after script/handler failures.
- Assert meaningful control interactions and computed styles: datetime-local/control theming, card surface, contrast/sizing, scoped stylesheet/instrument layout, navigation/auth challenge and current reference routes. No legacy proto-/action-btn/card-shell/archive-table classes.
- Confirm expected local asset requests/Chart.js readiness. A canvas or loaded stylesheet alone cannot prove chart/theme/control behavior. Existing BrowserFailureQualificationTests demonstrate assertion failure and recovery.

Run relevant policy/planner/transition/runner/syntax checks for workflow/helper changes. Draft automated checks stay bounded preflight; distinct current-source independent review, verified finding dispositions and resolved actionable threads precede ready and standard CI. Required checks/green matching source and assignment authority precede merge. All actual contributors, check performers, unavailable results, source and runtime facts remain attributed per [automation evidence](development/automation-evidence.md). No new gate or controller behavior is defined here.

## Deployment and documentation boundaries

Update affected Compose/env examples/mounted-config schema and docs when ports, environment, volumes, public contracts or integration behavior change. Use [publishing](CONTAINER_PUBLISHING.md), [Pi deployment](../deploy/pi-gateways/README.md), [gateway operations](GATEWAY_OPERATIONS.md) and [website deployment](../deploy/hvo-docker/README.md) for their exact prerequisites/check-versus-apply/rollout semantics. Safe local render/dry-run checks do not prove live deployment; do not execute publish/restart/hardware validation as ordinary issue tests. Preserve exactly one acquisition authority and writer.

Document meaningful setup/config/API/MQTT/runtime changes and sandbox demos for shared tokens/components; avoid documentation for obvious code. Add a curated [project history](PROJECT_HISTORY.md) entry for structural/architectural/deployment assumptions. Preserve source/date/issue evidence and open follow-ups; future hardware/roadmap and historical recovery documents do not become active contracts.

Stabilize safety/security/circuit/build failures first, then missing auth/failure handling/theme correctness, meaningful regressions, telemetry and bounded duplication/dependency improvements. Severity and merge implications follow the canonical [review procedure](../.agents/skills/hvo-code-review/references/review-format.md); priorities here do not authorize finding deferral.

## Development tools

Use rg for searches; bash/zsh, gh, dotnet and az for applicable authorized work; jq/System.Text.Json/Python for focused JSON checks. Use repository tooling for browser/test setup and record actual prerequisites. Azure-resource commands or deployment contexts are tools, not automatic authority for remote changes.

Do not use terminal heredocs. Write multiline issue/PR/comment bodies with a file tool and pass the exact file through `--body-file`, or use a structured connector argument. Preserve literal content/newlines and never expose secrets through shell interpolation.
