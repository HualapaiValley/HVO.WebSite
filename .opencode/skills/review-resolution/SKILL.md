---
name: review-resolution
description: Route HVO.WebSite finding corrections through author response, distinct independent verification and authorized owner resolution.
---

# OpenCode correction adapter

Read [AGENTS.md](../../../AGENTS.md), the [repository profile](../../../docs/development/repository-profile.md), [hvo-issue-work](../../../.agents/skills/hvo-issue-work/SKILL.md) and [hvo-pr-lifecycle](../../../.agents/skills/hvo-pr-lifecycle/SKILL.md). Use the shared [correction checklist](../../../.agents/skills/hvo-pr-lifecycle/references/correction-preparation.md) to triage evidence, check similar patterns and validate focused fixes.

Keep finding IDs/threads and prior review history. The author replies with the actual correction/source-bound checks; a distinct non-implementation reviewer follows [hvo-code-review](../../../.agents/skills/hvo-code-review/SKILL.md) and its full [review format](../../../.agents/skills/hvo-code-review/references/review-format.md) to verify each terminal disposition. Only then may the authorized owner resolve the thread. An author's own check, a "fixed" reply or an outdated annotation is not independent verification.

Stay draft for source corrections and unmet evidence. Live model/settings and allowed fallback are resolved at the review assignment; do not promise a fixed model from this adapter. Retain the authorized scope and report blocked/failed checks or publication honestly. This adapter adds no severity, deferral, approval, CI or output rules beyond the canonical procedures.
