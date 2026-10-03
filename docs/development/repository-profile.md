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
```

Standard post-review CI also runs `bash tools/run-home-assistant-integration-tests.sh` and the full Docker smoke checks. The integration runner and live Playwright checks need their documented services/environment. UI changes include applicable Playwright/screenshots. Workflow changes require focused transition/gate tests and workflow syntax checks in addition to the existing build expectations. Record an unavailable check with its cause and remaining acceptance; do not hide it. A container using the exact pinned SDK is valid local evidence when the host's SDK differs.

The trusted WebSite gate requires source-bound local evidence with stable IDs: `local:pinned-sdk-build-zero-warnings`, `local:non-live-tests`, and, for workflow/helper changes, `local:pr-process-tests`. Required local evidence must pass; the build records SDK 10.0.400, zero errors and zero warnings. An author cannot waive these prerequisites with a `required: false` field or an explained blocked result. Post-review integration/container CI is a separate stage and does not become a prerequisite to start itself. Draft preflight still permits honest failing or blocked preparation.

Keep CSS/theme/offline-gateway, Key Vault, logging, hardware, contract and curated project-history rules from [project guidance](../AGENT_PROJECT_GUIDANCE.md). The exporter remains disabled in production and retired collector apps are not deployment targets.

## Pilot adoption

This revision is first adopted by issue #394. The trusted workflow/evaluator and metadata controller become active after adoption on main. During the adoption PR, the owner performs state writes manually; bounded preflight and local adversarial/controller fixtures exercise the proposed rules. The initial candidate cannot make itself an approval authority. Full live trusted PR CI therefore awaits adoption; record that activation boundary rather than claiming bootstrap CI is fully qualified or bypassing review.

The original CI requires an `hvo-website` self-hosted Linux x64 runner; GitHub reported none when this pilot started. Any hosted-runner migration must preserve the standard checks and be stated in the reviewed PR. Repository-required-check configuration is a separate GitHub setting and must be recorded when changed; committed policy alone does not activate protection.

The pilot moves standard CI to hosted Ubuntu, uses ephemeral Docker smoke resources and installs Chromium's required system dependencies. All existing application/integration/deployment-policy checks remain. The trusted `Review Evidence` status and standard `build-and-test` / `docker-smoke` checks, plus required conversation resolution and an up-to-date candidate, are the intended main-branch protection profile after adoption. Required native approval count can remain zero for attributed same-account independent sessions. Do not claim these repository settings are active until their GitHub state is verified.

PR CI uses a read-only `pull_request_target` workflow whose admission evaluator comes from the immutable trusted target tip. Standard jobs then check out the verified immutable GitHub merge SHA, with no write credentials or secret environment. The controller accepts only the matching PR/head/target/merge tuple, a fresh complete attempt created after approval, and successful gate/build/smoke jobs. It publishes candidate-head statuses for the intended required checks; native target-workflow checks alone do not establish candidate readiness. Partial or old-target reruns cannot qualify. An unchanged-source infrastructure recovery uses one fresh complete run, not a partial rerun of old admission.

The repository requires Actions to be pinned to full commit SHAs. Keep readable version comments beside the immutable references and verify each replacement against its official Action repository. Tagged references alone fail job preparation even if workflow YAML is valid.

`src/HVO.Database` is an archive of SQL stored procedures and table definitions, not an active build/deployment project. The owner confirmed its exclusion during this pilot. Keep `HVO.Database.sqlproj` outside `HVO.WebSite.sln` and active project references; retain the files for historical reference and direct file-reading tests. EF Core migrations in `HVO.DataModels` remain the schema source of truth. The active solution still requires zero warnings and zero errors; do not suppress warnings or repair archive schema to satisfy its build.

Initial exact-SDK 10.0.400 Linux ARM64 validation accidentally included that archived SQL project and reported 778 SQL warnings with zero errors. Missing jq/Chromium caused environment-only test failures that passed after installing those dependencies. Revalidate the active solution after exclusion and report actual results; the historical run does not establish current acceptance.
