---
name: hvo-coordination
description: Candidate procedure for HVO ownership, coordination and reconciliation.
---

# Coordination procedure

Read [common policy](../../../docs/development/policy/common.md) and the
[WebSite profile](../../../docs/development/policy/website.md). One accountable
coordinator owns each work domain; a standalone worker may be lifecycle owner.
Record the assignment, owner, scope, risk, endpoint, source, evidence and state.
Do not take over ambiguous ownership or treat silence as failure.

Use the T3 hub MCP. Before launch record a unique `clientRequestId` and title
nonce, along with project/environment, time window and prompt digest. Pass the
request ID and nonce at launch. Record the returned thread ID and other handles.
Poll with `t3_thread_read`; never call `task_status` as a read.

Treat any uncertain launch, send, push or merge as AMBIGUOUS. Reconcile before
any retry against all available bindings: clientRequestId, thread ID,
environment/project, time window, prompt digest and title nonce. A single title
match is not proof. Never relaunch blindly. A later run on a completed thread
requires fresh evidence review.

A read-only watchdog is separate from recovery that can resume work. Until recovery
is tested, it stops at a report to the human. Record requested and provider-reported
model and effort separately; unknown provenance stays unknown. Distinct thread IDs
are observable, but distinct environment sessions may not be. Follow the common
review and authorization rules before finalization.
