# Documentation index

Current direct collectors are Davis, JK BMS, EG4 and SmartShunt. Home Assistant
owns Kasa/Govee acquisition and presentation; the HA exporter is implemented but
disabled, unmapped and without production source claims. Use current owners below;
archives preserve their original claims without granting current authority.

## Development

- [Contributor workflow](../CONTRIBUTING.md), [agent routing](../AGENTS.md), [repository profile](development/repository-profile.md) and [process](development/PROCESS.md)
- [PR machine evidence](development/automation-evidence.md), [independent review](../.agents/skills/hvo-code-review/SKILL.md), [shared review preparation](../.agents/skills/hvo-code-review/references/review-preparation.md) and [correction preparation](../.agents/skills/hvo-pr-lifecycle/references/correction-preparation.md)
- [OpenCode adapter](../.opencode/skills/code-review/SKILL.md), [project engineering guidance](AGENT_PROJECT_GUIDANCE.md) and [CSS governance](CSS_GOVERNANCE.md)
- [Development container](../.devcontainer/devcontainer.json) and [dated OpenCode recovery context/current script owners](../.devcontainer/opencode-web-recovery.md)
- [Navigation guard and archive link policy](development/documentation-navigation.md), [selective CI](development/selective-ci.md)

## Architecture

- [Current ownership and data flows](ARCHITECTURE.md)
- [Headless runtime](architecture/EDGE_VNEXT_RUNTIME.md), [detailed edge data flows](architecture/EDGE_DATA_FLOWS.md) and [gateway standards](gateways/common-gateway-standards.md)
- [EF schema authority](../src/HVO.DataModels/README.md), [weather query boundary](development/canonical-weather-queries.md), [retry durability](development/canonical-ingest-retries.md), [power observation identity](development/power-observation-identity.md), [UTC power windows](development/power-history-utc.md) and [ingest trust boundaries](development/ingest-trust-boundaries.md)
- [Camera/media model](gateways/camera-media-model.md)

## Testing

- [All test assemblies and fixture host](../tests/README.md)
- [Commands/categories/prerequisites and CI ownership](development/testing.md)
- [Browser fixtures and artifacts](../tests/HVO.WebSite.PlaywrightTests/README.md)
- [Disposable SQL Server](development/sql-server-integration-tests.md) and [Home Assistant environment](../tests/HomeAssistant.IntegrationEnvironment/README.md)

## Operations

- [Current website deployment](../deploy/hvo-docker/README.md), [container publishing](CONTAINER_PUBLISHING.md), [data-protection recovery](WEBSITE_DATA_PROTECTION.md), [shared infrastructure](SHARED_INFRASTRUCTURE.md) and [CI runners](CI_RUNNERS.md)
- [Gateway operations](GATEWAY_OPERATIONS.md), [Pi commissioning](../deploy/pi-gateways/README.md), [SQLite backup/rollback](gateways/sqlite-backup-and-rollback.md) and [Key Vault materialization prerequisites/hazard](development/key-vault-materialization.md)
- [Gateway manuals](gateways/README.md): [Davis](gateways/davis-vantage-pro2/README.md), [JK](gateways/jk-bms.md), [EG4](gateways/eg4/deployment-and-shadow-validation.md), [SmartShunt](gateways/victron-smartshunt.md)
- [Davis console fields/settings](gateways/davis-vantage-pro2/console-fields-and-settings.md), [archive continuity/rollback](gateways/davis-vantage-pro2/cutover-and-rollback.md), [Weather Underground](gateways/davis-vantage-pro2/weather-underground-deployment.md), [CWOP](gateways/davis-vantage-pro2/cwop-deployment.md) and [JK endurance](gateways/jkbms/deployment-and-endurance.md)
- [Home Assistant](../deploy/home-assistant/README.md), [guarded HA exporter](../deploy/pi-gateways/home-assistant-exporter/README.md)

## Future decisions

- [Short issue-owned roadmap and untracked questions](FUTURE_WORK.md)
- [Unselected camera/NVR/PDU/roof/motion research and current Govee boundary](gateways/future-integrations.md)
- [Optional gateway manual structure](gateways/templates/gateway-manual-template.md)

