<!-- Open as draft. Only simple preflight checks run before independent review and
verified finding/thread resolution. Mark ready for standard CI only after those gates.
CI failures return to draft; review code corrections before starting CI again.
The lifecycle owner updates the phase label while retaining the issue claim. -->

## Purpose

Closes #<!-- issue number -->

<!-- State the concrete problem or trigger and the resulting behavior. -->

## Work completed

<!-- Group actual changes by behavior or component. Include material contract,
configuration, documentation and test changes. -->

-

## Authorship and provenance

GitHub PR author: <!-- actual PR author account -->

| ID / role | Person or agent session | System / harness | Provider / model / effort | Contribution and runtime evidence |
|---|---|---|---|---|
| A1 / Implementation | | | | |

<!-- Use actual values; human model/effort = N/A, unavailable agent values = unknown.
Record materially contributing sessions and model/settings changes as additional rows,
with contribution ranges and task/runtime links when available. Add expandable Agent details
with host/server, known harness instance ID, exact checkout/worktree path and branch; unknown
instance IDs stay unknown. Keep location current if work moves. The primary author does
not inherit credit for work or checks performed by another session. -->

## Acceptance evidence

| Issue criterion | Evidence or remaining limitation |
|---|---|
| | |

## Local validation

Validated commit or source fingerprint: <!-- full SHA or reproducible fingerprint -->

<!-- Include host, SDK, architecture or container details when they affect reproduction.
Record exact commands, actual results and useful counts. Link lengthy output separately.
For unavailable checks, state not run and the reason. Attach applicable UI/browser evidence. -->

| Command or check | Result | Performed by | Evidence |
|---|---|---|---|
| | | <!-- provenance ID; add a validation contributor row when needed --> | |

<!-- Distinguish checks personally run from imported results inspected. Identify a
different validated source per row when the results do not apply to the SHA above. -->

## Review requirements

Review depth: <!-- Mechanical / Standard / Deep: required coverage, not model effort -->

Required reviewers: <!-- Default: 1 independent reviewer. Explicitly state any additional reviews. -->

Independence constraints: <!-- Baseline: distinct reviewing session/person; same GitHub
account allowed. Optional: different GitHub account, same account with a different model,
or different provider. A different model means the actual underlying model, not just a
different tool or effort. State the comparison scope; default is all implementation models. -->

Reviewer selection: <!-- Auto / Preferred / Required. Default: Auto from configured eligible profiles. -->

Model/settings and fallback: <!-- For Preferred/Required, name ordered preferences or allowed
alternatives, supported settings, and permitted fallback. A list means one-of unless explicitly
requiring multiple reviews. For Auto, resolve at assignment using the available tool/catalog.
Do not guess an unavailable or undisclosed model/effort. -->

<!-- Name relevant lenses: correctness, failure handling, concurrency, durability,
security, compatibility, UI, performance, CI or deployment. -->

## Risks and follow-up

<!-- Describe material compatibility, migration or operational effects.
Link deferred work to issues; use None when there is none. -->

<!-- Keep this body current after corrections. Independent review reports and CI results
remain separate records on this PR. -->

<!-- Append one actual author JSON record using docs/development/automation-evidence.md
and tools/pr-process-fixture.json. It must name the current full PR head/base SHA,
all implementation contributors, validation performers and reviewer requirements.
Keep requested model choices separate from actual author/runtime values. -->
