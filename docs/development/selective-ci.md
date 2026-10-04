# Selective CI

Issue #411 introduces dependency-aware PR validation. The implementation is awaiting full local validation, independent Deep review, adoption on main and hosted measurements. Its first PR must pass the existing trusted full CI; candidate workflow changes cannot authorize their own admission. #408 browser migration and #409 SQL-provider integration also need their own qualification.

## Planning and ownership

For PRs, trusted code from the immutable target tip reads the complete Git diff from base to the verified synthetic merge. It checks the merge's base/head parents and reads solution, project and test sources as data. Planning never invokes candidate MSBuild, imports or scripts. Missing Git objects, truncated/oversized input and malformed source fail visibly.

The planner reads both old and new project graphs. It unions their reverse ProjectReference consumers so deleting or changing a reference cannot erase an old consumer, then adds the candidate's forward dependencies needed to build the selected roots. Renames consider both paths; missing active projects fail. Changed SDK, solution membership, central packages/build props/targets, NuGet configuration, runsettings and CI/planner/controller inputs request full validation. Unsupported project evaluation and unknown paths conservatively request full validation. Unsupported test metadata stops planning instead of silently producing an incomplete category union.

Project folders are only one ownership input. Literal copied/read Content, None, Compile, AdditionalFiles and EmbeddedResource paths and globs belong to their consuming projects. Archived src/HVO.Database SQL belongs to Website.UnitTests. Home Assistant configuration/environment inputs also belong to the HA assemblies, Website.UnitTests and Playwright. Website, shared themes and ThemeSandbox explicitly require Playwright coverage even where project references do not express that browser dependency. Deployment and operational inputs broaden validation conservatively.

Only explicitly known non-executable documentation may produce empty build/test/image lanes; copied/read inputs take precedence over a documentation-looking extension. Test-only changes select their owning assemblies. Mixed changes use the union. Application image selection uses affected application consumers, not every application that happens to consume a selected forward dependency.

The retained ci-plan.json includes planner version, full head/base/merge tuple, complete changes/inputs, affected projects, Debug/Release roots, forward dependencies, test projects/lanes, six possible image IDs, operations, selection reasons, explicit empty reasons and a content digest. Executors reject a stale tuple, changed digest or checkout that differs from the admitted merge.

## Execution and aggregation

The bounded jobs run independent validation, HA, SQL, browser, operational and Docker work. Selected roots are locked-restored and built before tests; test execution reuses preparation with --no-build --no-restore. Debug validation runs fast and simulator partitions plus inexpensive policy checks; selected non-test roots also receive Release builds. HA assemblies share one sequential disposable stack. The browser job installs Chromium only for selected browser work. SQL ownership is reserved for the #409 API provider fixture; its integration remains pending.

tools/ci-run.mjs executes a validated plan supplied through CI_PLAN with CI_HEAD, CI_BASE and CI_MERGE. Its jobs are validation, home-assistant, sql-server, browser, operations, docker-smoke and aggregate. It does not derive obligations or admit a PR. Use the [test-lane commands](testing.md) for full local validation; hand-written environment JSON is not trusted hosted admission evidence.

The stable build-and-test job uses trusted aggregation to require success from every planned execution job. Missing, failed, cancelled or unexpectedly skipped work fails aggregation. An unplanned lane must be skipped and carry an explicit empty reason. Validation still runs inexpensive policy checks for documentation-only plans. Docker-smoke always verifies the image selection and cleanup fixtures, then passes the explicit image IDs to the Docker runner; an empty verified image selection performs no actual Docker build or pruning.

The metadata controller requires four successful internal jobs: review-evidence, plan, build-and-test and docker-smoke. It also retains exact source-tuple binding, review admission, a fresh complete first attempt created after approval and immutable candidate checkout. Stable candidate-head statuses remain build-and-test and docker-smoke. A benchmark or a successful native target-workflow status cannot substitute for qualified PR admission.

## Full runs and benchmarks

Main pushes, nightly snapshots and the default manual full profile use complete validation. A ci:full PR label broadens selection to a full plan; set it before starting a fresh complete run. It does not waive review, source checks or unsupported-metadata failures.

Manual workflow profiles run only on trusted main. The fixed jkbms, website and shared profiles model a JK leaf, website and shared-contract input respectively; the full profile validates the complete snapshot. These profiles measure execution against one trusted main SHA, with equal head/base/merge values. They do not create a PR candidate, exercise a new PR admission or prove omitted work is safe for a real changed source.

After adoption, invoke a fixed measurement profile with `gh workflow run ci.yml --ref main -f profile=jkbms`, substituting website or shared as needed. Omitting profile, or selecting full, requests the full snapshot. Retain the resulting run URL and immutable plan with the measurements.

Hosted before/after measurements are pending. Retain the plan and source tuple for representative leaf, website, shared and full runs. Report wall elapsed time as the earliest job start to the latest job end, excluding queue time, and report summed runner duration separately in minutes. Parallel job durations overlap and must not be added to claim PR elapsed time. Compare the same validation obligations and explain omitted jobs from the dependency/input closure. Record failures and unavailable lanes rather than inventing a speed table.

No dependency or Docker caching change is implemented here, and no cache benefit is claimed. Evaluate lock/SDK-keyed caching only after selection works, with separately measured evidence and without PR cache-write credentials.

The last inspected main protection endpoint returned 404 and rulesets were empty. The intended checks are process requirements enforced through review/controller coordination; committed documentation does not activate GitHub branch protection. Do not claim repository protection is active without a new verified settings inspection.
