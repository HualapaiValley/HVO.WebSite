# Test Coverage Gap Analysis

**Date:** 2026-06-17
**Scope:** Davis, JkBms, SmartShunt, SolarAssistant, TP-Link Kasa, shared Themes RCL, and main v9 site.
**Execution time:** Research performed with static repository inspection plus recent local verification from PR #245.

## 1. Executive Summary

The repository has strong MSTest coverage for protocol parsers, outbox libraries, gateway mappers, TP-Link device services, and v9 API ingest paths. The largest remaining production risk is browser-level and component-level coverage: every deployed gateway except JkBms has only thin Playwright smoke coverage, JkBms has no Playwright coverage at all, and gateway Blazor components have no true bUnit rendering coverage. Shared theme components used by every gateway are also missing bUnit tests.

The recommended Phase 2 plan is a single integration branch, `feature/test-coverage-gap-analysis`, with parallel-safe batches. Each batch owns a unique set of files to avoid merge conflicts.

## 2. Gap Count by Severity

| Severity | MSTest gaps | bUnit gaps | Playwright gaps |
|----------|-------------|------------|-----------------|
| P0 | 0 | 0 | 1 |
| P1 | 4 | 7 | 5 |
| P2 | 5 | 4 | 3 |
| P3 | 1 | 1 | 1 |

## 3. Batch Assignments (for Phase 2 implementation)

| Batch | Issue title | Type | Scope | Files touched | Severity |
|-------|-------------|------|-------|---------------|----------|
| 1 | `test(playwright/jkbms): add gateway browser coverage` | Playwright | JkBms gateway | `tests/HVO.WebSite.PlaywrightTests/JkBmsGatewayPlaywrightTests.cs` | P0 |
| 2 | `test(playwright/gateways): extend Davis and shared browser assertions` | Playwright | Davis + shared helper | `PlaywrightGatewayAssertions.cs`, `DavisGatewayPlaywrightTests.cs`, `DavisGatewayLayoutPlaywrightTests.cs` | P1 |
| 3 | `test(playwright/gateways): extend SmartShunt and SolarAssistant coverage` | Playwright | SmartShunt + SolarAssistant | `SmartShuntGatewayPlaywrightTests.cs`, `SolarAssistantGatewayPlaywrightTests.cs` | P1 |
| 4 | `test(playwright/kasa-v9-theme): extend Kasa, v9, and ThemeSandbox coverage` | Playwright | TP-Link Kasa, v9, ThemeSandbox | `TplinkKasaGatewayPlaywrightTests.cs`, `MainSitePlaywrightTests.cs`, `ThemeSandboxPlaywrightTests.cs` | P1 |
| 5 | `test(bunit/themes): add shared theme component coverage` | bUnit | Themes RCL | New files under `tests/HVO.WebSite.UnitTests/Components/Themes/` | P1 |
| 6 | `test(bunit/davis): add Davis component coverage` | bUnit | Davis gateway components | New files under `tests/HVO.Hardware.DavisVantagePro2.Tests/Components/` | P1 |
| 7 | `test(bunit/jkbms-smartshunt): add battery gateway component coverage` | bUnit | JkBms + SmartShunt components | New files under their gateway test projects | P1 |
| 8 | `test(bunit/solar-kasa): add SolarAssistant and Kasa component coverage` | bUnit | SolarAssistant + TP-Link Kasa components | New files under their gateway test projects | P1 |
| 9 | `test(mstest/workers): add gateway worker orchestration coverage` | MSTest | Davis, JkBms, SmartShunt workers | New worker tests under gateway test projects | P1 |
| 10 | `test(mstest/solarassistant): add MQTT and REST client coverage` | MSTest | SolarAssistant MQTT/REST | New SolarAssistant tests | P1 |
| 11 | `test(mstest/api-diagnostics): add gateway API and v9 exception coverage` | MSTest | Davis, SmartShunt, SolarAssistant API diagnostics; v9 handler | New/updated hosting/unit tests | P2 |
| 12 | `test(mstest/format-health): add formatting culture and health-check coverage` | MSTest | Shared formatting and simple health checks | Existing/new small unit tests | P2/P3 |

## 4. Playwright Gaps

### 4.1 JkBms — NO PLAYWRIGHT TESTS (P0)

No `JkBmsGatewayPlaywrightTests.cs` exists. This leaves the deployed JkBms Blazor gateway without browser-level circuit, routing, resource-loading, theme, or chart validation.

