<!-- Kepler opens agent PRs as draft. The latest profile-named draft checks must pass on the exact head before independent review and
verified finding/thread resolution. Kepler marks ready for standard CI only after those gates.
CI failures return to draft; review code corrections before starting CI again.
The accountable issue owner retains the claim and verifies the phase label; the controller may maintain it. -->

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

<!-- Identify profile-required local prerequisites separately from post-review CI.
Required local checks must pass on the current source; explained blocked results remain
visible but do not qualify readiness. Use the profile's stable validation IDs in the record. -->

| Command or check | Result | Performed by | Evidence |
|---|---|---|---|
| | | <!-- provenance ID; add a validation contributor row when needed --> | |

<!-- Distinguish checks personally run from imported results inspected. Identify a
different validated source per row when the results do not apply to the SHA above. -->

## Review requirements

Review depth: <!-- Mechanical / Standard / Deep: required coverage, not model effort -->

Required reviewers: <!-- LOW: one independent frontier model CLEAN (default Sol); HIGH: two distinct independent frontier models CLEAN on the same head, at least one Deep (default Sol + Opus). Substitution follows common policy. -->

Independence constraints: <!-- Distinct reviewing sessions; by default each model differs from every
contributor in the reviewed range. Contributor overlap leaves the role BLOCKED unless Roy’s
sole recorded exact-head decision under the common review contract addresses it; the decision
never carries over to a later head, and an agent can never waive the requirement or grant the exception. Same GitHub account is allowed; a strict different-account
requirement has a direct-publication limitation under the current gate and Kepler relay. -->

Reviewer selection: <!-- Auto / Preferred / Required. Default: Auto from configured eligible profiles. -->

Model/settings and fallback: <!-- Tier minima and default Sol and Opus IDs are in common policy.
Specify eligible harness, settings and fallback routes; substitution follows common policy and
assignment constraints. Do not guess an unavailable or undisclosed model/effort. -->

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
