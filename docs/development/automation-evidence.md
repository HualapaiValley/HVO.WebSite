# PR automation evidence

Readable author/review text is the primary human interface. A small versioned JSON record lets the gate compare actual reported identity, source and findings without guessing from prose. It records execution declarations; it does not attest a hidden model or prove review quality.

Add exactly one record per relevant PR/comment body:

```text
<!-- hvo-pr-process
<one JSON object>
-->
```

[tools/pr-process-fixture.json](../../tools/pr-process-fixture.json) contains parseable examples of author, review, finding and verification records. Copy only the relevant object, replace all example values with actual execution/source data, and retain the required readable description. Do not paste the entire fixture or imply the examples are real reviews.

## Identity and source

Each identity contains `account`, distinct `session`, `system`, `provider`, `model`, `effort`, `host`, `instance` and `workspace`. Host names the actual server; workspace is the exact checkout/worktree path. Record the harness/server instance ID only if exposed, otherwise `unknown`. Keep branch/location in expandable Agent details and update location when work moves. Human values use provider `human`, model/effort `n/a`; undisclosed agent settings use `unknown` or accurately reported `provider-managed`. Record real runtime values rather than requested model aliases or the catalog's default setting. The record's `publisher` must match the actual GitHub creator of the containing PR/comment. A relayed reviewer identity can differ from its publisher, but that route cannot prove a strict different-account requirement.

The gate authenticates publication permissions separately: a qualifying review or verification is published by an account with repository write/maintain/admin permission. Public comments, display names and bot names alone cannot approve the candidate. A qualified external reviewer can have its report faithfully relayed by an authorized owner, with the original reviewer identity preserved; strict direct-account requirements still apply. This publisher check protects review admission but does not attest model execution.

The permission lookup uses GitHub's collaborator-permission endpoint, which requires repository Metadata read access. Permission facts come from the authenticated API, outside the editable record. Unknown/inaccessible permissions cannot authorize a review. Verify the deployed workflow token's access in live CI; a local owner-token check alone does not prove it.

`headSha` is the full raw PR head, and `baseSha` the full current target tip. They must match the GitHub candidate at verification and publication. Record the reviewed merge base/range separately in readable review text. The CI summary records the synthetic merge SHA actually checked out in addition to head/base. An old record is historical evidence; update source and obtain applicable review rather than silently carrying approval to a changed candidate.

## Author record

The PR body contains `version: 1`, `kind: "author"`, publisher/source, primary `identity`, all materially implementing `contributors`, local `validation`, `allowedDeferrals` and `reviewPolicy`.

Each validation entry has a stable `id`, `stage` (`local` or `ci`), exact `command`, actual `result` (`pass`, `fail`, `blocked` or `pending`), performer `identity`, and source-bound `headSha` / `baseSha`. Explain blocked/pending evidence with a `reason`. Mandatory local IDs and required build metrics come from the trusted repository profile, not an author-controlled required flag. They must pass before readiness; a blocked explanation alone cannot qualify them. Post-review CI entries document future work and are separate from local prerequisites. Readable validation names source, useful counts/environment and evidence links; distinguish personally run checks from results inspected. Preserve multiple sessions and model/settings changes with their contribution ranges. A validation-only performer need not be mistaken for an implementation contributor.

`local:pinned-sdk-build-zero-warnings` requires `sdk: "10.0.400"` and `metrics: {"warnings": 0, "errors": 0}` with a passing result. `local:non-live-tests` and, for changes under `.github/workflows/` or `tools/pr-process*`, `local:pr-process-tests` require `metrics` with an integer `passed` count of at least one and `failed: 0`. GitHub changed-file metadata determines the process-test requirement. Honest nonzero metrics remain visible during draft preparation and cannot pass admission. A CI-stage entry cannot substitute for any required local ID.

The review policy declares:

- `depth`: `mechanical`, `standard` or `deep`.
- `selection`: `auto`, `preferred` or `required`.
- `minReviewers`: the explicit independent reviewer count.
- `differentAccount` and `differentModel`: required separation; `differentProvider` when a different provider is required. Distinct session is always required against every implementation contributor.
- `modelComparison`: `all-contributors` (default recommended scope) or explicitly `primary`.
- `models`: provider/model selectors with optional mandatory `effort`.
- `modelMode`: `one-of` for alternative candidates, or explicit `all-of` for a model panel. Each panel selector occupies one slot filled by a distinct independent reviewer session, including overlapping selectors.
- `fallback`: `any-eligible`, `listed-only` (with `fallbackModels`) or `none`. Required exact choices cannot be silently substituted.

Depth specifies coverage, not effort or reviewer count. Preferred substitution requires an actual `selectionNote` in the review. For an incomplete preferred panel, every missing slot needs a separate reviewer authorized by `fallback`, with that reviewer's substitution reason. A reviewer matching another preferred selector may substitute only under the same fallback rules; notes alone never authorize fallback. `none` requires the complete exact panel, while `listed-only` restricts substitutions to `fallbackModels`. Required panels accept no substitutions. Required models/settings/separation with unverifiable values keep the role incomplete. Same operator account with a distinct reviewer session and different model is supported; switching models in the implementation session is self-review.

`allowedDeferrals` records explicit owner authorization for eligible finding IDs with linked follow-ups. It cannot waive P0/P1 or acceptance/security/data-loss/material-correctness blockers. Keep the authorization and residual risk visible in the finding thread.

