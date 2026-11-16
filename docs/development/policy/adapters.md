# Thin harness and Kepler adapters

All adapters load [common policy](common.md), [WebSite profile](website.md),
repository AGENTS.md and its applicable existing issue/review/PR skills before
work. Mandatory content is repository-contained. No adapter grants broader scope,
waives gates, installs global skills or changes security settings.

These are explicit loading procedures, not verified automatic-discovery support.
For this repository's engineering workflow, the repo-local contracts govern over
legacy global `hvo-review`, `hvo-worker` and `hvo-plan` procedures. An auto-surfaced
global skill is not an instruction to invoke it or a substitute for local files.
Platform/security requirements and the assignment's narrower scope still apply;
resolve an actual conflict before acting. Do not edit or uninstall global skills.

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
do not infer it from configuration labels. Missing strict model provenance blocks
that route. Do not invent command flags or elevate access to satisfy a policy.
Model preferences and authorized fallbacks belong in assignment configuration.

## Kepler coordination

Kepler follows the [coordination procedure](../../../.agents/skills/hvo-coordination/SKILL.md)
using the T3 hub MCP. Record requested and provider-reported settings separately;
missing provenance stays unknown. A harness or alias does not prove model family
or distinct environment sessions. Missing required capability blocks the affected
operation.