Create `tests/HVO.WebSite.PlaywrightTests/JkBmsGatewayPlaywrightTests.cs` with class `JkBmsGatewayPlaywrightTests` and environment variable `HVO_JKBMS_BASE_URL`.

Required methods:
- `JkBmsGateway_ShouldRenderLayoutShellAndKeepCircuitAlive`
- `JkBmsGateway_ShouldNavigateOverviewBanksAndKeepCircuitAlive`
- `JkBmsGateway_ShouldRenderThemedCardsAndControls`
- `JkBmsGateway_ShouldLoadOnlyLocalOfflineResources`
- `JkBmsGateway_ShouldRenderDeviceDetailCellChart_WhenConfiguredDeviceExists`

### 4.2 Davis Playwright gaps (P1)

Existing Davis tests cover only thin layout smoke behavior. Missing chart/circuit/resource/navigation coverage.

Modify:
- `tests/HVO.WebSite.PlaywrightTests/DavisGatewayPlaywrightTests.cs`
- `tests/HVO.WebSite.PlaywrightTests/DavisGatewayLayoutPlaywrightTests.cs`

Add methods:
- `DavisGateway_ShouldRenderStatusChartsAndKeepCircuitAlive`
- `DavisGateway_ShouldLoadOnlyLocalOfflineResources`
- `DavisGateway_ShouldKeepCircuitAliveAcrossTopNavigation`
- `DavisGateway_ShouldOpenStationInfoDialogAndToggleTheme`

### 4.3 SmartShunt Playwright gaps (P1)

Existing SmartShunt browser coverage checks shell and telemetry chart basics. Missing offline resource, theme, legacy-class, and navigation/theme-toggle checks.

Modify `tests/HVO.WebSite.PlaywrightTests/SmartShuntGatewayPlaywrightTests.cs`.

Add methods:
- `SmartShuntGateway_ShouldLoadOnlyLocalOfflineResources`
- `SmartShuntGateway_ShouldRenderThemedOverviewCards`
- `SmartShuntGateway_ShouldKeepCircuitAliveAcrossNavigationAndThemeToggle`
- `SmartShuntGateway_ShouldHaveNoLegacyClasses`

### 4.4 SolarAssistant Playwright gaps (P1)

Existing SolarAssistant browser coverage checks only the monitor shell. Missing inventory route, chart/empty-state, offline resource, theme, and legacy-class checks.

Modify `tests/HVO.WebSite.PlaywrightTests/SolarAssistantGatewayPlaywrightTests.cs`.

Add methods:
- `SolarAssistantGateway_ShouldNavigateInventoryRoutesAndKeepCircuitAlive`
- `SolarAssistantGateway_ShouldRenderHistoryCharts_WhenHistoryExists`
- `SolarAssistantGateway_ShouldLoadOnlyLocalOfflineResources`
- `SolarAssistantGateway_ShouldRenderThemedCardsAndNoLegacyClasses`
- `SolarAssistantGateway_ShouldToggleThemeWithoutCircuitError`

### 4.5 TP-Link Kasa Playwright gaps (P1)

Existing Kasa coverage checks only layout/nav. Missing settings route, theme/resource, dialog, legacy-class, and device card coverage.

Modify `tests/HVO.WebSite.PlaywrightTests/TplinkKasaGatewayPlaywrightTests.cs`.

Add methods:
- `TplinkKasaGateway_ShouldNavigateSettingsAndKeepCircuitAlive`
- `TplinkKasaGateway_ShouldLoadOnlyLocalOfflineResources`
- `TplinkKasaGateway_ShouldRenderThemedDashboardAndSettingsControls`
- `TplinkKasaGateway_ShouldOpenGatewayInfoDialogAndCloseIt`
- `TplinkKasaGateway_ShouldHaveNoLegacyClasses`

### 4.6 v9 and ThemeSandbox Playwright gaps (P2)

Main site `HomePagePlaywrightTests.HomePage_ShouldRenderMainHeading` is ignored. ThemeSandbox coverage is broader, but HvoChart coverage should be updated for current dense/sparse chart ids.

Create `tests/HVO.WebSite.PlaywrightTests/MainSitePlaywrightTests.cs` with:
- `MainSite_ShouldRenderPublicHomeShell`
- `MainSite_ShouldShowUnauthenticatedPowerCardMessage`
- `MainSite_AdminRoute_ShouldRedirectUnauthenticatedUserToLogin`
- `MainSite_ShouldToggleThemeWithoutCircuitError`
- `MainSite_ShouldRenderSharedThemeCss`

