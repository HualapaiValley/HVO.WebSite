# WebSite process profile

Process revision: `2026-10-03.1` (WebSite pilot).

| Setting | WebSite value |
|---|---|
| Integration/default branch | `main` |
| Feature merge | Squash; no direct issue commits to main |
| Workspace | Dedicated branch/checkout; preserve other owners' claims/work |
| Required local application evidence | Pinned SDK build, zero warnings/errors; applicable non-live tests |
| SDK | `global.json` is authoritative; currently 10.0.400 with roll-forward disabled |
| Independent reviewer default | One distinct non-implementation person/session; qualified human or configured eligible agent route |
| Default selection | Auto; explicit Preferred/Required and separation constraints override within authorized scope |
| Publication | Direct GitHub comments/reviews; faithful attributed relay when needed; no mandatory bot |
| CI | Bounded draft preflight, review gate, then standard build/test and Docker smoke |
| Finalization reservation | None |
| Release/deployment | Separate authorization; no production/hardware action implied by issue work |

Depth is Mechanical for demonstrably non-behavioral changes, Standard for ordinary bounded application work, and Deep for security, concurrency, durability, migrations, safety, CI control, deployment or interactions across components. See [review coverage and selection](../../.agents/skills/hvo-code-review/references/review-format.md).

Choose an eligible model/tool using its available context, repository/test access, requested risk lenses and current runtime settings. Keep exact model preferences in assignment/configuration rather than universal skill text. Discover live capabilities; verify selectable settings before promising a specific model/effort. Unavailable required choices keep review incomplete. Managed/unknown metadata is allowed only where the assignment permits it.

Local application checks:

```text
dotnet restore HVO.WebSite.sln --locked-mode --nologo
dotnet build HVO.WebSite.sln --no-restore --nologo
dotnet test HVO.WebSite.sln --no-build --filter "TestCategory!=Integration&TestCategory!=Live"
bash tools/run-home-assistant-integration-tests.sh
```

The integration runner and live Playwright checks need their documented services/environment. UI changes include applicable Playwright/screenshots. Workflow changes require focused transition/gate tests and workflow syntax checks in addition to the existing build expectations. Record an unavailable check with its cause and remaining acceptance; do not hide it. A container using the exact pinned SDK is valid local evidence when the host's SDK differs.

Keep CSS/theme/offline-gateway, Key Vault, logging, hardware, contract and curated project-history rules from [project guidance](../AGENT_PROJECT_GUIDANCE.md). The exporter remains disabled in production and retired collector apps are not deployment targets.

## Pilot adoption

This revision is first adopted by issue #394. The new trusted metadata automation becomes active after its workflow and helpers reach main. During the adoption PR, the owner performs those state writes manually while local/controller fixtures and a read-only candidate CI gate exercise the same evidence rules. Standard CI still waits for independently reviewed current source; there is no missing-review bypass. Record the bootstrap boundary in the PR evidence.

The original CI requires an `hvo-website` self-hosted Linux x64 runner; GitHub reported none when this pilot started. Any hosted-runner migration must preserve the standard checks and be stated in the reviewed PR. Repository-required-check configuration is a separate GitHub setting and must be recorded when changed; committed policy alone does not activate protection.

The pilot moves standard CI to hosted Ubuntu, uses ephemeral Docker smoke resources and installs Chromium's required system dependencies. All existing application/integration/deployment-policy checks remain. The trusted `Review Evidence` status and standard `build-and-test` / `docker-smoke` checks, plus required conversation resolution and an up-to-date candidate, are the intended main-branch protection profile after adoption. Required native approval count can remain zero for attributed same-account independent sessions. Do not claim these repository settings are active until their GitHub state is verified.

The repository requires Actions to be pinned to full commit SHAs. Keep readable version comments beside the immutable references and verify each replacement against its official Action repository. Tagged references alone fail job preparation even if workflow YAML is valid.

Initial local application validation used the exact 10.0.400 Linux ARM64 SDK container. Missing jq/Chromium caused environment-only test failures that passed after installing those dependencies. The build produced 778 existing SQL-project warnings and zero errors; no application/database source changed in the pilot. Keep that baseline limitation visible and resolve the applicable zero-warning acceptance before claiming a green, fully qualified adoption. Do not suppress warnings or repair unrelated database schema under this workflow issue without authorized scope.