## Review and findings

Publish the concise human [review summary](../../.agents/skills/hvo-code-review/references/review-format.md), including the actual runtime/coverage/separation outcome, plus a `kind: "review"` record. It names publisher/source/identity, `verdict` (`APPROVE`, `CHANGES_REQUIRED`, `INCOMPLETE`), actual `depth`, `coverage`, `coverageComplete`, material `limitations`, and the complete `findings` index.

Both PR conversation summaries and submitted native review summaries are supported. Native summaries must target the current head and be published (not pending/dismissed); a native request-changes state cannot claim an APPROVE record. Under the same account, publish native COMMENT rather than self-approval. GitHub's native review REST data provides submitted_at but no updated_at; publish a new summary for a correction or changed verdict rather than depending on edits to reorder old decisions. Reconcile native-only updates through the ready transition, a normal PR comment, CI completion or explicit metadata workflow dispatch.

For an initial changes-required review, publish the real findings and open state in the readable summary; the gate will remain ineligible until a current-source approval contains terminal verified dispositions. Never fabricate a terminal disposition to fit a schema.

Each finding root gets its own inline code thread with `kind: "finding"`, stable `id` (`F1`, `F2`, …) and `severity` (`P0`–`P3`), alongside impact/evidence/source references/possible solution/verification. Set `nonDeferrable: true` for acceptance/security/data-loss/material-correctness blockers at any severity; P0/P1 always block regardless of the flag. Its GraphQL thread node ID is referenced by the final review index as `threadId`. Preserve IDs and threads across rounds.

The author replies with the correction and source-bound validation. Independent terminal verification adds a `kind: "verification"` reply with publisher/source/identity, `findingId`, terminal `disposition` (`CORRECTED`, `DEFERRED`, `NON_ACTIONABLE`, `SUPERSEDED`) and actual `evidence`. Deferral additionally needs a `followUp` and eligible owner authorization; superseded needs `supersededBy`. These machine values correspond to readable `VERIFIED_*` replies. Resolve only after the reviewer verifies.

The approval's finding index lists every carried finding with ID, severity, actual thread ID and verified terminal disposition. All substantive inline threads are tracked; unresolved, omitted or unverified threads prevent readiness. If a finding cannot be anchored by the current tool, preserve an individually addressable comment with immutable path/line source and report that it needs the gate's supported tracking route before approval. The initial gate supports GitHub inline review threads; do not claim an untracked fallback satisfies automated approval.

## Running and reconciling

`node --test tools/pr-process.test.mjs` tests the evidence predicates and transition behavior without external writes. `node tools/pr-process.mjs` performs a read-only live candidate check using `GITHUB_REPOSITORY`, `PR_NUMBER`, `GH_TOKEN` and `EXPECTED_HEAD` / `EXPECTED_BASE` / `EXPECTED_MERGE` source binding. It verifies the synthetic merge commit's parents against the target/head through GitHub's git-commit API and fails when the PR is draft, moved or incomplete. Supply credentials through normal credential handling; never paste tokens into evidence or logs.

`node tools/pr-process.mjs preflight` makes one PR read and one linked-issue read. It checks the conventional title (`<type>(<component>): <change> (#N)`), matching same-repository closing reference, seven description sections, current source and author/validation identities. Preparation evidence may report failed or explained blocked checks while draft; this format check grants no review approval. Description edits rerun preflight.

The trusted `PR Process` workflow reads main's controller, queries metadata and maintains draft state/phase labels and candidate-head check statuses. It never checks out/executes PR code or consumes PR artifacts with write credentials. PR CI is a read-only `pull_request_target` workflow: its authoritative gate checks out the immutable target tip; standard jobs check out only the verified immutable merge SHA after admission. Candidate helpers remain testable in bounded draft preflight but cannot authorize themselves.

The standard run's immutable GitHub event title is `HVO-PR-CI v1 pr=<N> head=<SHA> base=<SHA> merge=<SHA>`. The controller checks that tuple, trusted workflow/event, original creation after approval, first complete attempt, and success of every required gate/build/smoke job. It publishes `build-and-test` and `docker-smoke` statuses on the candidate head only after that verification. An old-target run or partial rerun cannot establish readiness; unchanged-source infrastructure recovery uses a fresh complete run.

GitHub has no Actions event for review-thread resolution alone. After a thread-only state edit, reconcile through the metadata workflow's `workflow_dispatch` input `pr_number`, or the owner's normal ready transition. The ready gate rechecks actual threads. Configure required conversation resolution and required checks before claiming branch-level enforcement; policy text alone does not set repository protection.

Agents create PRs with `--draft`. GitHub/API creators can open a native ready PR; the controller returns it to draft asynchronously, while standard CI has no opened trigger and cannot start without source-bound review. The preflight workflow's completion also provides a trusted reconciliation route for bot-created PRs with restricted event tokens. This is a practical correction boundary, not a claim GitHub guarantees draft-at-creation for every external creator.

For the initial adoption PR (issue #394, PR #395), owner state writes are manual because the trusted workflow/evaluator/controller is not on main yet. Live authoritative PR CI awaits that adoption; preflight and local/controller fixtures do not prove its live activation. No candidate-controlled bootstrap approval bypass is provided. Record tested behavior separately from active automation. No AgentControl bot, enrollment service or fleet is required.
