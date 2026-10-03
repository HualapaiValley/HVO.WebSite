# Shared issue and PR process

Revision `2026-10-03.1`, initially piloted in WebSite. Shared procedure lives in the repository-local `.agents/skills` packages; the [repository profile](repository-profile.md) supplies branches, validation and operational rules. No AgentControl bot or fleet enrollment is required.

## Task routing

| Assignment | Procedure |
|---|---|
| Issue pickup/implementation/resume | [Issue work](../../.agents/skills/hvo-issue-work/SKILL.md), then [PR lifecycle](../../.agents/skills/hvo-pr-lifecycle/SKILL.md) |
| Full issue lifecycle | The same procedures through reviewed, green, authorized merge and closeout |
| Epic | [Epic work](../../.agents/skills/hvo-epic-work/SKILL.md) plus issue/PR work for children and final epic acceptance |
| Independent review/corrections | [Code review](../../.agents/skills/hvo-code-review/SKILL.md) |

AGENTS, Claude and Copilot entry points name these routes explicitly. Automatic skill discovery alone cannot guarantee compliance; the source-bound evidence gate checks observable records. Every task still respects its actual assignment scope.

## Ownership and delivery

Read issue/comments/dependencies and existing claims before editing. Assign the accountable owner, apply `workflow:in-progress` and a depth label, publish a friendly Work started comment and re-read for conflicts. Lead with the outcome/plan and next checkpoint; preserve distinct person/session, scope, acceptance, target/branch, revision, review and stopping point in the readable record and expandable details. Keep the claim through review/CI; release or transfer explicitly with a handoff. A blocker overlay retains ownership.

Implement acceptance in a dedicated branch/checkout and record actual source-bound local validation. PR bodies use [the standard template](../../.github/PULL_REQUEST_TEMPLATE.md) and list every material author/session and the performer of each check. Keep requested models separate from actual execution values.

Every PR starts draft. Local validation and bounded simple preflight support review; standard build/test/container CI waits for completed independent review and verified finding/thread resolution. Review the complete initial diff, then focused correction/synchronization ranges with carried findings. One short summary leads with verdict, coverage/counts and linked P0–P3 finding bullets, followed by validation/limits. All reviewer/system/provider/actual model/effort, session/host/workspace, source and selection information stays together in expandable Agent details.

The author replies to each finding; the independent reviewer verifies its terminal disposition; the authorized owner resolves the thread. A cap, "fixed", an outdated annotation or omitted finding never proves correction. P0/P1 and acceptance/security/data-loss/material-correctness defects block; any eligible deferral needs explicit authorization and a linked follow-up.

Mark ready only when the current candidate meets review/acceptance requirements. Run standard CI, then merge using the profile when authorized. Failure/new source returns to draft and invalidates readiness; independently review source corrections before the next ready/run. Diagnosed infrastructure failure with unchanged source can retain existing source review through a bounded retry.

Verify merge SHA/target and actual issue closure; publish closeout, release active ownership/reservations and safely clean completed branches/workspaces. A full-lifecycle assignment authorizes these steps for its defined scope; release/deployment is separately scoped. An epic continues through ready authorized children and closes only when epic acceptance is verified.

## Reviewer selection and provenance

Mechanical/Standard/Deep define coverage. Auto selects an eligible reviewer profile; Preferred uses stated choices/fallbacks; Required permits only the explicit choices/settings/count. A list of models means one of them unless multiple reviews are explicitly required. Depth is independent of tool, model effort, reviewer count and round budget.

Baseline review uses a distinct non-implementation session/person. Assignments can require a different GitHub account, the same account with a different underlying model, or a different provider. Compare against all implementation models unless another scope is explicit. Switching a model inside the implementation session is supplementary self-review. Hidden model facts cannot satisfy strict unverifiable constraints. Compare actual runtime evidence when supported; editable prose alone cannot attest who ran a model.

## Work started comment

Use a short human-readable ownership update. The durable metadata stays available without dominating the comment. Example:

```markdown
### Work started

I’m picking up this issue to <observable outcome>. I’ll keep it updated through <authorized delivery boundary>.

- **Owner:** @<account>, working with <person/system>.
- **Plan:** <short implementation and validation plan; link existing acceptance>.
- **Branch:** `<branch>` → `<target>`.
- **Review:** <depth and any reviewer/model requirements>.
- **Delivery:** <authorized stopping point, including merge when assigned>.

**Next checkpoint:** <observable milestone>.

<details>
<summary>Agent and process details</summary>

- **Started:** <UTC>.
- **Process revision:** <revision>.
- **Session / system:** <distinct session and actual application/harness>.
- **Provider / model / effort:** <actual values, unknown when unavailable; human N/A>.
- **Host/server:** <actual host identifier; platform when relevant>.
- **Harness instance ID:** <actual instance ID, or unknown when not exposed>.
- **Checkout/worktree:** <exact absolute repository workspace path>.
- **Dependencies:** <relevant details or none>.

</details>

<!-- hvo-issue-claim:v1 -->
```

Update an existing ownership comment when clarifying its plan or presentation; preserve the start time and material history. Keep host/instance/session and exact checkout/worktree location together in Agent details, and update them when work moves. A real release/transfer or blocker gets its own clear handoff/update. Failed publication or conflicting claims still prevent assuming ownership.

## State labels

The issue retains `workflow:in-progress` until completion/release. Each PR has one phase: `workflow:draft`, `workflow:review`, `workflow:changes-required`, `workflow:ci`, `workflow:ready-to-merge`, `workflow:complete` or `workflow:cancelled`. Replace the old phase on transition. Add/remove `workflow:blocked` separately. Retain type/priority/component labels and one depth label (`review:mechanical`, `review:standard`, `review:deep`).

See the [complete state rules](../../.agents/skills/hvo-pr-lifecycle/references/pr-states-and-labels.md) and [review format](../../.agents/skills/hvo-code-review/references/review-format.md) for gates, recovery and publication details.