Modify `tests/HVO.WebSite.PlaywrightTests/ThemeSandboxPlaywrightTests.cs` with:
- `ThemeSandbox_HvoChart_RendersDenseAndSparseCharts`
- `ThemeSandbox_ShouldKeepCircuitAliveAcrossAllReferenceRoutes`
- `ThemeSandbox_ShouldLoadOnlyLocalChartAndThemeResources`
- `ThemeSandbox_CssReference_ShouldValidateControlsAndCardsStyling`
- `ThemeSandbox_Palette_ShouldRenderCanonicalTokens`

## 5. bUnit Gaps

### 5.1 Shared Themes components (P1)

Shared components used by all gateways have no component rendering tests.

Create under `tests/HVO.WebSite.UnitTests/Components/Themes/`:
- `HvoGatewayLayoutTests.cs`
  - `RendersBrandSubtitleNavActionsFooterSlots`
  - `AppliesDarkAndLightShellThemeClass`
  - `RendersFooterIndicators`
- `HvoPublicLayoutTests.cs`
  - `RendersBrandNavAuthAndBody`
  - `HidesSubtitleWhenBlank`
  - `RendersBlazorErrorUi`
- `HvoAdminLayoutTests.cs`
  - `RendersSidebarActionsAndBody`
  - `DrawerToggleChangesDrawerState`
- `HvoChartTests.cs`
  - `InitialRenderInvokesJsWithExpectedConfig`
  - `DataRevisionChangeRerendersChart`
  - `JsExceptionIsSwallowed`

### 5.2 Davis bUnit gaps (P1/P2)

Create under `tests/HVO.Hardware.DavisVantagePro2.Tests/Components/`:
- `StatusPageBunitTests.cs`
  - `RendersLiveStatusCards`
  - `RendersChartsWithStableIds`
  - `RendersSensorFallbacksWhenDataMissing`
- `ArchivePageBunitTests.cs`
  - `RendersArchiveTable`
  - `RendersEmptyArchiveState`
- `DavisSettingsPagesBunitTests.cs`
  - `RendersConsoleSettings`
  - `RendersStationMetadata`
- `DavisStaticPagesBunitTests.cs`
  - `RendersAlarmActive`
  - `RendersAlarmDefinitions`
  - `RendersStationDiagnostics`

### 5.3 JkBms bUnit gaps (P1)

Create under `tests/HVO.Hardware.JkBms.Tests/Components/`:
- `JkBmsStatusPageBunitTests.cs`
  - `RendersGatewaySummaryAndBankStates`
  - `RendersEmptyDeviceState`
- `DevicesPageBunitTests.cs`
  - `RendersConfiguredDeviceRows`
  - `UsesDefaultPollIntervalWhenDeviceIntervalMissing`
  - `RendersPollerErrors`
- `DeviceDetailPageBunitTests.cs`
  - `RendersSelectedBankTelemetry`
  - `RendersNotFoundWhenAddressUnknown`
  - `RendersCellChartContainerWhenCellVoltagesExist`

### 5.4 SmartShunt bUnit gaps (P1)

Create under `tests/HVO.Hardware.VictronSmartShunt.Tests/Components/`:
- `SmartShuntStatusPageBunitTests.cs`
  - `RendersCurrentSnapshotAndHealth`
  - `RendersFallbacksWhenSnapshotMissing`
- `TelemetryPageBunitTests.cs`
  - `RendersPrivateMetadataOverlay`
  - `RendersTelemetryChart`
  - `RendersMissingPrivateValuesAsFallback`

### 5.5 SolarAssistant bUnit gaps (P1/P2)

Create under `tests/HVO.Gateway.SolarAssistant.Tests/Components/`:
- `SolarAssistantStatusBunitTests.cs`
  - `RendersSnapshotHealthOutboxAndInventorySections`
  - `RendersAlertsWhenHealthIsWarning`
  - `RendersEmptyHistoryState`
- `PowerHistoryChartBunitTests.cs`
  - `RendersEmptyStateWithoutData`
  - `RendersHvoChartWhenDataPresent`
  - `FormatsRangeSummary`
- `SolarAssistantPrimitiveComponentsBunitTests.cs`
  - `CardHead_RendersOptionalLink`
  - `DetailRow_RendersLabelAndValue`
  - `MetricTile_RendersLabelValueAndDelta`
- `MqttInventoryPageBunitTests.cs`
  - `RendersInventoryJsonOrTableState`
- `RestInventoryPageBunitTests.cs`
  - `RendersRestMetricInventory`

