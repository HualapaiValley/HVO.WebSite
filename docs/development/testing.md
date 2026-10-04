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
bash tools/run-home-assistant-integration-tests.sh --ha-only
bash tools/run-sql-server-integration-tests.sh
node --test tools/pr-process.test.mjs tools/test-categories.test.mjs tools/ci-plan.test.mjs tools/ci-source.test.mjs tools/ci-run.test.mjs
node --test tools/sql-server-integration.test.mjs
python3 -m unittest discover -s tools -p 'ci_*_test.py'
python3 tools/verify-home-assistant-runner.py
bash tools/verify-docker-build-smoke.sh
bash tools/validate-test-categories.sh
```

The full local fast command deliberately retains all non-integration browser cases, so install Chromium before running it. The owned #409 SQL fixture is available and locally qualified against the actual production provider; use its provisioned command rather than including SQL categories in an unprovisioned solution run. Local qualification does not establish a passing hosted candidate/main run. #408's browser migration remains pending. The full exact-SDK build and applicable local tests remain required before review.

## Selected CI partitions

The planner selects whole assemblies and assigns their required non-live categories to lanes. Fast and simulator execution excludes Browser; the browser job installs Chromium only when selected. The current Playwright assembly belongs wholly to the browser job for its non-integration, non-live tests, including the isolated HA wind-card cases that predate the Browser trait.

| Lane | Runtime selection | Settings and environment |
|---|---|---|
| Fast | `TestCategory!=Integration&TestCategory!=Browser&TestCategory!=Live`, outside the Playwright assembly | test.runsettings;120000ms session bound |
| Simulator | `TestCategory=Integration&TestCategory!=HomeAssistantIntegration&TestCategory!=SqlServerIntegration&TestCategory!=Browser&TestCategory!=Live` | Self-contained loopback/HTTP/SQLite fixtures; integration.runsettings |
| Home Assistant | `TestCategory=HomeAssistantIntegration&TestCategory!=Live` | Disposable HA/MQTT/fake-ingest stack; sequential assemblies; integration.runsettings |
| SQL Server | `TestCategory=SqlServerIntegration&TestCategory!=Live` | Owned disposable provider fixture; integration.runsettings; locally qualified migrations/queries/conflicts/atomicity |
| Browser | `TestCategory=Browser&TestCategory!=Live`; Playwright assembly uses `TestCategory!=Integration&TestCategory!=Live` | Owned browser fixtures and Chromium; integration.runsettings |
| Live | `TestCategory=Live` | Explicit opt-in physical/deployed targets; excluded from routine validation |

HomeAssistantIntegration and SqlServerIntegration require Integration and cannot be combined. Browser cannot combine with Integration; unsupported Playwright integration ownership also fails visibly. Live cannot carry a required non-live category. Literal category validation checks each method's effective class/method categories; unknown or dynamic names fail. Conditional test declarations, attribute aliases and unsupported test-class inheritance fail planning visibly until their ownership convention is supported. A full build cannot repair an unknown test partition.

## Owned website and ThemeSandbox browser fixtures

Website and ThemeSandbox checks run against actual application hosts on OS-assigned loopback ports. The website replaces only test services with deterministic in-memory data, a fake clock and request authentication; the real authorization/challenge path remains exercised while the identity-provider redirect is intercepted. No production keys, external base URL or running application are required. The application project references build both targets. Each test owns its browser context and host, with bounded actions and teardown.

```bash
dotnet test tests/HVO.WebSite.PlaywrightTests/HVO.WebSite.PlaywrightTests.csproj --no-build --no-restore --settings integration.runsettings --filter "TestCategory!=Integration&TestCategory!=Live" --logger trx --results-directory TestResults/browser/HVO.WebSite.PlaywrightTests
python3 tools/ci-results.py TestResults/browser/HVO.WebSite.PlaywrightTests
```

This runs every non-Live case in the Playwright assembly, including the isolated HA wind-card fixture. The 22 migrated main-site/sandbox cases retain public, authorized, responsive, control, style and reference-route assertions. Dashboard and scoped-chart suites add real Chart.js creation/theme/gap/lifecycle, provider failure recovery and empty-state checks. BrowserFailureQualificationTests inject a missing chart script and a blocked theme handler; the same assertions used by positive checks must fail and then pass after recovery. A visible canvas or button is insufficient.

BrowserSession retains console/page errors, a screenshot and a trace under TestResults/browser by default, with unique test directories and TRX attachments. HVO_BROWSER_ARTIFACTS overrides that root for local evidence. CI uploads TestResults/** even on failure. Unexpected browser errors fail the migrated cases; intentional failure cases retain their expected diagnostics. Deployed Home Assistant/physical target checks retain Live and explicit opt-in configuration.

Directory.Build.targets applies the default RunSettingsFilePath after IsTestProject is known. Non-test projects receive no default; an explicit property or --settings overrides it. The timeout is per assembly session, not per method. integration.runsettings provides 900000ms and maps inconclusive results to failure. Do not lengthen real-time waits to repair fake-time races.

## Home Assistant execution

Explicit HA execution runs only the provisioned HA category. Simulators run independently and do not require Docker or HA credentials. Standalone HA execution performs locked restores and builds its three allowlisted assemblies before provisioning:

```bash
bash tools/run-home-assistant-integration-tests.sh --ha-only
```

CI prepares selected whole assemblies and explicitly reuses that work:

```bash
bash tools/run-home-assistant-integration-tests.sh --ha-only --prebuilt --projects tests/HVO.Edge.HomeAssistant.Mqtt.Tests/HVO.Edge.HomeAssistant.Mqtt.Tests.csproj
```

--projects accepts one or more exact repository-relative paths for the MQTT, HA exporter and HA entity-migration test projects. Omission selects all three; explicit empty, unknown or duplicate selections fail before provisioning. --prebuilt skips preparation, and all test invocations use --no-build --no-restore. --configuration Debug|Release and --results-directory ROOT control configuration and report location. --coverage explicitly enables XPlat Code Coverage; PR execution omits collection, while main/nightly/manual runs request it.

For compatibility with the trusted CI workflow during adoption, invoking the runner without --ha-only runs the complete simulator partition before provisioning HA. Without --prebuilt, that mode restores and builds the whole solution. Simulator reports use TestResults/integration/simulators; --results-directory controls only HA reports. Selected CI explicitly supplies --ha-only so it does not repeat the independent simulator lane.

Assemblies remain sequential because tests restart the shared stack. Every invocation retains the 15-minute integration session setting. The runner bounds readiness, records broker diagnostics on failure, and removes its stack and temporary Docker configuration. Missing services or reports cannot count as passing acceptance.

## SQL Server execution

The selected workflow's existing `sql-server` job requires the eight SQL runner safety
checks before building the owned API test assembly,
then invokes [the disposable SQL runner](sql-server-integration-tests.md) and independently
verifies fresh passing reports under `TestResults/sql-server/HVO.WebSite.ApiTests`.
The planner discovers the actual Integration/SqlServerIntegration method categories;
an empty lane still needs its explicit verified empty reason. A helper exit alone
cannot qualify the planned lane. Standalone runs default to `TestResults/sql-server`;
`HVO_SQL_TEST_RESULTS_DIRECTORY` selects a separate evidence directory.

Tests reuse preparation with --no-build --no-restore and strict integration settings.
PR execution omits coverage, while `CI_COVERAGE=true` preserves main/nightly/manual
XPlat collection. Missing fixture configuration fails visibly; only local default
Docker resources and generated test-owned databases are accepted. SQL categories
remain excluded from simulator and unprovisioned HA execution. No extra monolithic
SQL step is added to the selected workflow.

## Reports and source-bound evidence

Selected execution gives every lane/project its own TestResults directory and removes that invocation's prior TRX before testing. tools/ci-results.py requires fresh, nonempty reports with actual passing results and consistent counters. Failures, aborted/incomplete/inconclusive results and unexpected ignored cases fail the invocation; a zero-test filter is not success.

The legacy whole-solution simulator invocation alone uses --allow-empty-reports because assemblies without simulator tests can emit zero-result TRX. Those reports must have all-zero counters, and the complete invocation must still contain actual passing tests. Selected per-project execution never enables that exception.

The two obsolete ignored website scaffolds have been removed: the home heading is asserted by MainSite_ShouldRenderPublicHomeShell, and the disabled-configuration scaffold had no behavior assertion. Selected browser execution has no ignored-test allowance. Failed, ignored, inconclusive, empty or stale browser reports fail qualification, including when a helper exits successfully. The runner fixtures exercise those failure paths without launching real applications.

Solution testing uses generated TRX filenames rather than one shared LogFileName. HA reports retain `TestResults/integration/home-assistant/<project>`; selected direct lanes use `TestResults/<lane>/<project>`. Workflow artifact uploads retain reports after failure for validation, HA, SQL and browser jobs. Browser fixtures also retain their applicable screenshots, traces and console diagnostics.

Record the exact source, command, SDK, environment, project/lane and ignored or unavailable cases. See [selective CI](selective-ci.md) for trusted planning, aggregation, adoption and measurement boundaries.
