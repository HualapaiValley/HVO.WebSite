# Test lanes and evidence

The active solution uses MSTest with FluentAssertions, bUnit for component behavior and MSTest/Playwright for browser behavior. Use discovery and retained reports for current counts. The exact SDK in global.json is authoritative. Dependency selection changes CI execution, not the requirement to validate the full solution locally before review.

Install the development container or equivalent host tools: exact .NET SDK, Node.js, Ruby with stdlib YAML/JSON, Bash, Docker Compose, Python3, jq, curl and openssl. Ruby is provisioned in the container and bootstrapped if missing in bounded draft preflight. Ruby ENOENT in workflow tests means the environment is incomplete. Browser execution also needs Chromium and its system dependencies.

## Preparation and full local checks

```bash
dotnet restore HVO.WebSite.sln --locked-mode --nologo
dotnet build HVO.WebSite.sln --no-restore --nologo
pwsh tests/HVO.WebSite.PlaywrightTests/bin/Debug/net10.0/playwright.ps1 install --with-deps chromium
dotnet test HVO.WebSite.sln --no-build --no-restore --filter "TestCategory!=Integration&TestCategory!=Live" --logger trx --results-directory TestResults/fast
dotnet test HVO.WebSite.sln --no-build --no-restore --settings integration.runsettings --filter "TestCategory=Integration&TestCategory!=HomeAssistantIntegration&TestCategory!=SqlServerIntegration&TestCategory!=Browser&TestCategory!=Live" --logger trx --results-directory TestResults/integration/simulators
bash tools/run-home-assistant-integration-tests.sh
node --test tools/pr-process.test.mjs tools/test-categories.test.mjs tools/ci-plan.test.mjs tools/ci-run.test.mjs
python3 -m unittest discover -s tools -p 'ci_*_test.py'
python3 tools/verify-home-assistant-runner.py
bash tools/verify-docker-build-smoke.sh
bash tools/validate-test-categories.sh
```

The full local fast command deliberately retains all non-integration browser cases, so install Chromium before running it. Add applicable provisioned SQL and other fixture checks when their owned runners are available. #409's SQL integration and qualification are pending; a declared SQL lane does not establish a passing provider run. The full exact-SDK build and applicable local tests remain required for the #411 implementation PR.

## Selected CI partitions

The planner selects whole assemblies and assigns their required non-live categories to lanes. Fast and simulator execution excludes Browser; the browser job installs Chromium only when selected. The current Playwright assembly belongs wholly to the browser job for its non-integration, non-live tests, including the isolated HA wind-card cases that predate the Browser trait.

| Lane | Runtime selection | Settings and environment |
|---|---|---|
| Fast | `TestCategory!=Integration&TestCategory!=Browser&TestCategory!=Live`, outside the Playwright assembly | test.runsettings;120000ms session bound |
| Simulator | `TestCategory=Integration&TestCategory!=HomeAssistantIntegration&TestCategory!=SqlServerIntegration&TestCategory!=Browser&TestCategory!=Live` | Self-contained loopback/HTTP/SQLite fixtures; integration.runsettings |
| Home Assistant | `TestCategory=HomeAssistantIntegration&TestCategory!=Live` | Disposable HA/MQTT/fake-ingest stack; sequential assemblies; integration.runsettings |
| SQL Server | SqlServerIntegration excluding Live | Owned provider fixture from #409; integration and qualification pending |
| Browser | `TestCategory=Browser&TestCategory!=Live`; Playwright assembly uses `TestCategory!=Integration&TestCategory!=Live` | Owned browser fixtures and Chromium; integration.runsettings |
| Live | `TestCategory=Live` | Explicit opt-in physical/deployed targets; excluded from routine validation |

HomeAssistantIntegration and SqlServerIntegration require Integration and cannot be combined. Browser cannot combine with Integration; unsupported Playwright integration ownership also fails visibly. Live cannot carry a required non-live category. Literal category validation checks each method's effective class/method categories; unknown or dynamic names fail. Conditional test declarations, attribute aliases and unsupported test-class inheritance fail planning visibly until their ownership convention is supported. A full build cannot repair an unknown test partition. #408 owns browser migration and removal of legacy scaffolds.

Directory.Build.targets applies the default RunSettingsFilePath after IsTestProject is known. Non-test projects receive no default; an explicit property or --settings overrides it. The timeout is per assembly session, not per method. integration.runsettings provides 900000ms and maps inconclusive results to failure. Do not lengthen real-time waits to repair fake-time races.

## Home Assistant execution

The HA runner runs only the provisioned HA category. Simulators run independently and do not require Docker or HA credentials. Standalone HA execution performs locked restores and builds its three allowlisted assemblies before provisioning:

```bash
bash tools/run-home-assistant-integration-tests.sh
```

CI prepares selected whole assemblies and explicitly reuses that work:

```bash
bash tools/run-home-assistant-integration-tests.sh --prebuilt --projects tests/HVO.Edge.HomeAssistant.Mqtt.Tests/HVO.Edge.HomeAssistant.Mqtt.Tests.csproj
```

--projects accepts one or more exact repository-relative paths for the MQTT, HA exporter and HA entity-migration test projects. Omission selects all three; explicit empty, unknown or duplicate selections fail before provisioning. --prebuilt skips preparation, and all test invocations use --no-build --no-restore. --configuration Debug|Release and --results-directory ROOT control configuration and report location. --coverage explicitly enables XPlat Code Coverage; PR execution omits collection, while main/nightly/manual runs request it.

Assemblies remain sequential because tests restart the shared stack. Every invocation retains the 15-minute integration session setting. The runner bounds readiness, records broker diagnostics on failure, and removes its stack and temporary Docker configuration. Missing services or reports cannot count as passing acceptance.

## Reports and source-bound evidence

Selected execution gives every lane/project its own TestResults directory and removes that invocation's prior TRX before testing. tools/ci-results.py requires fresh, nonempty reports with actual passing results and consistent counters. Failures, aborted/incomplete/inconclusive results and unexpected ignored cases fail the invocation; a zero-test filter is not success.

The only ignored-test allowance is the actual existing HVO.WebSite.PlaywrightTests.PlaywrightTestSetupTests.PlaywrightSuite_IsConfiguredButDisabledByDefault result observed in the #407 baseline TRX. The verifier requires a matching class/method definition and permits that identity at most once across all reports in the invocation. It is reported as ignored, never as browser acceptance. The separate HomePage_ShouldRenderMainHeading scaffold was not discovered in that baseline and receives no allowance. #408 must replace both misleading scaffolds with meaningful coverage. Missing or mismatched definitions, duplicate ignored results and other ignored cases fail; pending browser migration must be reconciled before qualification.

Solution testing uses generated TRX filenames rather than one shared LogFileName. HA reports retain `TestResults/integration/home-assistant/<project>`; selected direct lanes use `TestResults/<lane>/<project>`. Workflow artifact uploads retain reports after failure for validation, HA, SQL and browser jobs. Browser fixtures also retain their applicable screenshots, traces and console diagnostics.

Record the exact source, command, SDK, environment, project/lane and ignored or unavailable cases. See [selective CI](selective-ci.md) for trusted planning, aggregation, adoption and measurement boundaries.
