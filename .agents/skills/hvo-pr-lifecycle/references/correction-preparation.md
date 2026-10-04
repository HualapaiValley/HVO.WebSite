# Preparing finding corrections

This checklist preserves useful focused-fix and similarity checks from the former OpenCode resolution prompt. [Issue work](../../hvo-issue-work/SKILL.md), [PR lifecycle](../SKILL.md) and the independent [review format](../../hvo-code-review/references/review-format.md) own the lifecycle, dispositions, deferral and thread rules. An author cannot verify its own terminal finding disposition.

## Triage and scope

Read all review/issue comments, threads, prior responses, acceptance and failed CI evidence. Confirm worktree/branch/current target/head and preserve other owners' work. For each finding track its original ID and URL/thread, immutable location, concern, present validity, proposed correction/disposition, focused checks, response and outstanding independent verification.

Classify evidence for planning: valid and in scope; valid but broader redesign; already addressed; outdated; unreproduced/insufficient evidence; or disputed with concrete source reasoning. These planning categories do not replace canonical terminal dispositions. A broader-refactor label, follow-up or owner disagreement cannot silently clear a blocker. Keep it open until an eligible independent disposition and required authorization exist; ask for a material scope/product decision only when existing authorization cannot resolve it.

## Focused corrections and similar patterns

Inspect surrounding conventions, callers and existing tests. Correct the root cause, preserve public contracts outside the assignment and avoid unrelated refactors, upgrades or compatibility scaffolding. Search repeated APIs, config keys, exception/cancellation/timer/worker patterns, DTO/SQL/EF shapes, security/logging rules and UI/test patterns when the same failure could recur.

Include other occurrences only when they share the root cause, fit scope and have bounded compatibility risk; validate generalized behavior where useful and explain the scope in the author response. Record broader architecture/dependency, public API/schema, security, deployment or UI redesign as separate follow-up evidence without claiming a correction. Delegated triage/similarity/verification preparation, when authorized, remains evidence to reconcile, not independent approval of candidate edits.

## Validate, respond and hand off

Run affected focused reproducers first, then the profile's mandatory current-source build/non-live validation; use meaningful regression/failure tests for behavior changes. Record failed/unavailable checks honestly. Check whitespace and the actual diff for introduced regressions. Keep draft state for changed source, update author provenance/evidence, and reply in the original thread with correction SHA, observable change, similar-pattern coverage and actual validation. Keep original finding history and IDs.

Carry the original brief, findings, responses, unresolved objections and prior/current immutable heads into each distinct-session correction review. The reviewer reopens source/interactions and publishes VERIFIED_CORRECTED/DEFERRED/NON_ACTIONABLE/SUPERSEDED or STILL_OPEN using the canonical format and record. The authorized owner resolves only after terminal independent verification is present; a cap, author's tests or top-level "resolved" claim is insufficient. Report failed reply/verification/resolution publication instead of claiming success. Review and resolution reports remain GitHub metadata, not implementation files.