### 5.6 TP-Link Kasa bUnit gaps (P1)

Create under `tests/HVO.Gateway.TplinkKasa.Tests/Components/`:
- `KasaDashboardBunitTests.cs`
  - `RendersLoadingState`
  - `RendersEmptyConfiguredDevicesState`
  - `GroupsDevicesIntoAllFavoritesAndGroupTabs`
  - `RendersDeviceDetailsDialog`
- `KasaSettingsBunitTests.cs`
  - `RendersConfiguredDevicesAndScanControls`
  - `RendersAddDeviceForm`
- `KasaDeviceCardsBunitTests.cs`
  - `KasaDeviceCard_RendersBaseDeviceIdentityAndStatus`
  - `KasaOutletDeviceCard_RendersOutletStateAndEnergy`
  - `KasaLightDeviceCard_RendersLightControlsAndUsesLightCommandMode`
  - `KasaDimmerDeviceCard_RendersBrightnessControls`
  - `KasaSwitchDeviceCard_RendersSwitchState`
- `KasaOutletRowBunitTests.cs`
  - `RendersOutletNameEnergyAndState`
  - `DisablesToggleWhenOfflineOrMissingCapability`
  - `LightModeCallsSetLightAsync`
  - `PowerModeCallsSetPowerAsync`

### 5.7 v9 Main Site bUnit gaps (P2)

Add or extend under `tests/HVO.WebSite.UnitTests/Components/`:
- `PowerStatusCardTests.cs`
  - `RendersNoSnapshotFallback`
  - `RendersStaleGatewayState`
  - `RendersMissingInventoryFallback`
- `LayoutProviderBunitTests.cs`
  - `SelectsPublicLayoutByDefault`
  - `SelectsAdminLayoutForAdminPath`
- `HomePageBunitTests.cs`
  - `RendersPowerStatusCardAndHero`
- `RedirectToLoginBunitTests.cs`
  - `NavigatesToSignInWithReturnUrl`

## 6. MSTest Gaps

### 6.1 P1 worker orchestration gaps

Create worker tests:
- `tests/HVO.Hardware.DavisVantagePro2.Tests/Workers/WeatherStationWorkerTests.cs`
  - `GetArchiveCatchupStatusAsync_ReturnsCurrentState`
  - `RunArchiveTopOffAsync_UsesConsoleOffsetAndArchiveInterval`
  - `StartupArchiveCatchup_HonorsDisabledEnabledForceModes`
  - `Loop2Poll_WritesMappedOutboxPayload`
  - `RecoverableError_UpdatesLastErrorWithoutStoppingWorker`
- `tests/HVO.Hardware.JkBms.Tests/Workers/BmsPollerWorkerTests.cs`
  - `Constructor_FiltersDisabledAndInvalidDevices`
  - `SuccessfulPoll_MapsCellInfoPacketToReading`
  - `ConfigSnapshot_EnqueuedOnlyWhenHashChanges`
  - `DeviceInfoSnapshot_EnqueuedOnlyWhenHashChanges`
  - `AlarmHandler_CalledOnlyWhenPacketHasAlarms`
- `tests/HVO.Hardware.VictronSmartShunt.Tests/Workers/SmartShuntWorkerTests.cs`
  - `PollOnceAsync_ReturnsFalseWhenNoCurrentSample`
  - `PollOnceAsync_NormalizesDefaultTimestampToUtc`
  - `PollOnceAsync_AppliesFreshPrivateOverlay`
  - `PollOnceAsync_WritesOutboxOnSnapshotCadence`
  - `PollOnceAsync_TrimsHistoryToCapacity`

### 6.2 P1 SolarAssistant MQTT gaps

Create:
- `tests/HVO.Gateway.SolarAssistant.Tests/SolarAssistant/SolarAssistantMqttClientTests.cs`
  - `RunAsync_ValidConnackSuback_InvokesOnSubscribed`
  - `RunAsync_RejectsNonZeroConnack`
  - `RunAsync_RejectsInvalidSubackPacketIdOrReturnCode`
  - `RunAsync_ParsesQos0AndQos1PublishPayloads`
  - `RunAsync_KeepaliveTimeoutRaisesFailure`
- `tests/HVO.Gateway.SolarAssistant.Tests/SolarAssistant/SolarAssistantMqttDiscoveryWorkerTests.cs`
  - `ExecuteAsync_DisabledWhenHostMissing`
  - `ExecuteAsync_DisabledWhenMqttDiscoveryDisabled`
  - `ExecuteAsync_AppliesIncomingMessagesToInventoryStore`

