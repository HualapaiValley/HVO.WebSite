# WebSite pilot adoption profile

Policy version: `2026-11-16.6`. Status: local preparation; no live rollout or
autonomous merge enabled. Canonical repository: `HualapaiValley/HVO.WebSite`.
Target: `main`. Use squash for feature merges. The [existing repository profile](../repository-profile.md),
[project guidance](../../AGENT_PROJECT_GUIDANCE.md), [CSS governance](../../CSS_GOVERNANCE.md)
and [automation evidence](../automation-evidence.md) retain their requirements.

The dated [repository baseline](../../PROJECT_HISTORY.md#unified-policy-baseline)
records observed branch/protection/merge settings and active work. Re-query those
facts before future work; unassigned issues are not proof of availability.

## Scope and discovery

The common policy is a reviewable candidate for a single-host, low-risk WebSite
pilot. Existing lifecycle skills continue to define current operations; the new
contract cannot authorize active work to bypass them. Before an operational pilot,
qualify trusted enforcement. Treat policy/enforcement changes themselves as high
risk, even though the pilot workload will be low risk. During this candidate period,
their review requires two independent reviewers from different model families,
at least one deep review. This stricter minimum applies to this candidate and
overrides the existing review-format statement that Deep does not automatically
require multiple reviews; the skill's other coverage/publication requirements stay
in force. Neither this rule nor a passing review authorizes dispatch or activation.

Load [common policy](common.md), the existing issue/review/PR skills and
[coordination](../../../.agents/skills/hvo-coordination/SKILL.md). Thin
[harness and Kepler adapters](adapters.md) add discovery and transport only.
The [manifest](manifest.json) pins the new common/adoption/adapter/coordination
snapshot; existing WebSite skills remain repository-versioned dependencies.

AGENTS.md states local/global skill precedence. Actual discovery in each launch
context is not verified.

## Preserved validation and boundaries

Use exactly the SDK in `global.json` (10.0.402, roll-forward disabled). Required
local evidence remains a zero-warning/error build and full applicable non-live
solution validation, even for a selected-CI PR. Use the commands in the existing
profile and [testing guide](../testing.md). Workflow/helper changes additionally
require their focused process/planner/runner/syntax checks. Record missing evidence
as blocked; this document grants no documentation-only admission exemption.

Preserve source-bound evidence IDs, trusted target evaluator/controller, immutable
merge-source execution, selected execution unions, main/full lanes and full-SHA
Action pins. WebSite live admission counts only first-attempt (`run_attempt` 1) `ci.yml`
runs. Infrastructure recovery retains WebSite's stricter one fresh complete run
budget. This stricter rule wins over the common procedural retry budget. No
workflow, schema, gate producer or CI selection changes belong to
this preparation. Production exporter stays disabled; retired collectors are not
deployment targets. Archived `src/HVO.Database` stays outside the solution;
`HVO.DataModels` migrations remain schema authority. Never operate physical devices
or use live tests as ordinary validation.

## Evidence translation and adoption gaps

Keep WebSite's `hvo-pr-process` version 1 format unchanged. Common CLEAN maps to
WebSite APPROVE only when coverage is complete and all mandatory conditions pass;
CHANGES_REQUIRED and INCOMPLETE map directly. BLOCKED is non-approval: publish
INCOMPLETE with the blocker, never invent an unsupported machine verdict.
The existing profile/review-format Deep triggers (including deployment, durability
and cross-component effects) are High risk for this pilot. Mechanical/Standard/Deep
map to low/standard/high coverage, with reviewer minima from the common contract.
Camera lifecycle is a cross-repository common-policy example, not an assertion that
WebSite operates cameras. Standard review permits the same model in a distinct
independent session unless the assignment adds separation requirements.

The current author record can request `minReviewers`, `differentModel`, panel
selectors and `modelComparison: all-contributors`. Those declarations do not prove
the common risk minimum is enforced by trusted code. Before activation, derive
minimum risk/count from trusted policy and changed scope; validate reviewer family
provenance separately from model-string inequality. Existing publisher permission
checks do not attest execution or review quality. Keep the trusted schema/producer
unchanged here and qualify its successor separately.

Authenticated canonical model-family mapping is a hard activation blocker. Derive
families from authenticated runtime provider/model receipts through a trusted
mapping, never from declared family strings or caller-supplied mappings. Qualify
aliases, Unicode lookalikes/invisible characters, unmapped or ambiguous identities
and forged mappings before activation.

## Pilot boundary

The first WebSite pilot stops at PR-ready. Roy separately authorizes any first
merge after inspecting the evidence. WebSite branch protection is a separate later
decision. Existing PR Process workflow and `Review Evidence` checks remain unchanged.
WebSite does not currently produce or require `hvo/reviewed-head`. Keep that
compatibility gate where currently required by SkyMonitor and RoofController;
remove no live required gate before a verified replacement.

Inputs still needed for the later protection/activation decision include fixture-repo
testing before production; authenticated settings and active-PR impact capture;
rollback to the verified baseline, stopping for an unknown baseline or concurrent
change, and a plan for protection that blocks its own repair; runner and credential
exposure review of any app identities; measured strict up-to-date re-review cost;
live-test authorization separate from watchdog approval; and reconciliation of
[review-format.md](../../../.agents/skills/hvo-code-review/references/review-format.md)'s one-reviewer Deep wording with the
two-family High rule. These inputs are not complete.

Before any authorized merge, the lifecycle owner verifies the current source,
review, required checks and resolved findings under the existing PR procedure.
A moving target requires requalification. Separate authorization is required for
release publication, production deployment and real roof or hardware operation.
