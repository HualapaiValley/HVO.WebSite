# Test project guide

The active solution has 13 MSTest assemblies and one fixture application. This guide assigns each project a documentation owner and a focused non-live command. [Test lanes and evidence](../docs/development/testing.md) owns the full validation procedure, category rules, report checks and fixtures; [selective CI](../docs/development/selective-ci.md) owns dependency planning and CI qualification. Categories come from actual class/method attributes and the [literal-category validator](../tools/test-categories.mjs), not directory names or a fixed test count.

## Prepare once

Run commands from the repository root using the exact SDK pinned in [global.json](../global.json). Prepare the whole solution before the focused `--no-build --no-restore` commands below:

```bash
dotnet restore HVO.WebSite.sln --locked-mode --nologo
dotnet build HVO.WebSite.sln --no-restore --nologo
```

The build must have zero warnings and errors. Install the [browser prerequisites](HVO.WebSite.PlaywrightTests/README.md#setup) before the required full local non-live run:

```bash
dotnet test HVO.WebSite.sln --no-build --no-restore --filter "TestCategory!=Integration&TestCategory!=Live" --logger trx --results-directory TestResults/fast
```

This command includes Browser. A focused project check does not replace it. Temporary-directory access is needed for SQLite, configuration, key and artifact fixtures. [Development setup](../docs/development/testing.md#preparation-and-full-local-checks) lists host tools required by repository helper/policy checks.

## Category and fixture ownership

| Effective category | Prerequisite and execution owner |
|---|---|
| No category | Local unit/component/in-process or owned temporary-file fixtures; the local non-live filter includes these. |
| `Integration` alone | Davis TCP/HTTP and JK fake-transport simulators; use the simulator command below. No physical devices or HA credentials. |
| `Integration` + `HomeAssistantIntegration` | [Disposable HA/MQTT/fake-ingest environment](HomeAssistant.IntegrationEnvironment/README.md); the runner provisions and sequentially runs the three HA assemblies. |
| `Integration` + `SqlServerIntegration` | [Disposable SQL Server fixture](../docs/development/sql-server-integration-tests.md); the runner owns databases and provider prerequisites for API tests. |
| `Browser` | [Owned website/ThemeSandbox or isolated card fixtures](HVO.WebSite.PlaywrightTests/README.md), Chromium and its system dependencies. Browser cannot combine with Integration. |
| `Live` | Explicitly authorized physical/deployed targets and their opt-in configuration. Excluded from routine local and PR validation. |

After preparation, use the established fixture commands for the remaining non-live partitions:

```bash
dotnet test HVO.WebSite.sln --no-build --no-restore --settings integration.runsettings --filter "TestCategory=Integration&TestCategory!=HomeAssistantIntegration&TestCategory!=SqlServerIntegration&TestCategory!=Browser&TestCategory!=Live" --logger trx --results-directory TestResults/integration/simulators
bash tools/run-home-assistant-integration-tests.sh --ha-only
bash tools/run-sql-server-integration-tests.sh
```

An unprovisioned solution run with only `TestCategory!=Live` does not supply HA or SQL fixtures. The [run settings](../integration.runsettings) make inconclusive required-integration results fail; absent fixtures, empty/stale TRX, failures and unexpected ignored cases cannot establish success. The default [test settings](../test.runsettings) apply only to test projects through [Directory.Build.targets](../Directory.Build.targets). Session limits are per assembly, not per method. Discover actual passing counts from fresh retained reports.

Post-review CI selects whole assemblies and assigns their categories to lanes. It excludes Browser from fast/simulator jobs and provisions selected HA/SQL/browser lanes. The full local solution requirement remains unchanged. The project entries below describe current effective categories, not separate CI policy.

## HVO.Edge.Contracts.Tests

Tests [shared edge contracts](../src/HVO.Edge.Contracts/README.md): payload serialization, observation identities, diagnostics, power snapshots, trace context and API-key matching. All current cases are uncategorized and use local data; no external service or device is required. [Project definition](HVO.Edge.Contracts.Tests/HVO.Edge.Contracts.Tests.csproj).

```bash
dotnet test tests/HVO.Edge.Contracts.Tests/HVO.Edge.Contracts.Tests.csproj --no-build --no-restore --filter "TestCategory!=Integration&TestCategory!=Live"
```

## HVO.Edge.Hosting.Tests

Tests [shared hosting](../src/HVO.Edge.Hosting/README.md): mounted configuration and secret-path validation, headless health/diagnostics/authentication, logging, OTLP endpoint conventions and outbox continuity. Current cases are uncategorized. They use owned temporary state and the [reference host](#hvoedgehostingtesthost), without a gateway or remote telemetry service. [Project definition](HVO.Edge.Hosting.Tests/HVO.Edge.Hosting.Tests.csproj).

```bash
dotnet test tests/HVO.Edge.Hosting.Tests/HVO.Edge.Hosting.Tests.csproj --no-build --no-restore --filter "TestCategory!=Integration&TestCategory!=Live"
```

## HVO.Edge.Hosting.TestHost

This is a fixture application, not a fourteenth test assembly or deployment target. [Program.cs](HVO.Edge.Hosting.TestHost/Program.cs) registers the shared headless runtime with fake acquisition, diagnostics and successful outbox delivery. [HeadlessRuntimeTests](HVO.Edge.Hosting.Tests/Hosting/HeadlessRuntimeTests.cs) start it through `WebApplicationFactory`, using the checked-in test configuration/credential and isolated database path. Build it with the solution and exercise it through the Hosting.Tests command above; no independent `dotnet test`, live endpoint or manual daemon is needed. [Project definition](HVO.Edge.Hosting.TestHost/HVO.Edge.Hosting.TestHost.csproj).

## HVO.Edge.Outbox.Tests

Tests [durable outbox behavior](../src/HVO.Edge.Outbox/README.md): SQLite schema/initialization, enqueue/selection/state transitions, forwarder failure outcomes, health and option validation. Current cases are uncategorized and own disposable SQLite databases; they do not back up or mutate a running collector volume. [Project definition](HVO.Edge.Outbox.Tests/HVO.Edge.Outbox.Tests.csproj).

```bash
dotnet test tests/HVO.Edge.Outbox.Tests/HVO.Edge.Outbox.Tests.csproj --no-build --no-restore --filter "TestCategory!=Integration&TestCategory!=Live"
```

## HVO.Edge.HomeAssistant.Mqtt.Tests

Tests [MQTT projection and routing](../src/HVO.Edge.HomeAssistant.Mqtt/README.md): identities, discovery, current-state availability, option/credential validation, worker reconnect and exact non-retained command routing. Uncategorized cases are local. `Integration` + `HomeAssistantIntegration` cases require the [HA fixture](HomeAssistant.IntegrationEnvironment/README.md) for discovery, reconnection and simulated alert/recovery automations; invoke its runner rather than supplying production credentials. These checks do not qualify physical device writes. [Project definition](HVO.Edge.HomeAssistant.Mqtt.Tests/HVO.Edge.HomeAssistant.Mqtt.Tests.csproj).

```bash
dotnet test tests/HVO.Edge.HomeAssistant.Mqtt.Tests/HVO.Edge.HomeAssistant.Mqtt.Tests.csproj --no-build --no-restore --filter "TestCategory!=Integration&TestCategory!=Live"
```

## HVO.Edge.Exporter.HomeAssistant.Tests

Tests the [HA exporter](../src/HVO.Edge.Exporter.HomeAssistant/README.md): source-authority/mapping boundaries, observation identity, projection, local outbox/sender outcomes and diagnostics. Uncategorized cases are local; `Integration` + `HomeAssistantIntegration` cases use the disposable HA/fake-ingest fixture. The exporter remains disabled in production; passing tests do not enable source ownership. [Project definition](HVO.Edge.Exporter.HomeAssistant.Tests/HVO.Edge.Exporter.HomeAssistant.Tests.csproj).

```bash
dotnet test tests/HVO.Edge.Exporter.HomeAssistant.Tests/HVO.Edge.Exporter.HomeAssistant.Tests.csproj --no-build --no-restore --filter "TestCategory!=Integration&TestCategory!=Live"
```

## HVO.Hardware.DavisVantagePro2.Tests

Tests the [headless Davis collector](../src/HVO.Hardware.DavisVantagePro2/README.md): protocol/CRC/archive/display units/timezones, acquisition/outbox cursors, options, HA projection and WU/CWOP formatting/publishing. Uncategorized cases use local fakes; `Integration` cases use owned TCP/HTTP station and publisher simulators. `Live` cases require an authorized station and explicit source configuration. Routine validation needs no real station or publisher account. [Project definition](HVO.Hardware.DavisVantagePro2.Tests/HVO.Hardware.DavisVantagePro2.Tests.csproj).

```bash
dotnet test tests/HVO.Hardware.DavisVantagePro2.Tests/HVO.Hardware.DavisVantagePro2.Tests.csproj --no-build --no-restore --filter "TestCategory!=Integration&TestCategory!=Live"
```

The [simulator partition](#category-and-fixture-ownership) additionally exercises its Integration cases.

## HVO.Hardware.JkBms.Tests

Tests the [headless JK collector](../src/HVO.Hardware.JkBms/README.md): frame parsing/CRC, options, fake device lifecycle and adapter coordination, outbox migration/sending, diagnostics and HA projection. Uncategorized and `Integration` fake-transport cases need no BlueZ device. They also exercise the existing bounded secret-backed password-change ACK/readback path; simulation does not authorize or prove live credential writes. `Live` requires an explicitly configured physical BMS. [Project definition](HVO.Hardware.JkBms.Tests/HVO.Hardware.JkBms.Tests.csproj).

```bash
dotnet test tests/HVO.Hardware.JkBms.Tests/HVO.Hardware.JkBms.Tests.csproj --no-build --no-restore --filter "TestCategory!=Integration&TestCategory!=Live"
```

Use the simulator partition for its separate Integration cases.

## HVO.Hardware.Eg4.Tests

Tests the [headless EG4 collector](../src/HVO.Hardware.Eg4/README.md): fixed read-only protocol allowlists, serial/HID response framing and preserved captures, MPPT evidence, fake fleet/port coordination, power mapping, outbox and HA projection. Simulator cases are currently uncategorized, not members of the Integration lane. `Live` requires the approved physical connector and configured device path. Local fixtures do not qualify a cable or authorize running the [proof tool](../tools/HVO.Tools.Eg4SerialProbe/README.md). [Project definition](HVO.Hardware.Eg4.Tests/HVO.Hardware.Eg4.Tests.csproj).

```bash
dotnet test tests/HVO.Hardware.Eg4.Tests/HVO.Hardware.Eg4.Tests.csproj --no-build --no-restore --filter "TestCategory!=Integration&TestCategory!=Live"
```

## HVO.Hardware.VictronSmartShunt.Tests

Tests the [public-GATT collector](../src/HVO.Hardware.VictronSmartShunt/README.md): decoding/sign conventions, aggregate/session/worker behavior, fake-time freshness, canonical mapping, options, outbox and HA projection. Current simulated cases are uncategorized; `Live` requires authorized BlueZ access and a configured physical SmartShunt. No device or private-stream probe is needed for the command below. [Project definition](HVO.Hardware.VictronSmartShunt.Tests/HVO.Hardware.VictronSmartShunt.Tests.csproj).

```bash
dotnet test tests/HVO.Hardware.VictronSmartShunt.Tests/HVO.Hardware.VictronSmartShunt.Tests.csproj --no-build --no-restore --filter "TestCategory!=Integration&TestCategory!=Live"
```

## HVO.WebSite.UnitTests

Tests website controllers/services, power/weather composition and query behavior, auth/configuration/Data Protection, shared formatting/theme/chart/layout behavior through bUnit, and Staging calculations. All current cases are uncategorized. In-memory EF/fakes verify application behavior, not production-provider SQL semantics. [DatabaseProjectSchemaTests](HVO.WebSite.UnitTests/DatabaseProjectSchemaTests.cs) still reads archived `src/HVO.Database` SQL files directly; those inputs must remain available despite exclusion from the solution. [Project definition](HVO.WebSite.UnitTests/HVO.WebSite.UnitTests.csproj).

```bash
dotnet test tests/HVO.WebSite.UnitTests/HVO.WebSite.UnitTests.csproj --no-build --no-restore --filter "TestCategory!=Integration&TestCategory!=Live"
```

## HVO.WebSite.ApiTests

Tests real website HTTP/auth/forwarded-header/ingest boundaries with owned application factories. Uncategorized cases use test-owned services. `Integration` + `SqlServerIntegration` cases verify actual SQL Server migrations, persistence/retry/atomicity, source authority, observation conflicts and history/query behavior. Use the [SQL runner](../docs/development/sql-server-integration-tests.md); EF InMemory checks do not substitute for provider proof. [Project definition](HVO.WebSite.ApiTests/HVO.WebSite.ApiTests.csproj).

```bash
dotnet test tests/HVO.WebSite.ApiTests/HVO.WebSite.ApiTests.csproj --no-build --no-restore --filter "TestCategory!=Integration&TestCategory!=Live"
```

## HVO.WebSite.PlaywrightTests

Tests rendered website/ThemeSandbox behavior and the isolated HA wind card with owned non-live Browser fixtures. Separate deployed HA dashboard cases carry `Live`. Install Chromium/system dependencies and use the [focused browser guide](HVO.WebSite.PlaywrightTests/README.md) for host ownership, auth interception, assertion/failure coverage and distinct diagnostic artifacts. No running website, SQL Server, Entra credentials or production HA is required for Browser. [Project definition](HVO.WebSite.PlaywrightTests/HVO.WebSite.PlaywrightTests.csproj).

```bash
dotnet test tests/HVO.WebSite.PlaywrightTests/HVO.WebSite.PlaywrightTests.csproj --no-build --no-restore --settings integration.runsettings --filter "TestCategory!=Integration&TestCategory!=Live" --logger trx --results-directory TestResults/browser/HVO.WebSite.PlaywrightTests
```

## HVO.Tools.HomeAssistantEntityMigration.Tests

Tests the [HA entity/Energy tool](../tools/HVO.Tools.HomeAssistantEntityMigration/README.md): manifests, version/identity/collision/reference guards, rename rollback, Energy validation/restore and audit formatting. Uncategorized tests use fake clients; `Integration` + `HomeAssistantIntegration` cases use the runner-owned disposable registry. A passing fixture does not prove a production backup or authorize live registry/preference mutation. [Project definition](HVO.Tools.HomeAssistantEntityMigration.Tests/HVO.Tools.HomeAssistantEntityMigration.Tests.csproj).

```bash
dotnet test tests/HVO.Tools.HomeAssistantEntityMigration.Tests/HVO.Tools.HomeAssistantEntityMigration.Tests.csproj --no-build --no-restore --filter "TestCategory!=Integration&TestCategory!=Live"
```

## Reports and review evidence

Use distinct results directories and generated TRX names when running multiple projects/lanes. [Report qualification](../docs/development/testing.md#reports-and-source-bound-evidence) requires fresh nonempty passing results and consistent counters; the browser guide also identifies screenshots, console logs and traces. Record the exact source, SDK, command, performer and unavailable cases. Required full local evidence precedes independent review; provisioned post-review CI and actual source-bound checks are a separate gate. Counts and successful hosted qualification belong in retained reports, not this project index.
