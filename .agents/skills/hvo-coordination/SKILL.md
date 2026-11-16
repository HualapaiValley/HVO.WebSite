---
name: hvo-coordination
description: CANDIDATE — not activated. Apply the tool-neutral HVO ownership, dispatch and reconciliation contract only within the pilot preparation scope.
---

# Coordination contract — pilot candidate

Read [common policy](../../../docs/development/policy/common.md) and the
[adoption profile](../../../docs/development/policy/website.md). This is a procedure
contract, not an installed dispatcher/watchdog or proof of unattended recovery.
The [disabled local journal](../../../tools/policy-pilot/README.md) implements
intent/event persistence and recovery inspection only; its observations are
unverified and never authorize dispatch, retry, completion or merge.
Do not activate dispatch before the profile's qualification is complete.

One authoritative coordinator owns the work domain on one host. A standalone
worker can be the lifecycle owner. Until atomic fencing is implemented and tested,
ambiguous ownership stops all further mutations; do not silently take a stale claim.

Persist before dispatch: repository/issue/assignment-revision work key, owner and
session, ownership generation, policy/profile versions, scope/risk/endpoint,
acceptance evidence, checkout/branch/base/head, worker/reviewer handles, requested
and observed model/effort, PR/check/queue/merge IDs, current state and next action.
Child tasks receive child keys. Each attempt has a distinct attempt ID under the
same work key. Record dispatch intent durably before calling transport; pass a
provider idempotency key only if actually supported and capture the returned handle.

Callbacks contain work key, generation, attempt ID, event ID, timestamp, source,
state and evidence. Persist receipt before acknowledging; deduplicate by event ID
within the attempt. Reject/quarantine wrong-owner or obsolete-generation callbacks;
retain them for reconciliation. Callback order must not regress terminal state.
Start, meaningful milestones, blockers and terminal outcomes are required signals.

A durable watchdog must survive the coordinator conversation. Proposed heartbeat:
10 minutes; investigate after 20 minutes. These are configuration defaults to
validate, not installed schedules. Silence proves neither failure nor safe replay.
On restart, reload journal and reconcile authoritative worker/run, PR/source,
checks and queue. Uncertain launch/push/PR/gate/merge/notification is AMBIGUOUS;
failed lookup is not evidence of absence. Resume only after safe outcome/ownership
is established. No blind retry or automatic stale-owner takeover.

Finalization follows the common lifecycle and existing PR contract through observed
merge and acceptance. Record terminal state, merge SHA, post-merge obligations,
remaining blockers and next owner; do not report queue admission as completion.

Before activation, implement and test durable journal/write failures, duplicate and
lost callbacks, reordered events, coordinator restart, launch timeout after remote
success, stale owner, provider failure, changed PR head/target and failed CI.
Assert no second dispatch on ambiguity and no merge without current evidence.
An unimplemented transport/watchdog leaves the assignment blocked; this contract
does not promise exactly-once dispatch, cross-owner fencing or unattended recovery.
