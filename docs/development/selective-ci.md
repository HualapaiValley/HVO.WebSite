# Selective CI

Issue #411 introduces dependency-aware PR validation. [PR #426](https://github.com/HualapaiValley/HVO.WebSite/pull/426) passed the previous trusted full CI after independent Deep review and was adopted on main at `4528667e69f8f88a75a4978792f980542dbdf044`. The first full main run and fixed measurement profiles are recorded below. The SQL-provider fixture is qualified on main after [PR #421](https://github.com/HualapaiValley/HVO.WebSite/pull/421), as recorded in [SQL fixture qualification](#sql-fixture-qualification). #408 browser migration remains pending. The measurements describe the original adoption snapshot, not those later changes, and cannot authorize a PR's admission.

## Planning and ownership

For PRs, trusted code from the immutable target tip reads the complete Git diff from base to the verified synthetic merge. It checks the merge's base/head parents and reads solution, project and test sources as data. Planning never invokes candidate MSBuild, imports or scripts. Missing Git objects, truncated/oversized input and malformed source fail visibly.

The planner reads both old and new project graphs. It unions their reverse ProjectReference consumers so deleting or changing a reference cannot erase an old consumer, then adds the candidate's forward dependencies needed to build the selected roots. Renames consider both paths; missing active projects and cyclic candidate references fail. The roots must cover every affected build project. Changed SDK, solution membership, central packages/build props/targets, NuGet configuration, runsettings and CI/planner/controller inputs request full validation. Unsupported reference evaluation and unknown paths conservatively request full validation. When reference evaluation is unsupported, every active project is an explicit build root: a conditional edge cannot prove that another build will include its target. Unsupported test identity, source selection or category metadata stops planning instead of silently producing an incomplete category union.

Project folders are only one ownership input. Literal copied/read Content, None, Compile, AdditionalFiles and EmbeddedResource paths and globs belong to their consuming projects. Archived src/HVO.Database SQL belongs to Website.UnitTests. Home Assistant configuration/environment inputs also belong to the HA assemblies, Website.UnitTests and Playwright. Website, shared themes and ThemeSandbox explicitly require Playwright coverage even where project references do not express that browser dependency. Deployment and operational inputs broaden validation conservatively.

Test discovery includes tracked C# files linked by literal Compile includes and globs. A literal Microsoft.NET.Test.Sdk or MSTest package also identifies a test project when IsTestProject is omitted; package IDs are matched without regard to case. Ambiguous package identity, conflicting identity, unsupported SDK/import/target evaluation, modified Compile items and custom default-source selection fail visibly. Shared Directory.Build.props/targets are checked for unsupported source selection too. Build metadata and project sources/inputs must be regular tracked files: symlinks within project directories or reachable by declared inputs fail because blob text is not their checked-out content. Unrelated agent-skill links are permitted; submodules are unsupported. This reader deliberately does not evaluate arbitrary MSBuild logic.

Only explicitly known non-executable documentation may produce empty build/test/image lanes; copied/read inputs take precedence over a documentation-looking extension. Test-only changes select their owning assemblies. Mixed changes use the union. Application image selection uses affected application consumers, not every application that happens to consume a selected forward dependency.

The retained ci-plan.json includes planner version, full head/base/merge tuple, complete changes/inputs, affected projects, Debug/Release roots, forward dependencies, test projects/lanes, six possible image IDs, operations, selection reasons, explicit empty reasons and a content digest. Executors reject a stale tuple, changed digest or checkout that differs from the admitted merge.

## Execution and aggregation

The bounded jobs run independent validation, HA, SQL, browser, operational and Docker work. Selected roots are locked-restored and built before tests; test execution reuses preparation with --no-build --no-restore. Debug validation runs fast and simulator partitions plus inexpensive policy checks; selected non-test roots also receive Release builds. HA assemblies share one sequential disposable stack. The browser job installs Chromium only for selected browser work. The #409 owned API SQL fixture is qualified locally and in hosted CI. The SQL job runs the eight runner-safety checks before provisioning, discovers the actual integration category, and independently verifies the resulting reports. #408 browser migration remains pending.

