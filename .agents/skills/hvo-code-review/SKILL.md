---
name: hvo-code-review
description: Independently review a HualapaiValley PR or correction, publish attributed P0-P3 summaries and separate evidence-backed code findings, and verify their dispositions.
---

# Independent code review

Read AGENTS.md, [the repository profile](../../../docs/development/repository-profile.md), project/scoped guidance and [the review procedure and formats](references/review-format.md). The latter owns depth, selection, provenance, severity, finding/thread and correction rules; read it for every assigned review. Load [PR lifecycle](../hvo-pr-lifecycle/SKILL.md) before changing GitHub state.

Use a distinct reviewer person/session that did not materially implement the candidate. Check author provenance and required account/session/model separation before accepting the assignment. Record actual reviewer/system/provider/model/effort; do not infer hidden facts. Required unavailable/unverifiable constraints yield INCOMPLETE. Do not edit the implementation branch.

Bind initial review to immutable merge-base/head and target SHA. Inspect the full change and affected context against acceptance and requested lenses. Deep adds applicable interaction/failure/security analysis; runtime effort is separately selected. Corrections review the exact prior-head/new-head delta, relevant interactions and every carried finding. Synchronization covers target-update effects/conflict resolution.

Run focused reproducers when necessary/available; distinguish personally run results from inspected evidence. Lead the summary with verdict, short coverage, counts and linked P0-P3 finding bullets, then concise validation/limits. Keep all reviewer identity/system/provider/actual model/effort, session/host/workspace, source and selection/separation details together in expandable Agent details, as the reference template specifies. Each finding has its own code thread/comment, stable ID, evidence/source references, impact, possible solution and verification criterion. For review-only issue work, use attributed issue summary plus separately addressable finding comments with immutable code references.

Verify author dispositions in their existing threads and publish VERIFIED_CORRECTED/DEFERRED/NON_ACTIONABLE/SUPERSEDED or STILL_OPEN. Preserve IDs/history and link carried findings. Only authorized owners resolve after terminal independent verification. Report publication gaps; never commit reports merely to publish them or equate a budget cap with approval.

Publish the machine-readable record described in [automation evidence](../../../docs/development/automation-evidence.md) alongside the readable summary. Re-check the head around publication and flag stale evidence. Code approval alone does not establish CI/merge readiness.
