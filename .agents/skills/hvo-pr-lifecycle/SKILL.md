---
name: hvo-pr-lifecycle
description: Open and advance a HualapaiValley draft PR through source-bound independent review, finding resolution, gated CI, authorized merge and issue closeout.
---

# PR lifecycle

Read AGENTS.md, [the repository profile](../../../docs/development/repository-profile.md), [state and label rules](references/pr-states-and-labels.md), and [automation evidence](../../../docs/development/automation-evidence.md). Use [issue work](../hvo-issue-work/SKILL.md) for claims and [code review](../hvo-code-review/SKILL.md) for every independent initial/correction review.

## Draft submission and review

Open every PR as draft against the configured integration branch. Use the conventional observable-change title and [author template](../../../.github/PULL_REQUEST_TEMPLATE.md). Record issue/acceptance, all material contributors' actual identity/system/provider/model/effort, validated source and check performers, risks, depth and selection/separation constraints. Keep the author description current after corrections. Review reports are separate metadata.

Retain issue ownership. Maintain one PR lifecycle label; preserve other labels. Request an eligible independent reviewer on the exact source range and carry all findings/responses into every correction request. No draft standard CI. Bounded preflight and required local validation remain available.

Before ready, verify current-source completed review, required reviewer count/selection/separation, terminal finding verification, resolved actionable threads, acceptance and synchronization evidence. Missing/stale/unknown mandatory facts keep the PR draft. A model preference uses only its declared fallback; Required choices are not silently substituted. Publication under an account does not confer merge authority.

Use the supporting [correction checklist](references/correction-preparation.md) for triage, focused fixes and similar-pattern searches. It adds no self-verification or thread-resolution exception to those gates.

## CI recovery and merge

Mark ready/apply `workflow:ci` only after the evidence gate qualifies the candidate. Understand the tested head/base or synthetic-merge binding. Green CI must match the current reviewed source. On source change or invalid/failed/cancelled CI, return draft, cancel obsolete runs, apply changes-required, diagnose and review source corrections before the next ready/run. Proven infrastructure failure with unchanged source may retain source review through one bounded documented retry.

Once current checks/threads/target are verified, apply `workflow:ready-to-merge`. Merge using the profile only when the assignment explicitly includes merge/full lifecycle. Otherwise report a concrete reviewed, green PR. Budgets never waive blockers or authorize broader work.

Verify merge SHA/target and actual issue closure; publish COMPLETE with delivered acceptance, final review/CI links, follow-ups and deployment status. Release active ownership/blockers/reservations and safely clean only completed branches/workspaces. Update an assigned epic and continue its ready authorized children. Do not blindly synchronize main when another repository profile uses a different integration branch.
