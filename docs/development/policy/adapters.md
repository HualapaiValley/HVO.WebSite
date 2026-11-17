# Thin harness and Kepler adapters

Load the [common policy scope and rules](common.md#scope), [WebSite profile](website.md),
AGENTS.md and applicable local skills; automatic discovery is not verified.

| Harness | Discovery contract |
|---|---|
| Codex | Read root AGENTS.md and its explicit local skill paths; do not assume a global skill with the same name is equivalent. |
| Claude | Existing CLAUDE.md routes to AGENTS.md; verify local skill-link resolution and explicitly load canonical `.agents/skills`. Same-named or auto-surfaced global `hvo-*` skills are not equivalent and do not supersede these local contracts. |
| OpenCode | Existing opencode.json loads AGENTS.md and Copilot guidance. Existing `.opencode` wrappers remain adapters to canonical procedures; model aliases are not execution evidence. |
| Copilot | Existing `.github/copilot-instructions.md` routes to AGENTS.md and the same local skills. |
| Cursor | No repository Cursor rules/discovery adapter is installed or qualified. Explicitly load AGENTS.md and its linked local contracts; verify effective loading before use. |
| T3 | T3 hosts an underlying harness; identify it and follow its row, explicitly loading AGENTS.md and local contracts. Record both T3 and underlying harness identities; neither T3 nor this table proves automatic discovery. |

At launch, record harness/version, actual host/workspace/session, policy digest,
assignment boundary, requested route/effort, observable actual values and effective
permission/noninteractive mode. Verify the mode in a harmless local qualification;
do not infer it from configuration labels. Unknown provenance does not block launching work but cannot satisfy a strict
model/family requirement; that review role stays INCOMPLETE unless Roy records a
manual bootstrapping decision under the [review contract](common.md#independent-review-contract).
Do not invent command flags or elevate access to satisfy a policy.
Model preferences and authorized fallbacks belong in assignment configuration.

## Kepler coordination

Kepler follows the [coordination procedure](../../../.agents/skills/hvo-coordination/SKILL.md)
using the T3 hub MCP for pilot work. Record requested and provider-reported settings separately;
missing provenance stays unknown. A harness or alias does not prove model family
or distinct environment sessions. Missing required capability blocks the affected
operation.
