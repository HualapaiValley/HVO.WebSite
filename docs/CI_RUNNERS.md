# CI Runners

Current CI uses GitHub-hosted `ubuntu-latest` jobs in
[ci.yml](../.github/workflows/ci.yml). It validates source and disposable fixtures;
it does not deploy the website or gateways. Follow the
[repository profile](development/repository-profile.md) and
[selective CI reference](development/selective-ci.md) for admission, source
binding, lane ownership and measured qualification.

## Admission and execution

Draft PR automation performs bounded preflight. Local full-solution exact-SDK
build/non-live validation remains required. Standard PR CI begins after completed
independent current-source review, verified findings and resolved actionable threads.

PR CI uses a read-only `pull_request_target` workflow. The review gate, planner
and aggregators execute from the immutable trusted target tip; application jobs
check out the verified immutable GitHub merge SHA with persisted credentials
disabled. The controller requires the current PR/head/base/merge tuple, a fresh
complete first attempt after approval, and successful `review-evidence`, `plan`,
`build-and-test` and `docker-smoke` jobs. Target-event run `head_sha` alone does
not identify the tested checkout. Failed or changed source returns to draft;
old-target and partial reruns cannot establish readiness.

#411 is merged through [#426](https://github.com/HualapaiValley/HVO.WebSite/pull/426)
and adopted on main at `4528667e69f8f88a75a4978792f980542dbdf044`.
Dependency/input graphs select whole assemblies, provisioned lanes and images;
stable aggregators validate selected obligations and explicit empty reasons.
Main, nightly and default manual runs remain full; `ci:full` broadens a PR plan.
See [testing](development/testing.md) for prerequisites and
[hosted measurements](development/selective-ci.md#hosted-measurements-2026-10-04)
for timings. No caching implementation or measured cache gain is claimed.

## Docker and browser prerequisites

Hosted jobs install the pinned SDK and lane-specific dependencies. Browser jobs
install Chromium plus its system dependencies. SQL and HA lanes own their
disposable resources; absence of a required fixture is a failure, not a skip.

[docker-build-smoke.sh](../tools/docker-build-smoke.sh) defaults to ephemeral
mode and removes its run-unique images, pruning the selected daemon's BuildKit
cache. CI uses ephemeral mode on a disposable hosted runner. Do not run that
broad cache-pruning mode against a shared or production daemon.
The optional `DOCKER_SMOKE_CACHE_MODE=persistent` and 30 GB ceiling support an
authorized persistent developer runner; that option is not current hosted CI
policy and does not demonstrate a performance improvement.

Inspect runs through the repository's Actions view or the read-only API:

```bash
gh run list --repo HualapaiValley/HVO.WebSite --workflow ci.yml --limit 10
gh run view <run-id> --repo HualapaiValley/HVO.WebSite
```

Repository-required checks and conversation protection are separate GitHub
settings. This guide does not attest runner registration or enable protection;
the profile records the dated verified protection state.

## Historical private-runner inventory

The earlier guide at source `8a27b3f512ce8ef6425b93b6c7f52dea16df7bcb`
described two repository-scoped runners on `github-runner`: `github-runner-04`
and `github-runner-05`, services
`actions.runner.RoySalisbury-HVO.WebSite.github-runner-04.service` and
`actions.runner.RoySalisbury-HVO.WebSite.github-runner-05.service`, with
`self-hosted`, `Linux`, `X64`, `hvo-website`, `ubuntu-24.04`, `dotnet`, `docker`
labels. It described persistent NuGet/Playwright caches and BuildKit's 30 GB
ceiling. The original observation date is not recorded. This inventory is
superseded history; it has not been reverified and is not a current registration
or storage assertion. Hosted adoption and failure evidence remain in the
profile. Do not operate historical hosts as part of routine issue validation.
