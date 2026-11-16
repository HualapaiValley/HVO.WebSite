# Thin harness and Kepler adapters

All adapters load [common policy](common.md), [WebSite profile](website.md),
repository AGENTS.md and its applicable existing issue/review/PR skills before
work. Mandatory content is repository-contained. No adapter grants broader scope,
waives gates, installs global skills or changes security settings.

| Harness | Discovery contract |
|---|---|
| Codex | Read root AGENTS.md and its explicit local skill paths; do not assume a global skill with the same name is equivalent. |
| Claude | Existing CLAUDE.md routes to AGENTS.md; verify local skill-link resolution before use and explicitly load canonical `.agents/skills` if auto-discovery is unavailable. |
| OpenCode | Existing opencode.json loads AGENTS.md and Copilot guidance. Existing `.opencode` wrappers remain adapters to canonical procedures; model aliases are not execution evidence. |
| Copilot | Existing `.github/copilot-instructions.md` routes to AGENTS.md and the same local skills. |

At launch, record harness/version, actual host/workspace/session, policy digest,
assignment boundary, requested route/effort, observable actual values and effective
permission/noninteractive mode. Verify the mode in a harmless local qualification;
do not infer it from configuration labels. Missing strict model provenance blocks
that route. Do not invent command flags or elevate access to satisfy a policy.
Model preferences and authorized fallbacks belong in assignment configuration.

## Kepler transport contract

Kepler is a coordinator identity using the same [coordination skill](../../../.agents/skills/hvo-coordination/SKILL.md).
Its adapter supplies only these capabilities:

| Operation | Required behavior |
|---|---|
| Persist | Durable work/attempt/event records before dispatch or callback acknowledgment |
| Launch | Pass complete scope, source, acceptance and policy paths; retain returned provider handle and supported idempotency key |
| Inspect | Query exact recorded worker/run and authoritative repository state without inferring absence from failure |
| Receive | Persist and deduplicate callback IDs; reject stale generation; preserve event/source evidence |
| Monitor | Durable watchdog independent of the conversation; reconcile before any retry |
| Finalize | Use the existing PR contract, assignment endpoint and qualified trusted gates; observe actual merge and acceptance |

No provider API, callback store or watchdog has been implemented/installed by this
pilot preparation. Missing capabilities are blockers, not permission to substitute
conversation memory or polling with unbounded replay. Validate each transport's
uncertain-outcome semantics before use. Legacy hvo worker/coordinator instructions
are historical audit evidence; never install them as a prerequisite here.