tools/ci-run.mjs executes a validated plan supplied through CI_PLAN with CI_HEAD, CI_BASE and CI_MERGE. Its jobs are validation, home-assistant, sql-server, browser, operations, docker-smoke and aggregate. It does not derive obligations or admit a PR. Use the [test-lane commands](testing.md) for full local validation; hand-written environment JSON is not trusted hosted admission evidence.

For HA and SQL, the trusted executor clears each selected project's owned report directory immediately before invoking the candidate fixture helper. After it returns, the trusted report verifier independently requires fresh, nonempty, passing reports for every planned project. A successful helper exit alone cannot satisfy the lane.

The HA helper's no-argument mode preserves the existing trusted workflow's complete simulator-plus-HA obligation during adoption. Planned execution explicitly requests --ha-only --prebuilt; simulators run in validation. Only the legacy whole-solution simulator report check permits all-zero reports from assemblies without matching tests, while requiring actual passing tests overall. Selected project reports remain strict.

The stable build-and-test job uses trusted aggregation to require success from every planned execution job. Missing, failed, cancelled or unexpectedly skipped work fails aggregation. An unplanned lane must be skipped and carry an explicit empty reason. Validation still runs inexpensive policy checks for documentation-only plans. Docker-smoke always verifies the image selection and cleanup fixtures, then passes the explicit image IDs to the Docker runner; an empty verified image selection performs no actual Docker build or pruning.

The metadata controller requires four successful internal jobs: review-evidence, plan, build-and-test and docker-smoke. It also retains exact source-tuple binding, review admission, a fresh complete first attempt created after approval and immutable candidate checkout. Stable candidate-head statuses remain build-and-test and docker-smoke. A benchmark or a successful native target-workflow status cannot substitute for qualified PR admission.

## Full runs and benchmarks

Main pushes, nightly snapshots and the default manual full profile use complete validation. A ci:full PR label broadens selection to a full plan; set it before starting a fresh complete run. It does not waive review, source checks or unsupported-metadata failures.

Manual workflow profiles run only on trusted main. The fixed jkbms, website and shared profiles model a JK leaf, website and shared-contract input respectively; the full profile validates the complete snapshot. These profiles measure execution against one trusted main SHA, with equal head/base/merge values. They do not create a PR candidate, exercise a new PR admission or prove omitted work is safe for a real changed source.

Invoke a fixed measurement profile with `gh workflow run ci.yml --ref main -f profile=jkbms`, substituting website or shared as needed. Omitting profile, or selecting full, requests the full snapshot. Run these profiles sequentially: they share main's workflow concurrency group, so overlapping dispatches cancel earlier runs. Retain the resulting run URL and immutable plan with the measurements.

Report wall elapsed time as the earliest executed job start to the latest executed job end, excluding the initial queue. This includes gaps between dependent jobs; those gaps are not runner duration. Report summed executed-job duration separately. Parallel job durations overlap and must not be added to claim PR elapsed time. Compare the same validation obligations and explain omitted jobs from the dependency/input closure. Record failures and unavailable lanes rather than inventing a speed table.

### Hosted measurements: 2026-10-04

The before sample is the last full main run at `150607c6ccfa4aaf7ef7e354ea893cdd6e2b39b0`. Every after sample uses the same adopted snapshot, `4528667e69f8f88a75a4978792f980542dbdf044`, with identical head/base/merge values in its version-1 plan. All are hosted Ubuntu runs with coverage enabled. The fixed profiles model input paths against that snapshot; no application change was made between profiles.