## Historical evidence

- [Curated project decisions](PROJECT_HISTORY.md), [one repository changelog](../CHANGELOG.md)
- [All 84 baseline dispositions and later document inventory](archive/2026-10-04-documentation-dispositions.md)
- [Complete previous project sessions](archive/2026-10-04-project-history-source.md), [June model experiments](archive/2026-06-model-review-experiments.md), [old deployment changelog](archive/undated-deployment-changelog.md), [prior README](archive/undated-root-readme.md)
- [Prior architecture](archive/2026-08-18-architecture-baseline.md), [prior roadmap](archive/2026-08-18-roadmap.md)
- [Davis full inventory](archive/2026-05-08-davis-console-inventory.md), [SmartShunt plan/private research](archive/2026-05-25-smartshunt-plan.md), [JK lifecycle plan](archive/jkbms-session-lifecycle.md)
- [Former ACA deployment and key migration](archive/website-container-app.md), [fixed campaign instructions](archive/2026-review-campaign-process.md)
- [Davis manufacturer PDF](VantageSerialProtocolDocs_v261.pdf), [original SmartShunt screenshots](screenshots/), [6500EX raw captures](../tests/HVO.Hardware.Eg4.Tests/Fixtures/6500ex/) and [MPPT raw captures](../tests/HVO.Hardware.Eg4.Tests/Fixtures/mppt100-48hv/)

Six former placeholder paths are deliberately retained as thin compatibility
redirects because external bookmarks are unknown. They are not additional current
manuals. Govee redirects to current HA ownership first. Archive movement does not
exempt rendered relative links; dated observations and historical command examples
must remain clearly distinct from current operational guidance.

## Project documentation owners

Every active solution project has one useful indexed entry. Test projects use
specific sections of the central test guide rather than empty per-test READMEs.
The archived HVO.Database sqlproj is excluded; its directly consumed SQL files
remain preserved. Build configuration and dependencies live in each linked csproj.

