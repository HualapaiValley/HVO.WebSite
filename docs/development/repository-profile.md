# WebSite process profile

Process revision: `2026-10-04.1` (selective CI implementation awaiting adoption).

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
| CI | Bounded draft preflight, immutable-source review gate and trusted affected-work plan, then selected execution with stable build/test and Docker aggregation; main/nightly/default manual runs remain full |
| Finalization reservation | None |
| Release/deployment | Separate authorization; no production/hardware action implied by issue work |

Depth is Mechanical for demonstrably non-behavioral changes, Standard for ordinary bounded application work, and Deep for security, concurrency, durability, migrations, safety, CI control, deployment or interactions across components. See [review coverage and selection](../../.agents/skills/hvo-code-review/references/review-format.md).

Choose an eligible model/tool using its available context, repository/test access, requested risk lenses and current runtime settings. Keep exact model preferences in assignment/configuration rather than universal skill text. Discover live capabilities; verify selectable settings before promising a specific model/effort. Unavailable required choices keep review incomplete. Managed/unknown metadata is allowed only where the assignment permits it.

Local application checks:

```text
dotnet restore HVO.WebSite.sln --locked-mode --nologo
dotnet build HVO.WebSite.sln --no-restore --nologo
dotnet test HVO.WebSite.sln --no-build --no-restore --filter "TestCategory!=Integration&TestCategory!=Live"
```

Dependency-aware post-review PR CI selects whole test assemblies, provisioned lanes, application builds and Docker images from both base and candidate graphs plus explicit input ownership. Main/nightly/default manual runs retain all work, and the ci:full PR label broadens selection. Full local solution validation remains required. The HA runner provisions only its HA category; simulator execution is separate. Integration and browser lanes need their documented fixtures. UI changes include applicable Playwright/screenshots. Workflow changes require focused planner, transition/gate, runner and workflow-syntax checks. Record unavailable checks and their remaining acceptance. A container using the exact pinned SDK is valid local evidence when the host differs. See [testing](testing.md) and [selective CI](selective-ci.md) for commands, partitions, source binding and pending adoption/measurement status.

The trusted WebSite gate requires source-bound local evidence with stable IDs: `local:pinned-sdk-build-zero-warnings`, `local:non-live-tests`, and, for workflow/helper changes, `local:pr-process-tests`. Required local evidence must pass; the build records SDK 10.0.400, zero errors and zero warnings. An author cannot waive these prerequisites with a `required: false` field or an explained blocked result. Post-review integration/container CI is a separate stage and does not become a prerequisite to start itself. Draft preflight still permits honest failing or blocked preparation.

Keep CSS/theme/offline-gateway, Key Vault, logging, hardware, contract and curated project-history rules from [project guidance](../AGENT_PROJECT_GUIDANCE.md). The exporter remains disabled in production and retired collector apps are not deployment targets.

## Pilot adoption

The original pilot was introduced by issue #394. Its adoption PR could not make its own evaluator or controller an approval authority; the owner performed state writes manually until the trusted code reached main. The same boundary applies to #411: the existing trusted full policy validates the selective-CI implementation before the new planner/workflow/controller can become authoritative. Keep historical adoption evidence separate from current qualification.

The original CI requires an `hvo-website` self-hosted Linux x64 runner; GitHub reported none when this pilot started. Any hosted-runner migration must preserve the standard checks and be stated in the reviewed PR. Repository-required-check configuration is a separate GitHub setting and must be recorded when changed; committed policy alone does not activate protection.

The pilot moves standard CI to hosted Ubuntu, uses ephemeral Docker smoke resources and installs Chromium's required system dependencies. All existing application/integration/deployment-policy checks remain. The trusted `Review Evidence` status and standard `build-and-test` / `docker-smoke` checks, plus required conversation resolution and an up-to-date candidate, are the intended main-branch protection profile after adoption. Required native approval count can remain zero for attributed same-account independent sessions. Do not claim these repository settings are active until their GitHub state is verified.

PR CI uses a read-only `pull_request_target` workflow whose admission evaluator, planner and aggregation code come from the immutable trusted target tip. Standard execution checks out the verified immutable GitHub merge SHA, with no write credentials or secret environment. The controller accepts only the matching PR/head/target/merge tuple, a fresh complete first attempt created after approval, and four successful internal jobs: review-evidence, plan, build-and-test and docker-smoke. Build-and-test verifies the complete planned execution union, including explicit reasons for skipped empty lanes. It publishes candidate-head statuses for the intended required checks; native target-workflow checks alone do not establish readiness. Partial or old-target reruns cannot qualify. An unchanged-source infrastructure recovery uses one fresh complete run.

The #411 implementation PR must pass the previously adopted full CI before selective execution becomes trusted on main. Its full current-source local checks, hosted activation and representative timing measurements are pending. Manual main benchmark profiles measure selected execution but do not establish PR admission. No caching change or measured cache gain is claimed. The last inspected main protection endpoint returned 404 and rulesets were empty; intended protection settings remain unconfigured and must not be described as active.

The repository requires Actions to be pinned to full commit SHAs. Keep readable version comments beside the immutable references and verify each replacement against its official Action repository. Tagged references alone fail job preparation even if workflow YAML is valid.

`src/HVO.Database` is an archive of SQL stored procedures and table definitions, not an active build/deployment project. The owner confirmed its exclusion during this pilot. Keep `HVO.Database.sqlproj` outside `HVO.WebSite.sln` and active project references; retain the files for historical reference and direct file-reading tests. EF Core migrations in `HVO.DataModels` remain the schema source of truth. The active solution still requires zero warnings and zero errors; do not suppress warnings or repair archive schema to satisfy its build.

Initial exact-SDK 10.0.400 Linux ARM64 validation accidentally included that archived SQL project and reported 778 SQL warnings with zero errors. Missing jq/Chromium caused environment-only test failures that passed after installing those dependencies. Revalidate the active solution after exclusion and report actual results; the historical run does not establish current acceptance.
