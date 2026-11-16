# WebSite pilot adoption profile

Policy version: `2026-11-16.1`. Status: local preparation; no live rollout or
autonomous merge enabled. Canonical repository: `HualapaiValley/HVO.WebSite`.
Target: `main`. Use squash for feature merges; the remote currently permits all
three merge methods. The [existing repository profile](../repository-profile.md),
[project guidance](../../AGENT_PROJECT_GUIDANCE.md), [CSS governance](../../CSS_GOVERNANCE.md)
and [automation evidence](../automation-evidence.md) retain their requirements.

Read-only baseline on 16 November 2026: main was
`add1adc273ab0b3413acdd2541f499c427125af1`; branch protection was disabled and the
ruleset list was empty. This is a snapshot, not a continuing guarantee. Re-query
before future work. Open PRs #477–480 and #485 were left untouched; #434, #437 and
#452 retained Roy's existing claims. Unassigned issues are not proof of availability.
No suitably low-risk unclaimed pilot issue was selected or created in this task.

## Scope and discovery

The common policy is a reviewable candidate for a single-host, low-risk WebSite
pilot. Existing lifecycle skills continue to define current operations; the new
contract cannot authorize active work to bypass them. Before an operational pilot,
reconcile differences and qualify trusted enforcement. Treat policy/enforcement
changes themselves as high risk, even though the pilot workload will be low risk.

Load [common policy](common.md), the existing issue/review/PR skills and
[coordination](../../../.agents/skills/hvo-coordination/SKILL.md). Thin
[harness and Kepler adapters](adapters.md) add discovery and transport only.
The [manifest](manifest.json) pins the new common/adoption/adapter/coordination
snapshot; existing WebSite skills remain repository-versioned dependencies.

## Preserved validation and boundaries

Use exactly the SDK in `global.json` (10.0.402, roll-forward disabled). Required
local evidence remains a zero-warning/error build and full applicable non-live
solution validation, even for a selected-CI PR. Use the commands in the existing
profile and [testing guide](../testing.md). Workflow/helper changes additionally
require their focused process/planner/runner/syntax checks. Record missing evidence
as blocked; this document grants no documentation-only admission exemption.

Preserve source-bound evidence IDs, trusted target evaluator/controller, immutable
merge-source execution, selected execution unions, main/full lanes and full-SHA
Action pins. Infrastructure recovery retains WebSite's stricter one fresh complete
run budget. No workflow, schema, gate producer or CI selection changes belong to
this preparation. Production exporter stays disabled; retired collectors are not
deployment targets. Archived `src/HVO.Database` stays outside the solution;
`HVO.DataModels` migrations remain schema authority. Never operate physical devices
or use live tests as ordinary validation.

## Evidence translation and adoption gaps

Keep WebSite's `hvo-pr-process` version 1 format unchanged. Common CLEAN maps to
WebSite APPROVE only when coverage is complete and all mandatory conditions pass;
CHANGES_REQUIRED and INCOMPLETE map directly. BLOCKED is non-approval: publish
INCOMPLETE with the blocker, never invent an unsupported machine verdict.
Mechanical/Standard/Deep map to low/standard/high coverage respectively, while
reviewer count and actual model separation remain independent requirements.

The current author record can request `minReviewers`, `differentModel`, panel
selectors and `modelComparison: all-contributors`. Those declarations do not prove
the common risk minimum is enforced by trusted code. Before activation, derive
minimum risk/count from trusted policy and changed scope; validate reviewer family
provenance separately from model-string inequality. Existing publisher permission
checks do not attest execution or review quality. Keep the trusted schema/producer
unchanged here and qualify its successor separately.

## Proposed minimal protection and enforcement — not activated

Use a main-branch protection/ruleset requiring PRs, conversation resolution,
up-to-date candidates, and exactly the existing candidate-head contexts:
`Review Evidence`, `build-and-test`, `docker-smoke`. Disable force pushes and
deletions; enforce for administrators with no routine bypass actors. Native approval
count may stay zero only with a qualified independent-session evidence gate.
Do not require target-event native checks as substitutes for the controller's
candidate-head statuses. No merge queue or CI optimization is proposed here.

Designate a trusted publisher identity before activation. Bind required contexts
to that GitHub App where supported and verify that wrong-app publications cannot
satisfy them. Existing shared credentials/status provenance are insufficient proof
of independent review. The trusted target-side evaluator must authenticate evidence,
reject implementer-as-reviewer/finalizer, enforce risk minimums and actual family
separation, and bind acceptance/reviews/checks to repository, PR, target, raw head
and verified merge SHA. Candidate code must never control its own admission.
Preserve `hvo/reviewed-head` on repositories already requiring it; WebSite has its
own existing `Review Evidence` context and must not rename it during this pilot.

Before merge, an authorized finalizer re-reads source, ownership, threads, trusted
verdict and complete successful run; requests squash with expected raw head SHA;
then observes actual merged SHA and required post-merge results. A moving target
invalidates source evidence and requires requalification. GitHub's required
up-to-date check plus trusted tuple validation must close target-race gaps; do not
claim a head-only API precondition protects the target. Test this race before use.

Qualification must prove rejection of stale head/target/merge, self-review, missing
reviewer/family evidence, wrong publisher, forged/edited records, failed/partial/old
CI, unresolved threads and unauthorized direct pushes. Also prove a positive
independent exact-source candidate and post-merge observation in an authorized
disposable fixture. Existing unit fixtures alone do not prove GitHub enforcement.
Protection changes, publisher credentials and live qualification require a separate
authorized step; until then autonomous merge is BLOCKED.

## Rollback and remaining acceptance

Before activation, snapshot protections, trusted producer revision, policy version,
active ownership and runs. Stop new dispatch on failure; reconcile in-flight work;
restore only the recorded approved configuration and verify protections. Retain
evidence; never replay uncertain operations or delete unrelated worktrees.

Still required: independent high-risk policy review, full source-bound local gates,
chosen low-risk assignment, actual harness/model/full-access qualification, durable
coordinator implementation and recovery tests, trusted enforcement implementation
and protected-branch qualification. No launch, recovery, merge or deployment success
is claimed by this document.
