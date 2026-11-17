# WebSite pilot adoption profile

Policy version: `2026-11-16.11`. Status: local preparation; no live rollout or
autonomous merge enabled. Canonical repository: `HualapaiValley/HVO.WebSite`.
Target: `main`. Use squash for feature merges. The [existing repository profile](../repository-profile.md),
[project guidance](../../AGENT_PROJECT_GUIDANCE.md), [CSS governance](../../CSS_GOVERNANCE.md)
and [automation evidence](../automation-evidence.md) retain their requirements.

The dated [repository baseline](../../PROJECT_HISTORY.md#unified-policy-baseline)
records observed branch/protection/merge settings and active work. Re-query those
facts before future work; unassigned issues are not proof of availability.

## Scope and discovery

The [common policy](common.md#scope) defines pilot work and policy text. Policy
text or enforcement changes are High risk. The existing review-format coverage and
publication requirements remain in force.

Load [common policy](common.md), the existing issue/review/PR skills and
[coordination](../../../.agents/skills/hvo-coordination/SKILL.md). Thin
[harness and Kepler adapters](adapters.md) add discovery and transport only.
The [manifest](manifest.json) pins the portable common/adapter/coordination
and README snapshot and names this repository adoption profile; existing WebSite skills remain repository-versioned dependencies.

See [common policy precedence](common.md#authority-and-precedence); automatic
discovery in each launch context is not verified.

## Repository-specific scope and discovery

This repository is the canonical common-policy source for the local WebSite pilot;
other repositories have not adopted it. The canonical source location is recorded
in the manifest. The six folded common rules apply to all WebSite agent work;
other existing procedures continue subject to those rules.
For WebSite work, repository-local skills govern over legacy global hvo-* skills;
global skills are not substitutes and do not authorize dispatch or installation.

Policy text includes AGENTS.md, CLAUDE.md, docs/development/policy/**,
docs/development/repository-profile.md, docs/development/PROCESS.md,
docs/development/automation-evidence.md, CONTRIBUTING.md,
.github/PULL_REQUEST_TEMPLATE.md, .agents/skills/**, .opencode/**, opencode.json
and Copilot instructions where they carry workflow rules. Enforcement examples
include .github/workflows/**, tools/pr-process*, tools/ci-plan* and repository settings.

Existing CLAUDE.md routes to AGENTS.md; verify local skill-link resolution and
explicitly load canonical `.agents/skills`. The `.claude/skills` link is part of
that local skill discovery. Existing opencode.json loads AGENTS.md and Copilot
guidance; existing `.opencode` wrappers remain adapters to canonical procedures.
Existing `.github/copilot-instructions.md` routes to AGENTS.md and the same local
skills. No repository Cursor rules/discovery adapter is installed or qualified.
The hvo-epic-work SSH-to-T3-Code route remains the existing procedure for non-pilot
epic work. Parked tooling at `parking/website-policy-pilot-tooling-20261116`
(2513350) is not a pilot requirement.

## Draft and gate facts

The draft checks required before review are the latest PR Preflight `simple-checks`
run and PR Process `metadata` job on the exact head. For `dependabot[bot]` PRs,
`metadata` is skipped by design; the draft check is the latest `simple-checks`
run on the exact head, with the required title and body format. `Review Evidence`,
`build-and-test` and `docker-smoke` deliberately fail on drafts and are not draft
checks. Preflight requires a closing keyword for the issue in the PR title: a
partial slice closes its child with `Closes #child` and references its parent with
`Refs #parent`. The gate requires zero-warning build and non-live evidence, counts
only first-attempt CI admission, and its v1 schema has no BLOCKED verdict.

## Transitional retained procedures

Each item below stays in force until Roy records a decision to retire it or promote it to common policy, or until the next policy release, whichever comes first; none relaxes common policy. "Next policy release" means the next shared-policy version bump. Status: retained, remains in force until Roy records a decision or until the next policy release, whichever comes first.

- Review-format coverage, publication and Deep-trigger mapping remain required, subject to the common tier and Kepler relay rules.
- Repository-local skill precedence and non-pilot epic dispatch route remain in force.
- Never operate physical devices or use live tests as ordinary validation.
- Infrastructure recovery permits one fresh complete run for unchanged source, a stricter limit than common's default two retries.
- Autonomous enforcement requires trusted enforcement and authenticated family qualification, including the mapping and adversarial checks below.
- Protection or ruleset changes require fixture qualification, authenticated baseline and impact evidence, and a verified rollback plan; stop for unknown baseline or concurrent change.
- Requalify a moving merge source before authorized merge; verify source, review, checks and resolved findings.
- Preserve source-bound evidence, trusted evaluator/controller, immutable merge-source execution, execution unions, full lanes and full-SHA Action pins; workflow/helper changes require focused checks.
- The first pilot stops at PR-ready; Roy separately authorizes its first merge. Stage (c) qualification retains the additional requirements below.

## Pilot stages

1. **Preparation:** write and review these candidate documents; no assignment
   runs under them.
2. **Supervised trial:** one low-risk WebSite assignment explicitly authorized by
   Roy follows the procedure manually and stops at PR-ready. Use existing PR Process
   and Review Evidence checks and check the review minimum by hand at Ready. No new
   enforcement, protection or automation, including the parked tooling, is required.
   Roy separately authorizes any first merge after inspecting the evidence.
3. **Autonomous enforcement:** a later, separately authorized stage with trusted
   enforcement, authenticated model-family mapping, protection and autonomous merge.
   Its qualification requirements below are future requirements, not implemented
   functionality; they do not block a supervised trial.

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
budget as a transitional retained procedure. No
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
describe coverage. Under the accepted interim HIGH mapping, ordinary behavior/API
changes require at least one Deep review; Standard coverage alone is insufficient.
Reviewer minima follow the common contract.
Camera lifecycle is a cross-repository common-policy example, not an assertion that
WebSite operates cameras. Before .9, Standard review permitted the same model in a
distinct independent session; common policy now requires a different model from
every contributor in the reviewed range.

The current author record can request `minReviewers`, `differentModel`, panel
selectors and `modelComparison: all-contributors`. Those declarations do not prove
the common risk minimum is enforced by trusted code. For autonomous enforcement, derive
minimum risk/count from trusted policy and changed scope; validate reviewer family
provenance separately from model-string inequality. Existing publisher permission
checks do not attest execution or review quality. Keep the trusted schema/producer
unchanged here and qualify its successor separately.

Authenticated canonical model-family mapping is required for autonomous enforcement. Derive
families from authenticated runtime provider/model receipts through a trusted
mapping, never from declared family strings or caller-supplied mappings. Qualify
aliases, Unicode lookalikes/invisible characters, unmapped or ambiguous identities
and forged mappings before autonomous enforcement.

## Pilot boundary

The first WebSite pilot stops at PR-ready. Roy separately authorizes any first
merge after inspecting the evidence. WebSite branch protection is a separate later
decision. Existing PR Process workflow and `Review Evidence` checks remain unchanged.
WebSite does not currently produce or require `hvo/reviewed-head`. Keep that
compatibility gate where currently required by SkyMonitor and RoofController;
remove no live required gate before a verified replacement.

Before any WebSite protection or ruleset change, qualify in a fixture repository,
capture authenticated settings and active-PR impact, and plan rollback to a verified
baseline, stopping for an unknown baseline or concurrent change. These are also
future qualification requirements for stage (c) autonomous enforcement, but not for
stage (b). Other stage (c) requirements include a plan for protection that blocks
its own repair; runner and credential
exposure review of any app identities; measured strict up-to-date re-review cost;
live-test authorization separate from watchdog approval. The prior one-reviewer
Deep wording in [review-format.md](../../../.agents/skills/hvo-code-review/references/review-format.md)
has been reconciled with the HIGH panel. The other inputs are not complete.

Before any authorized merge, Kepler verifies the current source,
review, required checks and resolved findings under the existing PR procedure.
A moving target requires requalification. Separate authorization is required for
release publication, any deployment (including staging), and real roof or hardware operation.
