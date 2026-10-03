---
name: hvo-issue-work
description: Pick up, implement or resume an authorized HualapaiValley issue, maintaining claim, acceptance, validation and handoff through its PR lifecycle.
---

# Issue work

Read repository AGENTS.md, scoped project instructions and [the repository profile](../../../docs/development/repository-profile.md). Load [hvo-pr-lifecycle](../hvo-pr-lifecycle/SKILL.md) before opening/updating the PR. Use [hvo-epic-work](../hvo-epic-work/SKILL.md) for an assigned epic.

## Claim before editing

- Read the full issue/comments, acceptance, dependencies, assignees and linked PRs. Work only in the authorized issue/queue. Preserve another owner's claim; waiting for review/CI is still ownership.
- Assign the accountable GitHub owner where available, apply `workflow:in-progress` and one review-depth label, and post a friendly **Work started** comment. Lead with the intended outcome, then owner, short plan, branch/target, review and delivery boundary, and next checkpoint. Link acceptance instead of copying it. Put actual person/session/system/provider/model/effort, host/server, known harness instance ID, exact checkout/worktree path, process revision, timestamp and dependencies in expandable Agent details. Use unknown for instance IDs the runtime does not expose. Update location when the workspace moves; a session ID alone may not identify its server. See [the claim example](../../../docs/development/PROCESS.md#work-started-comment). Avoid all-caps protocol blocks or unexplained shorthand.
- Re-read after writes to catch conflicting claims. Comments/labels are not an atomic lock; a failed write is not a claim. Release/transfer explicitly with a durable handoff; age alone does not permit takeover.

## Implement and retain evidence

Use a dedicated current-base branch/checkout. Implement acceptance and preserve unrelated work. Use focused validation during development, then the profile's required local checks. Record exact commands/results, source, environment and performer; unknown or not-run results stay explicit. Add meaningful behavior/failure tests when needed, not tests that mirror documentation.

Preserve identity for every materially contributing implementation session/model; model switches do not erase earlier contributors. Use PR provenance rows and link validation results to their performer. Actual values come from runtime, not requested aliases or catalog defaults.

Post progress at milestones/blockers and keep owner/claim through review, CI and merge. `workflow:blocked` is an overlay; continue independent authorized work when possible. A handoff records issue/PR, branch/head, validation, outstanding findings, blocker and next action.

## Deliver the authorized outcome

Follow the PR lifecycle through independent review, corrections and green required CI. Merge/closeout only when assignment authorization includes it; an explicit full-lifecycle request includes merge and cleanup for its scope. Stop at the assigned boundary with a concrete durable result. Release/deployment or unrelated queue work needs its own scope.
