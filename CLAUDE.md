# Claude instructions

Read [AGENTS.md](AGENTS.md) and the [repository profile](docs/development/repository-profile.md) before acting. Their mandatory task-to-skill routing, project guidance, assignment scope and draft/review/CI lifecycle apply to Claude too. Repository-local skills are in `.agents/skills`; `.claude/skills` points to that same source. Record actual Claude session/model/effort values, including unknown values when unavailable.

For WebSite work, repository-local skills (`.agents/skills`) govern; global `hvo-*` skills on the host are not substitutes and do not authorize dispatch or installation — see [common policy precedence](docs/development/policy/common.md#authority-and-precedence).
