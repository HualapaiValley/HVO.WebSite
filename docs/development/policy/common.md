# HVO engineering policy — 2026-11-16.6 pilot

This is the canonical common-policy source for the local WebSite pilot. Other
repositories have not adopted it. The [manifest](manifest.json) pins the portable
files; distribution is an explicit reviewed copy with digest verification, never
a runtime fetch or a global symlink. A future common-source repository must be
designated before distributing further snapshots. Edit here, update the manifest,
and review the complete snapshot together. A digest proves consistency, not trust.

This implements the accepted decisions in HVO-policy-proposal.md, Library version
1, design v0.2 (16 November 2026), pinned by identity and byte digest in the
[manifest](manifest.json). The source remains in Library; access is required to
inspect it, and a digest alone cannot replace that inspection. Roy subsequently
clarified that ordinary review requires independence, not a different model; that
clarification supersedes the design's Standard-tier different-model proposal.
The [WebSite adoption profile](website.md) fixes
the current activation boundary. Existing trusted evaluators remain authoritative;
these documents neither grant assignment authority nor publish a passing gate.

## Authority and precedence

Platform/security requirements come first, then this common policy, then repository
profile additions. Stop and resolve conflicts rather than guessing. An assignment may narrow the permitted
scope or add requirements; it cannot lower review-tier minima, required model-family
separation, required validation, protections or safety boundaries. An explicit
assignment is not a waiver. Resolve conflicts before the affected action.
Max, Kepler and other identities have no
special privileges. A coordinator is optional and follows the same policy.

Record owner, scope, acceptance criteria, risk and endpoint (research,
implementation, PR-ready, merged or deployed). Only an explicitly authorized lifecycle owner may merge autonomously, after all
required review and check gates pass. A draft-only assignment never merges.
Release publication, deployment and physical operations each require their own
explicit authorization, independent of merge authority and of each other. Physical
operations include operating the real roof or any other physical device.
Full-access, noninteractive execution is allowed only where supported and authorized;
it never overrides platform restrictions or authorizes credential/network bypass.

## Lifecycle and evidence

1. **Intake:** verify canonical remote, target/base SHA, clean or owned working
   changes, policy/profile version, existing PRs and ownership. Map each acceptance
   criterion to evidence; classify by impact rather than line count.
2. **Claimed:** establish one accountable owner and durable work key. Use an
   isolated task branch/checkout. Stale activity is not permission to take ownership.
3. **Implementing:** make the smallest coherent change; preserve unrelated work.
   Reproduce faults when feasible and test relevant behavior/failure boundaries.
   Record actual commands, source, performer, results and unavailable checks.
4. **Independent review:** reviewers inspect the intended merge range, requirements,
   affected interactions and all carried findings. Correct blockers and obtain a
   fresh verdict for the exact new head. An incomplete review is not approval.
5. **Ready:** finalizer verifies ownership, current target/head, acceptance, local
   validation, review independence/count and independently verified dispositions.
   After three unsuccessful correction cycles, replan/escalate; never waive blockers.
6. **CI/merge queue:** use the profile's existing checks and merge method. Bind
   evidence to the current head/target and tested merge candidate. Before another
   push, reconcile queue/auto-merge state, return to draft as appropriate and renew
   affected evidence. Never bypass protection with admin rights.
7. **Merged → Verified complete:** observe actual merge SHA/target and required
   post-merge results, then verify acceptance and closure. Queue admission is not
   completion. Clean up only owned, recorded, provably merged resources after
   checking active bindings. Report deployment obligations separately.

Blocked, paused, canceled, failed and ambiguous are explicit non-complete states.
An uncertain remote mutation must be reconciled before any replay.

## Independent review contract

| Risk | Minimum review |
|---|---|
| Low: non-executable documentation/presentation without behavior or security impact | One independent person/session; suitable lightweight model allowed |
| Standard: ordinary behavior/API changes | One independent person/session; a different model is optional unless explicitly required by the assignment |
| High: safety, security, durability, state/migrations, concurrency/recovery, deployment, cross-component effects, camera lifecycle, CI trust or policy enforcement | Two independent reviewers from different model families, at least one deep review |

A reviewer must not author/modify the candidate or finalize it during that review.
Never infer model family from a name or alias. Record requested and provider-reported
model/effort separately, session, scope, base/head/target, coverage, checks and
limitations; unknown provenance remains unknown. While model family cannot be
authenticated, automated High-risk admission is blocked; reviews can still inform
a human decision when labeled honestly. Distinct thread IDs are observable;
distinct environment sessions may not be. A high-risk
finalizer should be distinct from the implementer where practical.

Verdicts are CLEAN, CHANGES_REQUIRED, INCOMPLETE or BLOCKED. Findings have stable
IDs, severity, evidence, impact, possible correction and verification criteria.
Acceptance, correctness, safety, security, data-loss, compatibility and required-test
failures block regardless of priority. Independently verify dispositions; eligible
nonblocking improvements may become documented follow-ups. Never omit carried
findings to obtain a clean verdict. Repository evidence adapters translate formats
without relaxing this contract.

## Validation and runtime routes

Preserve profile-required tests, security checks, coverage floors and architecture
boundaries. Run focused checks during work and the mandatory local preflight before
review; protected-branch checks must qualify the actual candidate before merge.
Use established host resource gates for heavy jobs; cancel only obsolete owned jobs.
Do not weaken checks or tests to obtain green. CI/queue optimization is separate.

Retain failed evidence. Deterministic failures require correction. As procedure, the default
unchanged-source infrastructure budget is two retries; a stricter repo rule wins.
This budget is not claimed to be implemented.
Route selection is assignment configuration, not hardcoded model names. Validate
capability and supported effort in the actual launch context. Record requested and
observed settings and fallback reason. A catalog entry is not proof of execution.
For transient provider failures, use bounded backoff with jitter and provider hints
(starting proposal: 5/15/45 seconds), then only an authorized compatible fallback
that preserves required review separation/capability. Diagnose auth/configuration
or policy denials; do not bypass them. Uncertain execution needs reconciliation,
not a retry timer. Missing mandatory routes leave work blocked.