### 6.3 P2 gateway API diagnostics and v9 exception gaps

Create:
- `tests/HVO.Hardware.DavisVantagePro2.Tests/Hosting/DavisGatewayApiTests.cs`
  - `DiagnosticsEndpoints_RejectMissingApiKey`
  - `WeatherCurrent_ReturnsNoContentWhenNoLatestReading`
  - `WeatherCurrent_ReturnsCurrentConditionsWhenReadingExists`
  - `DiagnosticsStatus_DoesNotExposeApiKey`
- `tests/HVO.Hardware.VictronSmartShunt.Tests/Hosting/SmartShuntGatewayApiTests.cs`
  - `DiagnosticsEndpoints_RejectMissingOrInvalidApiKey`
  - `DiagnosticsStatus_ReturnsStandardShapeWithValidKey`
  - `DiagnosticsStatus_DoesNotExposeApiKey`
- `tests/HVO.Gateway.SolarAssistant.Tests/Hosting/SolarAssistantGatewayApiTests.cs`
  - `DiagnosticsEndpoints_RejectMissingOrInvalidApiKey`
  - `DiagnosticsStatus_ReturnsStandardShapeWithValidKey`
  - `DiagnosticsStatus_DoesNotExposeApiKey`
- `tests/HVO.WebSite.UnitTests/HvoServiceExceptionHandlerTests.cs`
  - `TryHandleAsync_ArgumentException_ReturnsBadRequest`
  - `TryHandleAsync_GenericException_ReturnsInternalServerError`
  - `TryHandleAsync_Production_DoesNotExposeExceptionDetails`
  - `TryHandleAsync_Development_IncludesExceptionDetails`

### 6.4 P2/P3 formatting and health-check gaps

Modify/create:
- `tests/HVO.WebSite.UnitTests/HvoFormatTests.cs`
  - `NumericFormatting_IsStableUnderCommaDecimalCulture`
- `tests/HVO.Hardware.DavisVantagePro2.Tests/Telemetry/VantageStationHealthCheckTests.cs`
  - `CheckHealthAsync_ReturnsHealthyWhenStationConnected`
  - `CheckHealthAsync_ReturnsUnhealthyWhenStationDisconnected`
- `tests/HVO.Hardware.JkBms.Tests/Telemetry/BmsDeviceHealthCheckTests.cs`
  - `CheckHealthAsync_ReturnsHealthyWhenAllDevicesConnected`
  - `CheckHealthAsync_ReturnsDegradedWhenSomeDevicesDisconnected`
  - `CheckHealthAsync_ReturnsUnhealthyWhenNoneConnected`
- `tests/HVO.Gateway.TplinkKasa.Tests/Hosting/KasaGatewayHealthCheckTests.cs`
  - `CheckHealthAsync_ReturnsHealthyWhenAllDevicesOnline`
  - `CheckHealthAsync_ReturnsDegradedWhenSomeDevicesOffline`
  - `CheckHealthAsync_ReturnsUnhealthyWhenNoDevicesOnline`

## 7. Batch Implementation Plan

1. Create `feature/test-coverage-gap-analysis` from `main`.
2. Each Phase 2 agent creates its assigned `feature/test-coverage-gap-analysis-batch-<N>` branch.
3. Each batch only touches files listed in its issue.
4. Each batch verifies `dotnet build` and the relevant test project(s).
5. Batches merge into the integration branch, not `main`.
6. Phase 3 validates the integrated branch and opens one PR to `main`.

## 8. Estimated Effort per Batch

| Batch | Estimated effort | Notes |
|-------|------------------|-------|
| 1 | Medium | First JkBms Playwright setup and chart/detail handling. |
| 2 | Medium | Adds shared Playwright helper plus Davis coverage. |
| 3 | Medium | Two gateways, mostly route/resource/theme tests. |
| 4 | Medium | Kasa, v9, and ThemeSandbox assertions. |
| 5 | Medium | bUnit JS interop mocks for HvoChart. |
| 6 | Medium | Davis page/service injection setup. |
| 7 | Medium | Two battery gateway component suites. |
| 8 | Medium | SolarAssistant and Kasa component suites. |
| 9 | Large | Worker orchestration tests may require fakes or small extraction. |
| 10 | Medium | MQTT tests may need a loopback broker or protocol helper extraction. |
| 11 | Medium | WebApplicationFactory/endpoint test setup across projects. |
| 12 | Small | Focused culture and health-check tests. |