| Project | Useful documentation owner | Role |
|---|---|---|
| [HVO.DataModels](../src/HVO.DataModels/HVO.DataModels.csproj) | [entry point](../src/HVO.DataModels/README.md) | Canonical SQL models and EF migration chains |
| [HVO.Edge.Contracts](../src/HVO.Edge.Contracts/HVO.Edge.Contracts.csproj) | [entry point](../src/HVO.Edge.Contracts/README.md) | Shared wire envelopes, typed weather/power payloads, health and diagnostics auth contracts |
| [HVO.Edge.Exporter.HomeAssistant](../src/HVO.Edge.Exporter.HomeAssistant/HVO.Edge.Exporter.HomeAssistant.csproj) | [entry point](../src/HVO.Edge.Exporter.HomeAssistant/README.md) | Coalesce approved native HA state/events into source-time canonical observations/outbox |
| [HVO.Edge.HomeAssistant.Mqtt](../src/HVO.Edge.HomeAssistant.Mqtt/HVO.Edge.HomeAssistant.Mqtt.csproj) | [entry point](../src/HVO.Edge.HomeAssistant.Mqtt/README.md) | HA MQTT Discovery/current-state projection, diagnostics and bounded command routing |
| [HVO.Edge.Hosting](../src/HVO.Edge.Hosting/HVO.Edge.Hosting.csproj) | [entry point](../src/HVO.Edge.Hosting/README.md) | Mounted config/secrets, shared startup, health/diagnostics and logging/telemetry |
| [HVO.Edge.Outbox](../src/HVO.Edge.Outbox/HVO.Edge.Outbox.csproj) | [entry point](../src/HVO.Edge.Outbox/README.md) | Durable SQLite enqueue, failure/retry forwarding and diagnostic state |
| [HVO.Hardware.DavisVantagePro2](../src/HVO.Hardware.DavisVantagePro2/HVO.Hardware.DavisVantagePro2.csproj) | [entry point](../src/HVO.Hardware.DavisVantagePro2/README.md) | Direct Davis LOOP/DMPAFT acquisition, durable live/archive outboxes and HA current-state presentation |
| [HVO.Hardware.Eg4](../src/HVO.Hardware.Eg4/HVO.Hardware.Eg4.csproj) | [entry point](../src/HVO.Hardware.Eg4/README.md) | Direct allowlisted 6500EX inquiries and point-to-point MPPT fixed-register telemetry |
| [HVO.Hardware.JkBms](../src/HVO.Hardware.JkBms/HVO.Hardware.JkBms.csproj) | [entry point](../src/HVO.Hardware.JkBms/README.md) | Per-device BLE BMS sessions, canonical summaries/children and HA projection |
| [HVO.Hardware.VictronSmartShunt](../src/HVO.Hardware.VictronSmartShunt/HVO.Hardware.VictronSmartShunt.csproj) | [entry point](../src/HVO.Hardware.VictronSmartShunt/README.md) | Paired direct public-GATT acquisition, atomic summary/detail and MQTT current state |
| [HVO.Staging](../src/HVO.Staging/HVO.Staging.csproj) | [entry point](../src/HVO.Staging/README.md) | Partial SDK compatibility source for astronomy and weather extension gaps |
| [HVO.ThemeSandbox](../src/HVO.ThemeSandbox/HVO.ThemeSandbox.csproj) | [entry point](../src/HVO.ThemeSandbox/README.md) | Interactive theme/component/layout showcase and owned browser fixture target |
| [HVO.WebSite.Themes](../src/HVO.WebSite.Themes/HVO.WebSite.Themes.csproj) | [entry point](../src/HVO.WebSite.Themes/README.md) | Shared Razor theme/layout, components, CSS tokens, fonts and HvoFormat authority |
| [HVO.WebSite.v9](../src/HVO.WebSite.v9/HVO.WebSite.v9.csproj) | [entry point](../src/HVO.WebSite.v9/README.md) | Central authenticated ingest/query persistence, public site and operations browser UI |
| [HVO.Edge.Contracts.Tests](../tests/HVO.Edge.Contracts.Tests/HVO.Edge.Contracts.Tests.csproj) | [entry point](../tests/README.md#hvoedgecontractstests) | Typed envelope/version/identity/diagnostics-auth/health contract regressions |
| [HVO.Edge.Exporter.HomeAssistant.Tests](../tests/HVO.Edge.Exporter.HomeAssistant.Tests/HVO.Edge.Exporter.HomeAssistant.Tests.csproj) | [entry point](../tests/README.md#hvoedgeexporterhomeassistanttests) | HA projection/coalescing/time/immutable replay/source rejection and provisioned HA exporter boundaries |
| [HVO.Edge.HomeAssistant.Mqtt.Tests](../tests/HVO.Edge.HomeAssistant.Mqtt.Tests/HVO.Edge.HomeAssistant.Mqtt.Tests.csproj) | [entry point](../tests/README.md#hvoedgehomeassistantmqtttests) | Discovery/identity/state/command/reconnect unit tests and provisioned MQTT/HA boundaries |
| [HVO.Edge.Hosting.TestHost](../tests/HVO.Edge.Hosting.TestHost/HVO.Edge.Hosting.TestHost.csproj) | [entry point](../tests/README.md#hvoedgehostingtesthost) | Disposable actual shared-runtime host for Hosting.Tests |
| [HVO.Edge.Hosting.Tests](../tests/HVO.Edge.Hosting.Tests/HVO.Edge.Hosting.Tests.csproj) | [entry point](../tests/README.md#hvoedgehostingtests) | Mounted config/secret/startup/endpoints/logging/telemetry and actual disposable TestHost process invariants |
| [HVO.Edge.Outbox.Tests](../tests/HVO.Edge.Outbox.Tests/HVO.Edge.Outbox.Tests.csproj) | [entry point](../tests/README.md#hvoedgeoutboxtests) | Actual SQLite enqueue/transaction/deduplication/sender/failure/recovery/health tests |
| [HVO.Hardware.DavisVantagePro2.Tests](../tests/HVO.Hardware.DavisVantagePro2.Tests/HVO.Hardware.DavisVantagePro2.Tests.csproj) | [entry point](../tests/README.md#hvohardwaredavisvantagepro2tests) | Driver/LOOP/archive/cursor/API/astronomy/external-publisher deterministic and simulator tests; explicit Live remains separate |
| [HVO.Hardware.Eg4.Tests](../tests/HVO.Hardware.Eg4.Tests/HVO.Hardware.Eg4.Tests.csproj) | [entry point](../tests/README.md#hvohardwareeg4tests) | Allowlist/CRC/framing/fixed Modbus mappings/units/signs/outbox/diagnostics and simulated fleet |
| [HVO.Hardware.JkBms.Tests](../tests/HVO.Hardware.JkBms.Tests/HVO.Hardware.JkBms.Tests.csproj) | [entry point](../tests/README.md#hvohardwarejkbmstests) | BMS protocol/frame/config/device-info/session/adapter/secret-backed password and migration/outbox boundaries |
| [HVO.Hardware.VictronSmartShunt.Tests](../tests/HVO.Hardware.VictronSmartShunt.Tests/HVO.Hardware.VictronSmartShunt.Tests.csproj) | [entry point](../tests/README.md#hvohardwarevictronsmartshunttests) | Public UUID/sentinel/aggregate/session/atomic summary-detail/outbox/migration/HA projection behavior |
| [HVO.Tools.HomeAssistantEntityMigration.Tests](../tests/HVO.Tools.HomeAssistantEntityMigration.Tests/HVO.Tools.HomeAssistantEntityMigration.Tests.csproj) | [entry point](../tests/README.md#hvotoolshomeassistantentitymigrationtests) | Registry/manifest supported API/reversal and energy eligibility/audit/preference recovery; provisioned HA boundary |
| [HVO.WebSite.ApiTests](../tests/HVO.WebSite.ApiTests/HVO.WebSite.ApiTests.csproj) | [entry point](../tests/README.md#hvowebsiteapitests) | HTTP/auth/source ownership, actual provisioned SQL migrations/conflicts/query translation and canonical persistence |
| [HVO.WebSite.PlaywrightTests](../tests/HVO.WebSite.PlaywrightTests/HVO.WebSite.PlaywrightTests.csproj) | [entry point](../tests/HVO.WebSite.PlaywrightTests/README.md) | Owned website/ThemeSandbox/HA wind-card browser fixtures and separately opt-in deployed HA checks |
| [HVO.WebSite.UnitTests](../tests/HVO.WebSite.UnitTests/HVO.WebSite.UnitTests.csproj) | [entry point](../tests/README.md#hvowebsiteunittests) | MSTest/bUnit services/components/format/astronomy/canonical persistence/query/chart presenter and direct archived SQL inputs |
| [HVO.Tools.Eg4SerialProbe](../tools/HVO.Tools.Eg4SerialProbe/HVO.Tools.Eg4SerialProbe.csproj) | [entry point](../tools/HVO.Tools.Eg4SerialProbe/README.md) | Bounded direct inquiry proof/commissioning utility |
| [HVO.Tools.HomeAssistantEntityMigration](../tools/HVO.Tools.HomeAssistantEntityMigration/HVO.Tools.HomeAssistantEntityMigration.csproj) | [entry point](../tools/HVO.Tools.HomeAssistantEntityMigration/README.md) | Supported HA registry ID migration, Energy preference check/audit/apply, and HA-agent backup requests |
| [HVO.Tools.JkBleConsole](../tools/HVO.Tools.JkBleConsole/HVO.Tools.JkBleConsole.csproj) | [entry point](../tools/HVO.Tools.JkBleConsole/README.md) | Standalone bounded live BLE session/contention diagnostic |
| [HVO.Tools.SmartShuntBleConsole](../tools/HVO.Tools.SmartShuntBleConsole/HVO.Tools.SmartShuntBleConsole.csproj) | [entry point](../tools/HVO.Tools.SmartShuntBleConsole/README.md) | Standalone live BLE service/public/private/raw-write investigation console |