| Sample / retained run | Wall time | Summed runner time | Actual test results |
|---|---:|---:|---|
| [Before: full main](https://github.com/HualapaiValley/HVO.WebSite/actions/runs/37178384532) | 9m26s (566s) | 13m31s (811s) | 1,066 passed; one legacy ignored |
| [After: full main](https://github.com/HualapaiValley/HVO.WebSite/actions/runs/37180483240) | 4m54s (294s) | 15m57s (957s) | 1,066 passed; one legacy ignored |
| [After: JK leaf](https://github.com/HualapaiValley/HVO.WebSite/actions/runs/37180766829) | 1m22s (82s) | 2m11s (131s) | 142 passed |
| [After: website](https://github.com/HualapaiValley/HVO.WebSite/actions/runs/37180864706) | 2m59s (179s) | 4m58s (298s) | 324 passed; one legacy ignored |
| [After: shared contracts](https://github.com/HualapaiValley/HVO.WebSite/actions/runs/37181033514) | 5m28s (328s) | 16m15s (975s) | 1,066 passed; one legacy ignored |

The old workflow ran the full solution and all images for these input types, so its full run is the observed before reference for each selected profile. There are no separately measured old leaf/website/shared runs and no paired real-PR comparison. These are single observations, not repeated trials or a performance guarantee. Full validation gave faster feedback while consuming more summed runner time; selection reduces the work for narrow inputs. The shared sample was slower than the after-full sample despite omitting operational work: its Docker job took 317s versus 255s. One hosted observation cannot attribute that variation to the planner or establish a stable speedup.

| Profile | Modeled input and required closure | Work omitted with a verified reason |
|---|---|---|
| JK leaf | `src/HVO.Hardware.JkBms/Program.cs`; JK Debug test root and Release application root, their forward dependencies, JK Fast/simulator tests and the `jkbms` image | Other applications are not reverse consumers of JK. Its test assembly owns no HA, SQL or browser cases; no operational input changed. Shared dependencies are built without selecting every unrelated application that also references them. |
| Website | `src/HVO.WebSite.v9/Program.cs`; API/Unit/Playwright roots, website Release build and forward dependencies, Fast/browser cases and the `website` image | Gateways/exporter are not reverse consumers of the website. The affected test assemblies own no simulator/HA/SQL cases at this snapshot; no operational input changed. Browser coverage is explicitly owned at this snapshot, before the later owned-fixture project references. |
| Shared contracts | `src/HVO.Edge.Contracts/Contract.cs`; old/new reverse consumers plus forward dependencies, all 13 test assemblies across Fast/simulator/HA/browser and all six images | Unrelated standalone tools and ThemeSandbox are not affected build roots. The baseline browser executor still explicitly builds ThemeSandbox as a fixture. No operational input changed. SQL ownership is empty before #409. |
| Full main | Every active build project, every non-live test lane present in the snapshot, all six images and operational checks | SQL ownership is empty before #409; Live tests remain opt-in. |

The shared path is a fixed modeled input name, not a claim that a file with that name changed in Git. The plans retain their complete affected-project and build-dependency lists, roots, per-lane assemblies and empty reasons. Inexpensive policy checks still execute for every profile. Full main includes the operational checks omitted from all three fixed profiles.

The before and after full TRX artifacts both contain 1,066 actual passes. Before retained 29 reports, including eleven zero-match simulator reports from the legacy solution-wide invocation. After retained 18 reports: 1,048 validation passes, four HA passes and fourteen browser passes. Selected reports must have actual passing tests; they do not use the legacy empty-report allowance. The one ignored browser scaffold is explicitly tracked by #408 and is never counted as acceptance. SQL and migrated website/browser tests are not claimed by this adoption snapshot; qualify those on their own merged source.

<details>
<summary>Reproduction and retained plan identities</summary>

Each linked after run retains `ci-plan-<run-id>` and the selected `test-results-<lane>-<run-id>` artifacts. The before run retains `test-results-37178384532`. Test counts above come from individual TRX `UnitTestResult` outcomes, not a command exit or filter count. The workflow's other Node/Python/shell checks are additional and are not included in these .NET test totals.

Use `gh run view <run-id> --json jobs` to retrieve job timestamps. Exclude skipped jobs; subtract each executed job's start from its completion and sum those durations for runner time. Wall time spans the earliest executed-job start through the latest completion. Verify the run/source and every executed job's success before reporting a qualifying result. The initial GitHub queue and billing multipliers are outside these measurements.

| Profile / run | Plan digest |
|---|---|
| Full / 37180483240 | `5123fa13d2eb558111797457429eada84d5e8c363dba053039f5847fd7421889` |
| JK / 37180766829 | `3956d411cb169f816d841677e826aeb296d41c90f4dcc0556bff62f4cd8ba3a5` |
| Website / 37180864706 | `3b5713b5a1a04475923cd862035d8eb8f06b39fbb082d8e10904c6547c0e5e7b` |
| Shared / 37181033514 | `631de3b6b505e6a8a272e8172807aac00d0328db9f53f491f394fb426e6b774d` |

The reviewed implementation head was `3fd418a6801ab9948264f144db4bf3a671d8b971`; its [qualified pre-adoption PR CI](https://github.com/HualapaiValley/HVO.WebSite/actions/runs/37179975304) tested merge `f5774730d968901e21316c86170e114d3940f6de` against base `150607c6ccfa4aaf7ef7e354ea893cdd6e2b39b0`. That fresh run and the [independent correction approval](https://github.com/HualapaiValley/HVO.WebSite/pull/426#issuecomment-5976923639) establish the implementation's admission under the old trusted policy. The fixed profiles above do not replace PR review or admission evidence.

</details>

No dependency or Docker caching change is implemented here, and no cache benefit is claimed. Evaluate lock/SDK-keyed caching only after selection works, with separately measured evidence and without PR cache-write credentials.

The last inspected main protection endpoint returned 404 and rulesets were empty. The intended checks are process requirements enforced through review/controller coordination; committed documentation does not activate GitHub branch protection. Do not claim repository protection is active without a new verified settings inspection.

## SQL fixture qualification

[PR #421](https://github.com/HualapaiValley/HVO.WebSite/pull/421) integrated the production-provider fixture at `31297abd3d2156ebf1280418fb86082e6dc38a85`. Its [first full main CI run](https://github.com/HualapaiValley/HVO.WebSite/actions/runs/37182172545) passed every planned lane and Docker smoke. Downloaded reports contain 1,084 passed tests, zero failures and one existing ignored browser scaffold across 19 TRX files; this later source includes both the SQL fixture and the chart tests from #406.

The [SQL job](https://github.com/HualapaiValley/HVO.WebSite/actions/runs/37182172545/job/111376792359) ran all eight runner-safety checks successfully before provisioning, then passed all eight actual SQL Server tests with zero skips and a retained qualifying report. This verifies the newly adopted executor step on trusted main; premerge CI used the older trusted executor. [Issue #409 closeout](https://github.com/HualapaiValley/HVO.WebSite/issues/409#issuecomment-5977278554) records migration, query, fault and ownership acceptance. The original timing samples above remain tied to their earlier `4528667e` source; this later qualification is not a replacement benchmark.

## Browser fixture qualification

Final-main qualification for #408 is pending. Its [combined development checkpoint](https://github.com/HualapaiValley/HVO.WebSite/issues/408#issuecomment-5977665435) at `7e46e33d953bb5658eb4a0e28fbeac8054a2ab52` passed the pinned build with zero warnings/errors, 1,348 Fast tests and 58 owned browser cases, with zero failures or ignored tests. This source includes the prepared website, observation-identity and history changes; it is not a merged main snapshot.

The browser run used the planner and executor from immutable main `b9c12f0a995963c0203cf55a13dea5fd99b86193`. Only its Browser job was executed against the development snapshot. The candidate's strict report verifier separately accepted all 58 results without the legacy scaffold allowance. [Independent preparatory review](https://github.com/HualapaiValley/HVO.WebSite/issues/408#issuecomment-5977695783) also passed all 58 browser cases, including the missing-script and blocked-handler negative controls, and verified that the SQL/browser runner test union retains SQL safety checks. Neither this development run nor its review establishes hosted PR admission or activation of the new executor on main.

Final acceptance requires issue-only adoption after the website/history prerequisites, current-source independent review and PR CI, then a full main run that exercises the newly trusted browser executor and retains every planned lane's results. Record that merged source and hosted run here before closing #411. These later fixture checks do not replace or rebind the original timing samples above.
