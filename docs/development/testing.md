# Test lanes and evidence

The active solution uses MSTest with FluentAssertions, bUnit for component behavior and MSTest/Playwright for browser behavior. It has 13 test assemblies; use discovery and retained reports for current counts. Do not infer coverage from stale test-count prose. The pinned SDK in global.json is authoritative.

Install the repository's development container or equivalent host tools: exact .NET SDK, Node.js, Ruby (stdlib YAML/JSON), Bash, Docker Compose, Python3, jq, curl and openssl. Ruby is provisioned in the development container and bootstrapped if absent in the bounded GitHub preflight. `node --test tools/pr-process.test.mjs` uses Ruby to parse workflow YAML; Ruby ENOENT means the environment is incomplete. Install the Chromium browser/system prerequisites when running the Playwright assembly:

```bash
dotnet restore HVO.WebSite.sln --locked-mode --nologo
dotnet build HVO.WebSite.sln --no-restore --nologo
pwsh tests/HVO.WebSite.PlaywrightTests/bin/Debug/net10.0/playwright.ps1 install --with-deps chromium
node --test tools/pr-process.test.mjs tools/test-categories.test.mjs
bash tools/validate-test-categories.sh
```

## Selection union

| Lane | Selection | Fixture and runner settings |
|---|---|---|
| Fast | `TestCategory!=Integration&TestCategory!=Live` | Self-contained MSTest/bUnit and the current isolated HA wind-card browser fixture. Default test.runsettings: 120000ms test-session bound. |
| Simulator integration | `TestCategory=Integration&TestCategory!=HomeAssistantIntegration&TestCategory!=Live` | Loopback protocol simulators, local HTTP/SQLite and other self-contained integration. Explicit integration.runsettings: 900000ms bound. |
| Provisioned Home Assistant | `TestCategory=HomeAssistantIntegration&TestCategory!=Live` | Disposable Docker HA/MQTT/fake-ingest environment supplied by tools/run-home-assistant-integration-tests.sh; strict integration.runsettings. |
| Live | `TestCategory=Live` | Opt-in physical/deployed targets, configured explicitly; excluded from routine CI. |

Every HomeAssistantIntegration test also has Integration. Thus Fast + Simulator + provisioned HA covers all non-Live tests. Live cannot also carry a required non-live category. Category checks accept literal Integration, HomeAssistantIntegration, Live, Browser and SqlServerIntegration names and validate each test method's effective class/method categories. Unknown or dynamic category names fail for a deliberate convention update. Integration/Live folders and matching `*IntegrationTests.cs`/`*LiveTests.cs` names require their respective category; helpers without test methods are exempt.

Browser is a non-live category: under current filters it remains included in Fast unless also marked Integration. #408 owns deterministic website/ThemeSandbox hosts and browser migration; existing deployed-site tests remain Live and legacy ignored scaffolds do not establish browser acceptance. #409 owns the provisioned SQL-provider lane; SqlServerIntegration requires Integration and its provisioning/filter must be added together when adopted. #411 owns later dependency-aware selection. Until those changes land, no future lane is claimed active.

The WU/CWOP loopback client tests now carry Integration explicitly. They move from Fast to Simulator without leaving the selection union. This classification does not change their behavior or #387's separate publisher cadence correction.

## Local and CI commands

The default RunSettingsFilePath is applied in Directory.Build.targets after each project declares IsTestProject. Non-test projects receive no default. An explicit property or `--settings` overrides the default. The session bound is per test assembly; it is not an individual method timeout. Use the longer explicit integration setting for slow fixture/restart work; do not extend real-time waits to fix fake-time races.

```bash
dotnet test HVO.WebSite.sln --no-build --filter "TestCategory!=Integration&TestCategory!=Live" --logger trx --results-directory TestResults/fast
dotnet test HVO.WebSite.sln --no-build --settings integration.runsettings --filter "TestCategory=Integration&TestCategory!=HomeAssistantIntegration&TestCategory!=Live" --logger trx --results-directory TestResults/integration/simulators
bash tools/run-home-assistant-integration-tests.sh
```

The HA runner retains standalone restore/build behavior and supplies the required fixture variables. Its three HA assemblies run sequentially because tests restart the shared stack. Each test session has a 15-minute budget, adequate for observed complete HA runs around five to six minutes, within the CI job's separate bound. Strict integration settings map inconclusive missing-fixture results to failure; running a required lane without provisioning must fail visibly. Routine settings retain existing optional inconclusive behavior. A missing fixture or skipped/ignored test is never reported as passed acceptance.

Generated TRX filenames retain distinct assembly reports even when tests start together. Fast reports are under TestResults/fast, simulator reports under TestResults/integration/simulators, and HA reports under TestResults/integration/home-assistant/<project>. CI uploads all TestResults/** even after failure. Do not set a single shared LogFileName for solution testing. Preserve reports and note actual source, command, environment, assembly/lane and skipped cases in review evidence.
