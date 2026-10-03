HualapaiValley PR states and labels

This is the WebSite pilot policy. Every PR starts as draft. Drafts can receive simple preflight checks, while standard CI waits for completed independent review and verified resolution of findings. A green current candidate can be merged within the owner's authorization. A CI failure returns the PR to draft for diagnosis, correction and review of any source changes.

```text
claim issue -> implement -> draft PR -> independent review
    -> correct findings -> reviewer verifies -> resolve threads
    -> mark ready -> standard CI -> green -> authorized merge -> closeout

CI failure or a new source change:
    -> return to draft -> diagnose/correct -> review the changes
    -> verify findings -> mark ready -> standard CI again
```

**State and label assignment**

The issue remains assigned to its accountable owner through implementation, review, CI and merge. Apply `workflow:in-progress` when the claim is acquired and remove it after completion or explicit release. A reviewer is recorded/requested separately; waiting for review does not surrender the implementation claim. Distinct agent sessions sharing an account remain identified in claim/review records.

Each PR has exactly one lifecycle label from the table. Changing phase replaces the previous lifecycle label; it does not accumulate a contradictory history of active labels.

| PR phase | Native state | Lifecycle label | Permitted next action |
|---|---|---|---|
| Implementation and local preparation | Draft | `workflow:draft` | Local development/validation and simple preflight. |
| Independent review requested or running | Draft | `workflow:review` | Initial, correction or synchronization review. |
| Findings or CI failure being addressed | Draft | `workflow:changes-required` | Diagnose, correct, locally validate, then return to review. |
| Review complete; standard CI queued/running | Ready | `workflow:ci` | Run the standard profile on the reviewed current candidate. |
| Required CI green and final conditions verified | Ready | `workflow:ready-to-merge` | Merge when the assignment authorizes it. |
| Merged and closeout recorded | Merged | `workflow:complete` | Verify issue closure and cleanup. |
| Closed without merging | Closed | `workflow:cancelled` | Record the outcome/handoff; do not claim implementation completion. |

Additional labels have separate purposes:

- One review-depth label, `review:mechanical`, `review:standard` or `review:deep`, stays on the issue/PR as the required coverage. Escalate when risk changes. Depth is independent of model/effort and reviewer count; record Auto/Preferred/Required selection and any exact-model or multiple-reviewer constraint separately, as defined in the [review procedure](../../hvo-code-review/references/review-format.md).
- `workflow:blocked` is an overlay on the issue/PR when awaiting a dependency, unavailable reviewer or required decision. Retain the owner and underlying phase; remove the overlay when the blocker clears. An ordinary running CI job is not a blocker by itself.
- Retain the repository's issue type, scheduling priority, component and risk labels. Phase updates must not delete them.
- `workflow:finalizing` remains an optional reservation only for repositories whose profile requires it; WebSite does not acquire a fleet-wide lock by default.

The common pilot uses `workflow:*` lifecycle labels and `review:*` depth labels. Older `review:requested`, `review:changes-required` and `review:converged` state labels are legacy equivalents, not an additional competing state system. Reconcile them during a reviewed adoption rather than removing labels from unrelated live work.

The lifecycle owner verifies labels at each transition. Automation may maintain them later, but labels are visibility aids: the actual PR source, review records, threads and check results establish whether a transition is valid. Setting a label never grants merge authority or supplies approval evidence. Record a failed state write instead of reporting a successful transition.

**Checks allowed while draft**

Configure a named, bounded preflight set: title/body format, linked issue/acceptance fields, whitespace, inexpensive syntax/workflow lint, a suitably bounded secret scan, and review-record structure/staleness checks. These should not start the standard application build/test/container/performance pipeline or consume its expensive runner capacity.

Local builds, tests and reproductions remain part of implementation and review evidence. The draft restriction controls automated PR CI; it does not prevent the owner from validating the change locally.

**Entering standard CI**

Before marking ready, verify that:

1. The current head has the required independent review and completed coverage. Author/validation provenance is recorded. Required reviewer count, account/session/model separation and model/setting constraints are met; permitted fallbacks and hidden metadata are reported according to the selection policy. Compare supported runtime evidence when strict verification is required, rather than treating editable body fields as proof. A missing or unverifiable mandatory choice does not qualify as approval.
2. Every finding has a verified terminal disposition. Deferral is allowed only where the adopted policy and owner authorization permit it; it is never silently treated as a correction.
3. Actionable threads are resolved and acceptance evidence is complete.
4. The target synchronization and affected local validation required by the profile are complete.
5. Owner authorization and label/claim records are current.

Then replace the phase label with `workflow:ci` and mark the PR ready. A lightweight Review Evidence guard runs before the standard jobs. It checks the actual current head and finding records; a manually marked-ready but unreviewed PR does not qualify for the standard pipeline.

Record the PR head and the base or synthetic merge source actually tested. GitHub's PR workflow can test a merge commit, so final CI evidence must identify that binding rather than assuming every check tested the raw head. GitHub exposes both `ready_for_review` and `converted_to_draft`; they must be explicitly selected when workflows need those transitions because the default PR triggers are opened, synchronize and reopened. [GitHub PR workflow events](https://docs.github.com/en/actions/reference/workflows-and-actions/events-that-trigger-workflows#pull_request).

When implementing automation, account for event delivery: transitions written with a workflow's `GITHUB_TOKEN` may not trigger subsequent workflows automatically. The initial pilot can use the owner's normal GitHub route; any automated controller must explicitly ensure the gated standard run starts and records its source. Do not infer CI success from a transition for which no run exists.

This rule applies to dependency and other automatically generated PRs too. Before calling adoption complete, verify each PR creator's draft behavior and prevent its unreviewed candidates from launching standard CI. Existing ready PRs need an explicit migration decision rather than silently being treated as reviewed.

**CI recovery and source changes**

A failed, cancelled, timed-out or otherwise invalid standard run cannot satisfy merge readiness. Return the PR to draft, replace the phase with `workflow:changes-required`, retain the issue claim and publish the diagnostic evidence. Cancel obsolete active standard runs when returning to draft.

For a code defect, correct it, run affected local checks and request correction review of the previous reviewed head to the new head. Carry all outstanding findings and the CI failure evidence. After independent verification and thread resolution, mark ready and start standard CI again.

For a diagnosed infrastructure failure with no source change, the existing review remains attached to the same head. Return to draft, document the infrastructure correction and re-establish the ready conditions; a single bounded same-candidate rerun need not invent a code correction or a redundant code review. If the cause is uncertain, keep the failure unresolved rather than guessing it is infrastructure.

Any new source commit after review, including a correction made after CI passed, invalidates the relevant current-head approval and readiness. Return to draft before pushing where possible. The guard must also catch a push made while still ready, block standard jobs for the unreviewed head, clear `workflow:ready-to-merge`, and route the candidate back to review. Source synchronization follows the same applicable review rules.

Once required checks are green for the current reviewed candidate, verify target/source currency and conversation resolution, then apply `workflow:ready-to-merge`. Merge using the repository profile when authorized. Complete the issue/epic progress record, remove active ownership/blocker labels, apply `workflow:complete`, release reservations and clean up safely.
